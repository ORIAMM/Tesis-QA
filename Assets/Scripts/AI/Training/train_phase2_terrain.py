"""Phase2T: gradual coverage/transfer curriculum, exact episode mastery and safe resume.

uv run python Assets/Scripts/AI/Training/train_phase2_terrain.py <yaml> --run-id=Phase2T
Use the SAME command plus --resume to continue; initialization is cleared on resume.
"""
import copy
import json
from pathlib import Path

from terrain_curriculum import TerrainWindow, NAMES


def constant_parameter(parameters, name, default):
    if name not in parameters:
        return default
    lessons = parameters[name].curriculum
    if len(lessons) != 1 or not hasattr(lessons[0].value, "value"):
        raise ValueError(f"{name} must be one constant value.")
    return lessons[0].value.value


def resume_steps(checkpoint, status):
    import torch
    saved = torch.load(checkpoint, map_location="cpu")
    step = int(saved["global_step"]["_GlobalSteps__global_step"].item())
    explorer = status.get("Explorer", {})
    known = [x["steps"] for x in explorer.get("checkpoints", [])]
    if explorer.get("final_checkpoint"):
        known.append(explorer["final_checkpoint"]["steps"])
    if not known or step != max(known):
        raise ValueError(f"Resume checkpoint step {step} disagrees with training_status.json ({max(known) if known else 'missing'}). No files changed.")
    return step


def main(phase3=False, check_only=False):
    import mlagents.trainers
    from mlagents.trainers import learn
    from mlagents.trainers.environment_parameter_manager import EnvironmentParameterManager
    from mlagents.trainers.stats import StatsReporter, StatsWriter, StatsSummary
    from mlagents.trainers.training_status import GlobalTrainingStatus, StatusType
    from mlagents_envs.side_channel.stats_side_channel import StatsAggregationMethod
    from mlagents_envs.logging_util import get_logger

    if mlagents.trainers.__version__ != "1.1.0":
        raise RuntimeError("Terrain driver requires the project's mlagents==1.1.0.")
    options = learn.parse_command_line()
    parameters = options.environment_parameters
    key = "terrain_level"
    levels = [8, 9] if phase3 else list(range(12))
    state_key = "phase3_terrain" if phase3 else "phase2_terrain"
    mode_key = state_key + "_mode"
    if (key not in parameters or [x.value.value for x in parameters[key].curriculum] != levels
            or "terrain_curriculum" not in parameters
            or parameters["terrain_curriculum"].curriculum[0].value.value != 1):
        raise ValueError("Use Explorer_Phase3.yaml." if phase3 else "Use Explorer_Phase2_Terrain.yaml.")
    phase3_flag = constant_parameter(parameters, "phase3_curriculum", 0)
    if phase3_flag != int(phase3):
        raise ValueError("Phase 3 requires train_phase3.py; do not interchange Phase 2/3 drivers.")
    if options.env_settings.num_envs != 1 or options.env_settings.num_areas != 1:
        raise ValueError("Terrain episode packets support one environment/area only.")
    settings = options.checkpoint_settings
    if settings.force or settings.initialize_from:
        raise ValueError("Use a new run-id, or --resume. --force/--initialize-from are not accepted.")
    root = Path(__file__).resolve().parents[4]
    run = Path(settings.write_path).resolve()
    if run.parent != (root / "results").resolve() or run.name != settings.run_id:
        raise ValueError("Launch from project root with a simple run-id inside results.")
    behavior = options.behaviors["Explorer"]
    initial_level = constant_parameter(parameters, "terrain_start_level", 0)
    if initial_level not in levels:
        raise ValueError("terrain_start_level is outside the configured curriculum.")
    initial_level = int(initial_level)
    if "completed_branch_assist" not in parameters:
        parameters["completed_branch_assist"] = copy.deepcopy(parameters["terrain_curriculum"])
        parameters["completed_branch_assist"].curriculum[0].value.value = 0
    branch_assist = constant_parameter(parameters, "completed_branch_assist", 0)
    if branch_assist not in (0, 1):
        raise ValueError("completed_branch_assist must be 0 or 1.")
    run_mode = {"initial_level": initial_level, "completed_branch_assist": int(branch_assist)}
    if phase3:
        reward_revision = constant_parameter(parameters, "phase3_reward_revision", 0)
        if reward_revision not in (0, 1, 2):
            raise ValueError("Unsupported Phase 3 reward revision.")
        if reward_revision:
            run_mode["reward_revision"] = int(reward_revision)
    branch_observation = constant_parameter(parameters, "branch_memory_observation", 0)
    if branch_observation not in (0, 1) or branch_observation and branch_assist:
        raise ValueError("Branch observation must be 0/1 and cannot be combined with action assistance.")
    if "branch_memory_observation" not in parameters:
        parameters["branch_memory_observation"] = copy.deepcopy(parameters["terrain_curriculum"])
        parameters["branch_memory_observation"].curriculum[0].value.value = 0
    if branch_observation:
        run_mode["branch_memory_observation"] = 1
    distant_observation = constant_parameter(parameters, "terrain_distant_observation", 0)
    if distant_observation not in (0, 1) or distant_observation and not branch_observation:
        raise ValueError("Distant terrain observation must be 0/1 and requires branch observation (161-input schema).")
    if "terrain_distant_observation" not in parameters:
        parameters["terrain_distant_observation"] = copy.deepcopy(parameters["terrain_curriculum"])
        parameters["terrain_distant_observation"].curriculum[0].value.value = 0
    if distant_observation:
        run_mode["terrain_distant_observation"] = 1
    ramp_observation = constant_parameter(parameters, "ramp_memory_observation", 0)
    if ramp_observation not in (0, 1) or ramp_observation and not distant_observation:
        raise ValueError("Ramp memory must be 0/1 and requires distant terrain (177-input schema).")
    if "ramp_memory_observation" not in parameters:
        parameters["ramp_memory_observation"] = copy.deepcopy(parameters["terrain_curriculum"])
        parameters["ramp_memory_observation"].curriculum[0].value.value = 0
    if ramp_observation:
        run_mode["ramp_memory_observation"] = 1
    if settings.resume:
        saved = json.loads((run / "run_logs/training_status.json").read_text(encoding="utf-8-sig"))
        previous_mode = saved.get(mode_key, {"initial_level": 0, "completed_branch_assist": 0})
        if previous_mode != run_mode:
            raise ValueError("Run mode differs from its saved start level/branch assistance. Use the original YAML or a NEW run-id.")
        level = levels[saved[key]["lesson_num"]]
        TerrainWindow.restore(saved.get(state_key), level)
        step = resume_steps(run / "Explorer/checkpoint.pt", saved)
        if distant_observation:
            import onnx
            import torch
            model = onnx.load(str(run / "Explorer.onnx"))
            shapes = [x.type.tensor_type.shape.dim[-1].dim_value for x in model.graph.input
                      if x.name.startswith("obs_")]
            state = torch.load(run / "Explorer/checkpoint.pt", map_location="cpu")
            weight = state["Policy"]["network_body._body_endoder.seq_layers.0.weight"]
            expected = [24, 34, 49, 10, 32, 12] + ([16] if ramp_observation else [])
            if shapes != expected or tuple(weight.shape) != (256, sum(expected)):
                raise ValueError(f"Resume requires validated {sum(expected)}-input migration; refusing partial model load.")
        # ML-Agents prioritizes init_path over resume. Never reload Phase 1 here!
        behavior.init_path = None
        if step >= behavior.max_steps:
            raise ValueError(f"Checkpoint already at {step}; max_steps={behavior.max_steps}. Raise the budget explicitly before resuming.")
        print(f"Resume verified: {run / 'Explorer/checkpoint.pt'} at {step} steps, terrain level {level}.", flush=True)
    else:
        if run.exists():
            raise ValueError("Run exists; use --resume or a new run-id. Nothing was overwritten.")
        initial = Path(behavior.init_path or "")
        if not behavior.init_path or not initial.is_file():
            raise ValueError("Missing initialization checkpoint.")
        # Check the pinned export's observation schema before the trainer can
        # silently partially initialize an incompatible old model.
        import onnx
        model = onnx.load(str(initial.with_suffix(".onnx")))
        shapes = [x.type.tensor_type.shape.dim[-1].dim_value for x in model.graph.input
                  if x.name.startswith("obs_")]
        expected_shapes = [24, 34, 49, 10] + ([32] if branch_observation else []) + ([12] if distant_observation else []) + ([16] if ramp_observation else [])
        if shapes != expected_shapes:
            raise ValueError(f"Initialization schema {shapes} != expected {expected_shapes}. Use explicit migration for the 32-value branch sensor.")
        print(f"Initialize NEW run from {initial}; new step counter starts at 0, terrain level {initial_level}, branch assistance {bool(branch_assist)}, branch observation {bool(branch_observation)}.", flush=True)
    if phase3:
        from train_phase3 import validate_phase3
        validate_phase3(options, run, resume=settings.resume)
    if check_only:
        print("Preflight passed. No Unity/trainer launched and no run files written.", flush=True)
        if behavior.max_steps <= 0:
            print("Training BLOCKED: no additional budget approved (max_steps=0).", flush=True)
        return
    if phase3 and behavior.max_steps <= 0:
        raise ValueError("No additional budget approved. Set max_steps only after the user chooses a budget; no training started.")
    parameters["terrain_driver"] = copy.deepcopy(parameters["terrain_curriculum"])
    logger = get_logger(state_key)
    bank = TerrainWindow()

    def persist():
        GlobalTrainingStatus.saved_state[state_key] = bank.state()
        GlobalTrainingStatus.saved_state[mode_key] = run_mode.copy()

    class TerrainWriter(StatsWriter):
        def on_add_stat(self, category, stat, value, aggregation=StatsAggregationMethod.AVERAGE):
            if category == "Explorer" and stat == "AI/Terrain Outcome":
                bank.add(value)
                persist()

        def write_stats(self, category, values, step):
            if category != "Explorer":
                return
            summary = bank.summary()
            for label, value in {"level": bank.level, "mastered": bank.ready(), **summary}.items():
                values["AI/TerrainMastery/" + label] = StatsSummary([float(value)], StatsAggregationMethod.AVERAGE)
            logger.info(f"Terrain {bank.level} ({NAMES[bank.level]}): {summary['episodes']}/100 main episodes; "
                        f"success {summary['success']:.0%}; up/down {summary['up_success']:.0%}/{summary['down_success']:.0%}; "
                        f"far {summary['far_success']:.0%} (n={summary['far_trials']}).")

    class TerrainManager(EnvironmentParameterManager):
        def __init__(self, settings=None, run_seed=-1, restore=False):
            nonlocal bank
            super().__init__(settings, run_seed, restore)
            if not restore:
                GlobalTrainingStatus.set_parameter_state(key, StatusType.LESSON_NUM, levels.index(initial_level))
            level = levels[GlobalTrainingStatus.get_parameter_state(key, StatusType.LESSON_NUM)]
            bank = (TerrainWindow.restore(GlobalTrainingStatus.saved_state.get(state_key), level)
                    if restore else TerrainWindow(level))
            persist()

        def update_lessons(self, trainer_steps, trainer_max_steps, trainer_reward_buffer):
            nonlocal bank
            with StatsReporter.lock:
                if not bank.ready() or bank.level == levels[-1]:
                    return False, False
                logger.info(f"Terrain mastery passed {bank.level} -> {bank.level + 1}: {bank.summary()}")
                bank = TerrainWindow(bank.level + 1)
                GlobalTrainingStatus.set_parameter_state(key, StatusType.LESSON_NUM, levels.index(bank.level))
                persist()
            self.log_current_lesson(key)
            return True, True

    StatsReporter.add_writer(TerrainWriter())
    learn.EnvironmentParameterManager = TerrainManager
    learn.run_cli(options)


if __name__ == "__main__":
    main()
