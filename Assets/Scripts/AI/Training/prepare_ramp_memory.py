"""Prepare 161 -> 177 ramp-memory observations in a NEW run; never starts training.

No source run is modified. Retains the source step counter and learned weights;
new input weights are zero, new normalizers/optimizer and mastery window fresh.
Never starts Unity or training. Refuses existing destinations and unpinned sources.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import shutil

from prepare_branch_observation import make_policy, migrate_states

OLD_SHAPES = [24, 34, 49, 10, 32, 12]
NEW_SHAPES = OLD_SHAPES + [16]
BASELINE = "results/RecoveryBackups/Phase2D_before_ramp_memory_5000036/Explorer-5000036.pt"
BASELINE_SHA256 = "bb1773069411f28eaf68f24ad80bea56c3f898510b26ffc3e562d44059d8ccd4"
CONFIG = "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_RampMemory.yaml"


def schema(model):
    return [i.type.tensor_type.shape.dim[-1].dim_value
            for i in model.graph.input if i.name.startswith("obs_")]


def prepare(destination="results/Phase2E", config=CONFIG):
    import onnx
    import torch
    from mlagents.trainers import learn
    from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
    from mlagents.trainers.model_saver.torch_model_saver import TorchModelSaver
    from mlagents.trainers.training_status import StatusMetaData
    from terrain_curriculum import TerrainWindow
    from train_phase2_terrain import resume_steps

    root = Path(__file__).resolve().parents[4]
    destination = Path(destination).resolve()
    if destination.parent != (root / "results").resolve():
        raise ValueError("Destination must be one NEW run directory directly under results.")
    if destination.exists():
        raise ValueError("Destination exists; refusing overwrite. Use the verified migration's --resume command.")
    source_path = root / BASELINE
    source_hash = hashlib.sha256(source_path.read_bytes()).hexdigest()
    if source_hash != BASELINE_SHA256:
        raise ValueError("Pinned pre-change baseline hash mismatch; refusing a different perception profile.")
    source_model = onnx.load(str(source_path.with_suffix(".onnx")))
    onnx.checker.check_model(source_model)
    if schema(source_model) != OLD_SHAPES:
        raise ValueError("Baseline must have the 161-input multi-range schema.")
    saved_status = json.loads((source_path.parent / "training_status.json").read_text())
    source = torch.load(source_path, map_location="cpu")
    step = int(source["global_step"]["_GlobalSteps__global_step"].item())
    if step != 5000036:
        raise ValueError("Unexpected baseline step.")
    options = learn.parse_command_line([str(config), "--run-id=" + destination.name, "--resume"])
    settings = options.behaviors["Explorer"]
    if settings.init_path:
        raise ValueError("Use the dedicated resume-only RampMemory config.")
    level = saved_status["terrain_level"]["lesson_num"]
    if options.environment_parameters["terrain_start_level"].curriculum[0].value.value != level:
        raise ValueError("Start level differs from source curriculum.")
    old = make_policy(settings, OLD_SHAPES)
    old_optimizer = TorchPPOOptimizer(old, settings)
    for name, module in {**old.get_modules(), **old_optimizer.get_modules()}.items():
        if isinstance(module, torch.nn.Module): module.load_state_dict(source[name], strict=True)
        else: module.load_state_dict(source[name])
    new = make_policy(settings, NEW_SHAPES)
    optimizer = TorchPPOOptimizer(new, settings)
    modules = {**new.get_modules(), **optimizer.get_modules()}
    state, expanded = migrate_states(source, {k: v.state_dict() for k, v in modules.items()},
        old_size=161, new_size=177, new_processor=6, preserve_step=True)
    for name, module in modules.items():
        if isinstance(module, torch.nn.Module): module.load_state_dict(state[name], strict=True)
        else: module.load_state_dict(state[name])

    # Verify deterministic policy encoding/LSTM and critic encoding, using nonzero
    # new observations. Policy action-distribution heads are copied unchanged.
    torch.manual_seed(54321)
    max_error = 0.0
    with torch.no_grad():
        for batch in (1, 4, 16):
            observations = [torch.randn(batch, n) for n in OLD_SHAPES]
            memory = torch.randn(1, batch, old.actor.memory_size)
            for a, b in [(old.actor, new.actor), (old_optimizer.critic, optimizer.critic)]:
                x, mx = a.network_body(observations, memories=memory)
                y, my = b.network_body(observations + [torch.randn(batch, 16)], memories=memory)
                error = (x - y).abs().max().item()
                if mx is not None: error = max(error, (mx - my).abs().max().item())
                max_error = max(max_error, error)
                if error > 1e-5: raise ValueError(f"Old policy/value preservation failed: {error}")

    # Preserve legacy env metadata, but not legacy checkpoint paths or mastery
    # samples. 100 NEW main episodes are required after this perception change.
    status = copy.deepcopy(saved_status)
    status["terrain_level"]["lesson_num"] = level
    status["terrain_distant_observation"] = {"lesson_num": 0}
    status["ramp_memory_observation"] = {"lesson_num": 0}
    status["phase2_terrain"] = TerrainWindow(level).state()
    status["phase2_terrain_mode"] = {"initial_level": level,
        "completed_branch_assist": 0, "branch_memory_observation": 1,
        "terrain_distant_observation": 1, "ramp_memory_observation": 1}
    status["metadata"] = StatusMetaData().to_dict()

    # Exclusive mkdir before the first write; validation above is all read-only.
    destination.mkdir()
    model_dir = destination / "Explorer"
    saver = TorchModelSaver(settings, str(model_dir))
    saver.register(new); saver.register(optimizer)
    saver.save_checkpoint("Explorer", step)
    exported = model_dir / f"Explorer-{step}.onnx"
    model = onnx.load(str(exported)); onnx.checker.check_model(model)
    if schema(model) != NEW_SHAPES: raise ValueError("Exported observation order differs from migration.")
    shutil.copyfile(exported, destination / "Explorer.onnx")
    entry = {"steps": step, "file_path": str(exported.relative_to(root)), "reward": None,
        "creation_time": exported.stat().st_mtime,
        "auxillary_file_paths": [str(exported.with_suffix(".pt").relative_to(root))]}
    status["Explorer"] = {"checkpoints": [entry], "final_checkpoint": {
        **entry, "file_path": str((destination / "Explorer.onnx").relative_to(root))}}
    logs = destination / "run_logs"; logs.mkdir()
    (logs / "training_status.json").write_text(json.dumps(status, indent=4), encoding="utf-8")
    shutil.copyfile(config, destination / "configuration.yaml")
    if resume_steps(model_dir / "checkpoint.pt", status) != step:
        raise ValueError("Prepared metadata/checkpoint mismatch.")
    if hashlib.sha256(source_path.read_bytes()).hexdigest() != source_hash:
        raise ValueError("Source baseline changed unexpectedly.")
    report = {"source": str(source_path), "source_sha256": source_hash,
        "source_step": step, "resume_step": step, "max_steps": settings.max_steps,
        "remaining_training_steps": max(0, settings.max_steps-step),
        "budget_increase_required": settings.max_steps <= step,
        "old_shapes": OLD_SHAPES, "new_shapes": NEW_SHAPES,
        "terrain_distances": [1.5, 3, 6], "expanded": expanded,
        "new_input_weights": "zero", "optimizer": "reset", "mastery_window": f"reset at level {level}",
        "max_policy_and_value_encoding_error": max_error,
        "training_started": False}
    (destination / "migration.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--destination", default="results/Phase2E")
    parser.add_argument("--config", default=CONFIG)
    args = parser.parse_args()
    prepare(args.destination, args.config)
