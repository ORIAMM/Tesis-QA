import copy
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from terrain_curriculum import TerrainWindow


class TerrainTests(unittest.TestCase):
    def test_never_advance_on_steps_rewards_partial_windows_replay_or_stale_packets(self):
        window = TerrainWindow(4)
        for _ in range(200):
            window.add(419)  # Current-level rehearsal.
            window.add(303)  # Earlier-level main episode.
        self.assertEqual(len(window.samples), 0)
        for _ in range(99): window.add(403)
        self.assertFalse(window.ready())
        window.add(403)
        self.assertFalse(window.ready())  # No descent trials.

    def test_directional_mastery_cannot_hide_weak_descent(self):
        window = TerrainWindow(4, [403] * 80 + [404] * 20)
        self.assertEqual(window.summary()["success"], 0.8)
        self.assertFalse(window.ready())
        self.assertTrue(TerrainWindow(4, [403] * 50 + [405] * 50).ready())

    def test_far_search_requires_far_success_and_trials(self):
        self.assertFalse(TerrainWindow(7, [703] * 50 + [705] * 50).ready())
        good = [703] * 35 + [705] * 35 + [711] * 15 + [713] * 15
        self.assertTrue(TerrainWindow(7, good).ready())
        weak_far = [703] * 40 + [705] * 40 + [710] * 10 + [712] * 10
        self.assertFalse(TerrainWindow(7, weak_far).ready())

    def test_balanced_coverage_requires_both_spawn_floors(self):
        self.assertFalse(TerrainWindow(8, [803] * 100).ready())
        self.assertTrue(TerrainWindow(8, [803] * 50 + [805] * 50).ready())

    def test_restore_preserves_window_and_rejects_wrong_schema(self):
        window = TerrainWindow(6, [603] * 25 + [605] * 25)
        self.assertEqual(TerrainWindow.restore(window.state(), 6).state(), window.state())
        with self.assertRaises(ValueError): TerrainWindow.restore(window.state(), 7)
        with self.assertRaises(ValueError): TerrainWindow.restore(None, 0)
        for value in [-1, 32, 1200, 3.5]:
            with self.assertRaises(ValueError): TerrainWindow().add(value)

    def test_driver_promotes_one_level_at_a_time_and_preserves_restores(self):
        from mlagents.trainers import learn
        from mlagents.trainers.stats import StatsReporter
        from mlagents.trainers.training_status import GlobalTrainingStatus
        import train_phase2_terrain
        options = learn.parse_command_line([
            "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_Terrain.yaml", "--run-id=UnitTestTerrain"])
        old_state, old_writers = copy.deepcopy(GlobalTrainingStatus.saved_state), list(StatsReporter.writers)
        old_manager = learn.EnvironmentParameterManager

        def fake_cli(opts):
            manager = learn.EnvironmentParameterManager(opts.environment_parameters)
            self.assertEqual(manager.get_current_samplers()["terrain_driver"].value, 1)
            self.assertEqual(manager.get_current_samplers()["completed_branch_assist"].value, 0)
            writer = StatsReporter.writers[-1]
            for level in range(12):
                self.assertEqual(manager.get_current_samplers()["terrain_level"].value, level)
                self.assertEqual(manager.update_lessons({"Explorer": 999999999}, {}, {"Explorer": [999]}), (False, False))
                main = ([3] * 35 + [5] * 35 + [11] * 15 + [13] * 15) if level in (6, 7) else [3] * 50 + [5] * 50
                for value in main: writer.on_add_stat("Explorer", "AI/Terrain Outcome", level * 100 + value)
                self.assertEqual(manager.update_lessons({}, {}, {}), (True, True) if level < 11 else (False, False))
                manager = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
                self.assertEqual(manager.get_current_samplers()["terrain_level"].value, min(level + 1, 11))
            self.assertEqual(GlobalTrainingStatus.saved_state["phase2_terrain"]["level"], 11)
        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(learn, "parse_command_line", return_value=options), patch.object(learn, "run_cli", fake_cli):
                train_phase2_terrain.main()
        finally:
            GlobalTrainingStatus.saved_state.clear(); GlobalTrainingStatus.saved_state.update(old_state)
            StatsReporter.writers[:] = old_writers
            learn.EnvironmentParameterManager = old_manager

    def test_resume_clears_phase1_init_path_without_resetting_level(self):
        from mlagents.trainers import learn
        from mlagents.trainers.stats import StatsReporter
        import train_phase2_terrain
        options = learn.parse_command_line([
            "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_Terrain.yaml", "--run-id=UnitTestTerrain", "--resume"])
        saved = {"terrain_level": {"lesson_num": 5}, "phase2_terrain": TerrainWindow(5, [503]).state()}
        old_writers, old_manager = list(StatsReporter.writers), learn.EnvironmentParameterManager
        try:
            with patch.object(learn, "parse_command_line", return_value=options), \
                 patch.object(Path, "read_text", return_value=json.dumps(saved)), \
                 patch.object(train_phase2_terrain, "resume_steps", return_value=310123), \
                 patch.object(learn, "run_cli") as cli:
                train_phase2_terrain.main()
                self.assertIsNone(cli.call_args[0][0].behaviors["Explorer"].init_path)
        finally:
            StatsReporter.writers[:] = old_writers
            learn.EnvironmentParameterManager = old_manager

    def test_resume_refuses_checkpoint_behind_status(self):
        import torch
        from train_phase2_terrain import resume_steps
        saved = {"global_step": {"_GlobalSteps__global_step": torch.tensor([310123])}}
        with patch.object(torch, "load", return_value=saved):
            self.assertEqual(resume_steps("dummy", {"Explorer": {"checkpoints": [{"steps": 310123}]}}), 310123)
            with self.assertRaisesRegex(ValueError, "disagrees"):
                resume_steps("dummy", {"Explorer": {"checkpoints": [{"steps": 500000}]}})

    def test_branch_pilot_starts_at_eight_and_resume_keeps_advanced_level(self):
        from mlagents.trainers import learn
        from mlagents.trainers.stats import StatsReporter
        from mlagents.trainers.training_status import GlobalTrainingStatus, StatusType
        import train_phase2_terrain
        options = learn.parse_command_line([
            "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_Branch.yaml", "--run-id=UnitTestTerrainBranch"])
        old_state, old_writers = copy.deepcopy(GlobalTrainingStatus.saved_state), list(StatsReporter.writers)
        old_manager = learn.EnvironmentParameterManager

        def fake_cli(opts):
            self.assertEqual(opts.behaviors["Explorer"].max_steps, 2000000)
            self.assertTrue(opts.behaviors["Explorer"].init_path.endswith("Explorer-8000127.pt"))
            manager = learn.EnvironmentParameterManager(opts.environment_parameters)
            self.assertEqual(manager.get_current_samplers()["terrain_level"].value, 8)
            self.assertEqual(manager.get_current_samplers()["completed_branch_assist"].value, 1)
            self.assertEqual(GlobalTrainingStatus.saved_state["phase2_terrain"]["samples"], [])
            writer = StatsReporter.writers[-1]
            for value in [803] * 50 + [805] * 50:
                writer.on_add_stat("Explorer", "AI/Terrain Outcome", value)
            self.assertEqual(manager.update_lessons({}, {}, {}), (True, True))
            self.assertEqual(GlobalTrainingStatus.get_parameter_state("terrain_level", StatusType.LESSON_NUM), 9)
            manager = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
            self.assertEqual(manager.get_current_samplers()["terrain_level"].value, 9)
            self.assertEqual(GlobalTrainingStatus.saved_state["phase2_terrain_mode"],
                             {"initial_level": 8, "completed_branch_assist": 1})
        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(learn, "parse_command_line", return_value=options), patch.object(learn, "run_cli", fake_cli):
                train_phase2_terrain.main()
        finally:
            GlobalTrainingStatus.saved_state.clear(); GlobalTrainingStatus.saved_state.update(old_state)
            StatsReporter.writers[:] = old_writers
            learn.EnvironmentParameterManager = old_manager

    def test_branch_pilot_cannot_resume_baseline_under_new_mode(self):
        from mlagents.trainers import learn
        import train_phase2_terrain
        options = learn.parse_command_line([
            "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_Branch.yaml", "--run-id=Phase2T", "--resume"])
        saved = {"terrain_level": {"lesson_num": 8}, "phase2_terrain": TerrainWindow(8).state()}
        with patch.object(learn, "parse_command_line", return_value=options), \
             patch.object(Path, "read_text", return_value=json.dumps(saved)), \
             patch.object(learn, "run_cli") as cli:
            with self.assertRaisesRegex(ValueError, "Run mode differs"):
                train_phase2_terrain.main()
            cli.assert_not_called()

    def test_branch_resume_clears_initialization_and_keeps_saved_window(self):
        from mlagents.trainers import learn
        from mlagents.trainers.stats import StatsReporter
        import train_phase2_terrain
        options = learn.parse_command_line([
            "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_Branch.yaml", "--run-id=UnitTestTerrainBranch", "--resume"])
        saved = {"terrain_level": {"lesson_num": 9}, "phase2_terrain": TerrainWindow(9, [903]).state(),
                 "phase2_terrain_mode": {"initial_level": 8, "completed_branch_assist": 1}}
        old_writers, old_manager = list(StatsReporter.writers), learn.EnvironmentParameterManager
        try:
            with patch.object(learn, "parse_command_line", return_value=options), \
                 patch.object(Path, "read_text", return_value=json.dumps(saved)), \
                 patch.object(train_phase2_terrain, "resume_steps", return_value=310123), \
                 patch.object(learn, "run_cli") as cli:
                train_phase2_terrain.main()
                self.assertIsNone(cli.call_args[0][0].behaviors["Explorer"].init_path)
                self.assertEqual(saved["phase2_terrain"]["level"], 9)
        finally:
            StatsReporter.writers[:] = old_writers
            learn.EnvironmentParameterManager = old_manager


if __name__ == "__main__":
    unittest.main()
