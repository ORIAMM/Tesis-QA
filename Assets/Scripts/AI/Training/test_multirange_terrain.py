import copy
import hashlib
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from prepare_multirange_terrain import BASELINE, BASELINE_SHA256, CONFIG, OLD_SHAPES, NEW_SHAPES, prepare
from prepare_branch_observation import make_policy, migrate_states


class MultiRangeTests(unittest.TestCase):
    @staticmethod
    def initial_state():
        # Recreate in memory: the LIVE Phase2D checkpoint is allowed to train!
        import torch
        from mlagents.trainers import learn
        from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
        settings = learn.parse_command_line([CONFIG, "--run-id=UnitTestMultiRange"]).behaviors["Explorer"]
        p = make_policy(settings, NEW_SHAPES); opt = TorchPPOOptimizer(p, settings)
        modules = {**p.get_modules(), **opt.get_modules()}
        source = torch.load(BASELINE, map_location="cpu")
        return migrate_states(source, {k:v.state_dict() for k,v in modules.items()},
            old_size=149, new_size=161, new_processor=5, preserve_step=True)[0]

    def test_source_is_immutable_and_old_weights_exactly_preserved(self):
        import torch
        source = Path(BASELINE)
        self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(), BASELINE_SHA256)
        old = torch.load(source, map_location="cpu")
        new = self.initial_state()
        self.assertEqual(int(new["global_step"]["_GlobalSteps__global_step"].item()), 2000052)
        self.assertEqual(new["Optimizer:value_optimizer"]["state"], {})
        expanded = 0
        for module in ["Policy", "Optimizer:critic", "Module:Curiosity"]:
            for key, value in old[module].items():
                actual = new[module][key]
                if value.shape != actual.shape:
                    self.assertTrue(key.endswith("_body_endoder.seq_layers.0.weight"))
                    self.assertEqual(tuple(actual.shape), (256, 161))
                    self.assertTrue(torch.equal(value, actual[:, :149]))
                    self.assertEqual(actual[:, 149:].count_nonzero().item(), 0)
                    expanded += 1
                else:
                    self.assertTrue(torch.equal(value, actual), module + "/" + key)
        self.assertEqual(expanded, 3)

    def test_export_schema_counter_and_resume_metadata(self):
        import onnx
        from train_phase2_terrain import resume_steps
        from terrain_curriculum import TerrainWindow
        from prepare_multirange_terrain import schema
        r = Path("results/Phase2D")
        model = onnx.load(str(r / "Explorer.onnx")); onnx.checker.check_model(model)
        self.assertEqual(schema(model), NEW_SHAPES)
        report = json.loads((r / "migration.json").read_text())
        self.assertLess(report["max_policy_and_value_encoding_error"], 1e-5)
        self.assertEqual(report["remaining_training_steps"], 2999948)
        status = json.loads((r / "run_logs/training_status.json").read_text())
        self.assertGreaterEqual(resume_steps(r / "Explorer/checkpoint.pt", status), 2000052)
        window = TerrainWindow.restore(status["phase2_terrain"], status["terrain_level"]["lesson_num"])
        self.assertLessEqual(window.summary()["episodes"], 100)

    def test_appended_input_has_gradient_and_updates(self):
        import torch
        from mlagents.trainers import learn
        from mlagents.trainers.ppo.optimizer_torch import TorchPPOOptimizer
        settings = learn.parse_command_line([CONFIG, "--run-id=TestMultiRange"]).behaviors["Explorer"]
        p = make_policy(settings, NEW_SHAPES)
        opt = TorchPPOOptimizer(p, settings)
        p.actor.load_state_dict(self.initial_state()["Policy"], strict=True)
        x = [torch.randn(4, n) for n in NEW_SHAPES]
        encoding, _ = p.actor.network_body(x, memories=torch.zeros(1, 4, p.actor.memory_size))
        encoding.square().sum().backward()
        weight = dict(p.actor.named_parameters())["network_body._body_endoder.seq_layers.0.weight"]
        self.assertGreater(weight.grad[:, 149:].abs().sum().item(), 0)
        opt.optimizer.step()
        self.assertGreater(weight[:, 149:].abs().sum().item(), 0)

    def test_existing_destination_refused(self):
        with self.assertRaisesRegex(ValueError, "Destination exists"):
            prepare()

    def test_new_config_does_not_resume_old_149_run(self):
        from mlagents.trainers import learn
        import train_phase2_terrain
        options = learn.parse_command_line([CONFIG, "--run-id=Phase2C", "--resume"])
        with patch.object(learn, "parse_command_line", return_value=options), patch.object(learn, "run_cli") as cli:
            with self.assertRaisesRegex(ValueError, "Run mode differs"):
                train_phase2_terrain.main()
            cli.assert_not_called()

    def test_prepared_run_passes_full_driver_preflight_without_launching_training(self):
        from mlagents.trainers import learn
        from mlagents.trainers.stats import StatsReporter
        from mlagents.trainers.training_status import GlobalTrainingStatus
        import train_phase2_terrain
        options = learn.parse_command_line([CONFIG, "--run-id=Phase2D", "--resume"])
        live = json.loads(Path("results/Phase2D/run_logs/training_status.json").read_text())
        # Test preflight with a mocked budget; do NOT extend the real run's YAML.
        budget = live["Explorer"]["final_checkpoint"]["steps"] + 100
        options.behaviors["Explorer"].max_steps = budget
        previous = copy.deepcopy(GlobalTrainingStatus.saved_state)
        writers, manager_type = list(StatsReporter.writers), learn.EnvironmentParameterManager
        def fake_cli(opts):
            self.assertIsNone(opts.behaviors["Explorer"].init_path)
            self.assertEqual(opts.behaviors["Explorer"].max_steps, budget)
            GlobalTrainingStatus.load_state("results/Phase2D/run_logs/training_status.json")
            manager = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
            samplers = manager.get_current_samplers()
            self.assertEqual(samplers["terrain_level"].value, live["terrain_level"]["lesson_num"])
            self.assertEqual(samplers["terrain_distant_observation"].value, 1)
        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(learn, "parse_command_line", return_value=options), patch.object(learn, "run_cli", side_effect=fake_cli) as cli:
                train_phase2_terrain.main()
                cli.assert_called_once()
        finally:
            GlobalTrainingStatus.saved_state.clear(); GlobalTrainingStatus.saved_state.update(previous)
            StatsReporter.writers[:] = writers; learn.EnvironmentParameterManager = manager_type

    def test_rewards_network_budget_time_scale_identical_to_phase2c(self):
        import yaml
        old = yaml.safe_load(Path("Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_BranchObservation.yaml").read_text())
        new = yaml.safe_load(Path(CONFIG).read_text())
        old["behaviors"]["Explorer"]["init_path"] = None
        new["environment_parameters"].pop("terrain_distant_observation")
        self.assertEqual(old, new)


if __name__ == "__main__":
    unittest.main()
