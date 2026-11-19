import copy
import hashlib
from pathlib import Path
import unittest
from unittest.mock import patch

CONFIG = "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3.yaml"


class Phase3Tests(unittest.TestCase):
    def test_episode_limit_and_rebuild_are_aligned(self):
        scene = Path("Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity").read_text()
        builder = Path("Assets/Scripts/AI/Editor/AITrainingSceneSetup.cs").read_text()
        self.assertIn("  MaxStep: 12000\n", scene)
        self.assertIn("AITrainingPhase.MultiroomNavigation ? 4000 : 12000,", builder)

    def options(self, *args):
        from mlagents.trainers import learn
        return learn.parse_command_line([CONFIG, "--run-id=UnitTestPhase3", *args])

    def test_real_checkpoint_full_compatibility_read_only(self):
        from train_phase3 import SOURCE, SOURCE_SHA256
        import train_phase2_terrain as driver
        from mlagents.trainers import learn
        with patch.object(learn, "parse_command_line", return_value=self.options()), \
             patch.object(learn, "run_cli") as cli:
            driver.main(phase3=True, check_only=True)
            cli.assert_not_called()
        self.assertEqual(hashlib.sha256(Path(SOURCE).read_bytes()).hexdigest(), SOURCE_SHA256)
        self.assertFalse(Path("results/UnitTestPhase3").exists())

    def test_phase2_driver_rejects_phase3_config(self):
        import train_phase2_terrain as driver
        from mlagents.trainers import learn
        with patch.object(learn, "parse_command_line", return_value=self.options()), \
             patch.object(learn, "run_cli") as cli:
            with self.assertRaises(ValueError): driver.main()
            cli.assert_not_called()

    def test_budget_flags_and_schema(self):
        opts = self.options(); b = opts.behaviors["Explorer"]
        self.assertEqual(b.max_steps, 6000000)
        self.assertEqual(opts.engine_settings.time_scale, 40)
        self.assertEqual([x.value.value for x in opts.environment_parameters["terrain_level"].curriculum], [8, 9])
        self.assertEqual(b.network_settings.memory.memory_size, 128)
        self.assertEqual(b.hyperparameters.learning_rate, 1e-4)
        self.assertEqual(b.checkpoint_interval, 250000)

    def test_mastery_indices_restore_and_no_automatic_reward_promotion(self):
        from mlagents.trainers import learn
        from mlagents.trainers.stats import StatsReporter
        from mlagents.trainers.training_status import GlobalTrainingStatus
        import train_phase2_terrain as driver
        original = copy.deepcopy(GlobalTrainingStatus.saved_state)
        writers, manager_type = list(StatsReporter.writers), learn.EnvironmentParameterManager

        def fake(opts):
            manager = learn.EnvironmentParameterManager(opts.environment_parameters)
            self.assertEqual(manager.get_current_samplers()["terrain_level"].value, 8)
            self.assertEqual(GlobalTrainingStatus.saved_state["terrain_level"]["lesson_num"], 0)
            writer = StatsReporter.writers[-1]
            for _ in range(100): writer.on_add_stat("Explorer", "AI/Terrain Outcome", 819)
            self.assertEqual(manager.update_lessons({"Explorer": 999999}, {}, {"Explorer": [9999999]}), (False, False))
            for code in [803] * 50 + [805] * 49:
                writer.on_add_stat("Explorer", "AI/Terrain Outcome", code)
            self.assertEqual(manager.update_lessons({}, {}, {}), (False, False))
            writer.on_add_stat("Explorer", "AI/Terrain Outcome", 805)
            self.assertEqual(manager.update_lessons({}, {}, {}), (True, True))
            self.assertEqual(GlobalTrainingStatus.saved_state["terrain_level"]["lesson_num"], 1)
            manager = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
            self.assertEqual(manager.get_current_samplers()["terrain_level"].value, 9)
            for code in [903] * 50 + [905] * 50:
                writer.on_add_stat("Explorer", "AI/Terrain Outcome", code)
            self.assertEqual(manager.update_lessons({}, {}, {}), (False, False))
            self.assertEqual(GlobalTrainingStatus.saved_state["phase3_terrain"]["level"], 9)
            self.assertNotIn("phase2_terrain", GlobalTrainingStatus.saved_state)

        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(learn, "parse_command_line", return_value=self.options()), \
                 patch("train_phase3.validate_phase3"), patch.object(learn, "run_cli", side_effect=fake):
                driver.main(phase3=True)
        finally:
            GlobalTrainingStatus.saved_state.clear(); GlobalTrainingStatus.saved_state.update(original)
            StatsReporter.writers[:] = writers; learn.EnvironmentParameterManager = manager_type

    def test_force_is_rejected(self):
        from mlagents.trainers import learn
        import train_phase2_terrain as driver
        with patch.object(learn, "parse_command_line", return_value=self.options("--force")), \
             patch.object(learn, "run_cli") as cli:
            with self.assertRaisesRegex(ValueError, "not accepted"): driver.main(phase3=True)
            cli.assert_not_called()

    def test_phase3_resume_preserves_mastery_index_and_clears_source(self):
        import json
        import onnx
        from train_phase3 import SOURCE
        from terrain_curriculum import TerrainWindow
        from mlagents.trainers import learn
        from mlagents.trainers.stats import StatsReporter
        from mlagents.trainers.training_status import GlobalTrainingStatus
        import train_phase2_terrain as driver
        source_model = onnx.load(str(Path(SOURCE).with_suffix(".onnx")))
        saved = {"terrain_level": {"lesson_num": 1},
                 "phase3_terrain": TerrainWindow(9, [903, 905]).state(),
                 "phase3_terrain_mode": {"initial_level": 8, "completed_branch_assist": 0,
                     "branch_memory_observation": 1, "terrain_distant_observation": 1,
                     "ramp_memory_observation": 1}}
        original = copy.deepcopy(GlobalTrainingStatus.saved_state)
        writers, manager_type = list(StatsReporter.writers), learn.EnvironmentParameterManager
        import torch
        real_load = torch.load

        def fake(opts):
            self.assertIsNone(opts.behaviors["Explorer"].init_path)
            GlobalTrainingStatus.saved_state.update(saved)
            manager = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
            self.assertEqual(manager.get_current_samplers()["terrain_level"].value, 9)
            self.assertEqual(GlobalTrainingStatus.saved_state["phase3_terrain"]["samples"], [903, 905])

        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(learn, "parse_command_line", return_value=self.options("--resume")), \
                 patch.object(Path, "read_text", return_value=json.dumps(saved)), \
                 patch.object(driver, "resume_steps", return_value=300012), \
                 patch.object(torch, "load", side_effect=lambda *a, **k: real_load(SOURCE, map_location="cpu")), \
                 patch.object(onnx, "load", return_value=source_model), \
                 patch("train_phase3.validate_phase3"), patch.object(learn, "run_cli", side_effect=fake):
                driver.main(phase3=True)
        finally:
            GlobalTrainingStatus.saved_state.clear(); GlobalTrainingStatus.saved_state.update(original)
            StatsReporter.writers[:] = writers; learn.EnvironmentParameterManager = manager_type

    def test_phase2_resume_state_cannot_be_used_for_phase3(self):
        from mlagents.trainers import learn
        import train_phase2_terrain as driver
        with patch.object(learn, "parse_command_line", return_value=self.options("--resume")), \
             patch.object(Path, "read_text", return_value='{"phase2_terrain_mode": {"initial_level": 8}}'), \
             patch.object(learn, "run_cli") as cli:
            with self.assertRaisesRegex(ValueError, "Run mode differs"): driver.main(phase3=True)
            cli.assert_not_called()


if __name__ == "__main__": unittest.main()
