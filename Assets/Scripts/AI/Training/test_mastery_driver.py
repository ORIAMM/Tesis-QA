"""Integration with the installed 1.1.0 manager/stats/checkpoint APIs; no Unity needed."""
import sys
import json
from pathlib import Path
import unittest
from unittest.mock import patch

from mlagents.trainers import learn
from mlagents.trainers.stats import StatsReporter
from mlagents.trainers.training_status import GlobalTrainingStatus
from mlagents.trainers.settings import EnvironmentParameterSettings, Lesson, ConstantSettings
import train_phase2


class DriverTests(unittest.TestCase):
    def test_mixed_branch_starts_at_lesson_three_promotes_and_resumes_without_skipping_gate(self):
        options = learn.parse_command_line([
            "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_MixedSearch.yaml",
            "--run-id=UnitTestMixedSearch"])
        old_state = dict(GlobalTrainingStatus.saved_state)
        old_writers = list(StatsReporter.writers)
        old_manager = learn.EnvironmentParameterManager

        def fake_cli(opts):
            manager = learn.EnvironmentParameterManager(opts.environment_parameters)
            self.assertEqual(manager.get_current_samplers()["stair_search_lesson"].value, 3)
            self.assertEqual(manager.get_current_samplers()["stair_practice_level"].value, 2)
            self.assertEqual(manager.get_current_samplers()["mixed_search_driver"].value, 1)
            writer = StatsReporter.writers[-1]
            for level in range(5):
                self.assertEqual(manager.get_current_samplers()["mixed_search_level"].value, level)
                for _ in range(100): writer.on_add_stat("Explorer", "AI/Mastery Outcome", 13)
                self.assertEqual(manager.update_lessons({}, {}, {}), (False, False))
                codes = [level * 100 + 3] * 75 + [level * 100 + 1] * 25 if level == 0 else (
                    [level * 100 + 3] * 55 + [level * 100 + 7] * 20 + [level * 100 + 1] * 25)
                for code in codes:
                    writer.on_add_stat("Explorer", "AI/Mixed Search Outcome", code)
                self.assertEqual(manager.update_lessons({}, {}, {}), (True, True) if level < 4 else (False, False))
                # A restore keeps level/window instead of replaying level zero.
                manager = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
                self.assertEqual(manager.get_current_samplers()["mixed_search_level"].value, min(level + 1, 4))
                self.assertEqual(manager.get_current_samplers()["stair_search_lesson"].value, 3)
                if level < 4:
                    writer.on_add_stat("Explorer", "AI/Mixed Search Outcome", level * 100 + 3)
                    self.assertEqual(GlobalTrainingStatus.saved_state["phase2_mixed_search"]["samples"], [])
            values = {}
            writer.write_stats("Explorer", values, 999)
            self.assertEqual(values["AI/MixedSearch/mastered"].mean, 1)
        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(learn, "parse_command_line", return_value=options), patch.object(learn, "run_cli", fake_cli):
                train_phase2.main()
        finally:
            GlobalTrainingStatus.saved_state.clear()
            GlobalTrainingStatus.saved_state.update(old_state)
            StatsReporter.writers[:] = old_writers
            learn.EnvironmentParameterManager = old_manager

    def test_mixed_config_cannot_silently_resume_old_run(self):
        from phase2_mastery import MasteryWindow
        options = learn.parse_command_line([
            "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_MixedSearch_Resume.yaml",
            "--run-id=UnitTestMixedSearch", "--resume"])
        saved = {"phase2_mastery": MasteryWindow(3).state(), "stair_search_lesson": {"lesson_num": 3}}
        with patch.object(learn, "parse_command_line", return_value=options), patch.object(Path, "read_text", return_value=json.dumps(saved)):
            with self.assertRaisesRegex(ValueError, "separate run"):
                train_phase2.main()

    def test_near_search_migration_levels_and_resume_preserve_checkpoint(self):
        from phase2_mastery import MasteryWindow, StairPracticeWindow
        options = learn.parse_command_line([
            "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_StairSearch_Resume.yaml",
            "--run-id=UnitTestPhase2Mastery", "--resume"])
        saved = {"phase2_mastery": MasteryWindow(2, [23] * 75 + [21] * 25).state(),
                 "phase2_stair_practice": StairPracticeWindow(2, [23] * 80 + [22] * 20).state(),
                 "stair_search_lesson": {"lesson_num": 2},
                 "Explorer": {"final_checkpoint": {"steps": 5000102}}}
        old_state = dict(GlobalTrainingStatus.saved_state)
        old_writers = list(StatsReporter.writers)
        old_manager = learn.EnvironmentParameterManager

        def fake_cli(opts):
            GlobalTrainingStatus.saved_state.update(saved)
            manager = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
            self.assertEqual(manager.get_current_samplers()["near_search_level"].value, 0)
            self.assertEqual(GlobalTrainingStatus.saved_state["phase2_mastery"]["samples"], [])
            self.assertEqual(GlobalTrainingStatus.saved_state["phase2_stair_practice"], saved["phase2_stair_practice"])
            self.assertEqual(GlobalTrainingStatus.saved_state["Explorer"]["final_checkpoint"]["steps"], 5000102)
            writer = StatsReporter.writers[-1]
            for _ in range(100): writer.on_add_stat("Explorer", "AI/Mastery Outcome", 13)  # guided rehearsal
            self.assertEqual(manager.update_lessons({}, {}, {}), (False, False))
            for level in range(4):
                self.assertEqual(manager.get_current_samplers()["near_search_level"].value, level)
                for up in [True] * 75 + [False] * 25:
                    writer.on_add_stat("Explorer", "AI/Mastery Outcome", 23 if up else 21)
                    writer.on_add_stat("Explorer", "AI/Near Search Outcome", level * 10 + (3 if up else 1))
                self.assertEqual(manager.update_lessons({}, {}, {}), (True, True))
                self.assertEqual(manager.get_current_samplers()["stair_search_lesson"].value, 2 if level < 3 else 3)
                restored = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
                if level < 3:
                    self.assertEqual(restored.get_current_samplers()["near_search_level"].value, level + 1)
                    writer.on_add_stat("Explorer", "AI/Near Search Outcome", level * 10 + 3)
                    self.assertEqual(GlobalTrainingStatus.saved_state["phase2_near_search"]["samples"], [])
                manager = restored
            self.assertEqual(GlobalTrainingStatus.saved_state["Explorer"]["final_checkpoint"]["steps"], 5000102)
        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(learn, "parse_command_line", return_value=options), patch.object(Path, "read_text", return_value=json.dumps(saved)), patch.object(learn, "run_cli", fake_cli):
                train_phase2.main()
        finally:
            GlobalTrainingStatus.saved_state.clear()
            GlobalTrainingStatus.saved_state.update(old_state)
            StatsReporter.writers[:] = old_writers
            learn.EnvironmentParameterManager = old_manager

    def test_coverage_uses_own_lesson_index_and_restores_search_handoff(self):
        from phase2_mastery import MasteryWindow
        options = learn.parse_command_line([
            "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_StairSearch_Resume.yaml",
            "--run-id=UnitTestPhase2Mastery", "--resume"])
        params = options.environment_parameters
        previous = params.pop("stair_search_lesson")
        params["floor_lesson"] = EnvironmentParameterSettings(curriculum=[
            Lesson(value=ConstantSettings(value=4), name="Coverage10",
                   completion_criteria=previous.curriculum[0].completion_criteria),
            Lesson(value=ConstantSettings(value=5), name="Coverage20")])
        params["stair_eval_gate_passed"].curriculum[0].value.value = 1
        saved = {"phase2_mastery": MasteryWindow(3).state(),
                 "stair_search_lesson": {"lesson_num": 3}}
        old_state = dict(GlobalTrainingStatus.saved_state)
        old_writers = list(StatsReporter.writers)
        old_manager = learn.EnvironmentParameterManager

        def fake_cli(opts):
            GlobalTrainingStatus.saved_state.update(saved)
            manager = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
            writer = StatsReporter.writers[-1]
            for _ in range(75): writer.on_add_stat("Explorer", "AI/Mastery Outcome", 41)
            for _ in range(25): writer.on_add_stat("Explorer", "AI/Mastery Outcome", 40)
            self.assertEqual(manager.update_lessons({}, {}, {}), (True, True))
            self.assertEqual(manager.get_current_samplers()["floor_lesson"].value, 5)
            self.assertEqual(GlobalTrainingStatus.saved_state["floor_lesson"]["lesson_num"], 1)
            self.assertEqual(GlobalTrainingStatus.saved_state["phase2_mastery"]["stage"], 5)
        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(learn, "parse_command_line", return_value=options), patch.object(Path, "read_text", return_value=json.dumps(saved)), patch.object(learn, "run_cli", fake_cli):
                train_phase2.main()
        finally:
            GlobalTrainingStatus.saved_state.clear()
            GlobalTrainingStatus.saved_state.update(old_state)
            StatsReporter.writers[:] = old_writers
            learn.EnvironmentParameterManager = old_manager

    def test_gate_handoff_starts_fresh_coverage_window(self):
        from phase2_mastery import MasteryWindow
        saved = {"phase2_mastery": MasteryWindow(3, [33] * 100).state()}
        self.assertEqual(train_phase2.restore_window(saved, 4, True).summary()["episodes"], 0)
        with self.assertRaises(ValueError): train_phase2.restore_window(saved, 4, False)

    def test_real_parser_stats_delivery_advancement_and_resume(self):
        old_state = dict(GlobalTrainingStatus.saved_state)
        old_writers = list(StatsReporter.writers)
        old_manager = learn.EnvironmentParameterManager
        config = "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_StairSearch.yaml"

        def fake_cli(options):
            self.assertEqual(options.environment_parameters["mastery_driver"].curriculum[0].value.value, 1)
            manager = learn.EnvironmentParameterManager(options.environment_parameters)
            self.assertEqual(manager.get_current_samplers()["stair_search_lesson"].value, 0)
            # Even huge reward/step counts cannot promote a policy without mastery.
            self.assertEqual(manager.update_lessons({"Explorer": 10**9}, {"Explorer": 1}, {"Explorer": [10**9] * 100}), (False, False))
            writer = StatsReporter.writers[-1]
            # Guided stair rehearsals during single-floor training must not
            # satisfy the single-floor coverage mastery window.
            for _ in range(100): writer.on_add_stat("Explorer", "AI/Mastery Outcome", 13)
            self.assertEqual(manager.update_lessons({}, {}, {}), (False, False))
            for _ in range(75): writer.on_add_stat("Explorer", "AI/Mastery Outcome", 1)
            for _ in range(25): writer.on_add_stat("Explorer", "AI/Mastery Outcome", 0)
            self.assertEqual(manager.update_lessons({}, {}, {}), (True, True))
            self.assertEqual(manager.get_current_samplers()["stair_search_lesson"].value, 1)
            for level in range(3):
                self.assertEqual(manager.get_current_samplers()["stair_practice_level"].value, level)
                for success in [True] * 80 + [False] * 20:
                    writer.on_add_stat("Explorer", "AI/Mastery Outcome", 13 if success else 12)
                    writer.on_add_stat("Explorer", "AI/Stair Practice Outcome", level * 10 + (3 if success else 2))
                self.assertEqual(manager.update_lessons({}, {}, {}), (True, True))
                self.assertEqual(manager.get_current_samplers()["stair_search_lesson"].value, 1 if level < 2 else 2)
            for _ in range(50): writer.on_add_stat("Explorer", "AI/Mastery Outcome", 23)
            for _ in range(50): writer.on_add_stat("Explorer", "AI/Mastery Outcome", 20)
            self.assertEqual(manager.update_lessons({}, {}, {}), (False, False))
            restored = learn.EnvironmentParameterManager(options.environment_parameters, restore=True)
            self.assertEqual(restored.get_current_samplers()["stair_search_lesson"].value, 2)
            self.assertEqual(len(GlobalTrainingStatus.saved_state["phase2_mastery"]["samples"]), 100)
            values = {}
            writer.write_stats("Explorer", values, 123)
            self.assertEqual(values["AI/Mastery/up_success"].mean, 1)
            self.assertEqual(values["AI/Mastery/down_success"].mean, 0)

        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(sys, "argv", ["train_phase2.py", config, "--run-id=UnitTestPhase2Mastery"]), patch.object(learn, "run_cli", fake_cli):
                train_phase2.main()
        finally:
            GlobalTrainingStatus.saved_state.clear()
            GlobalTrainingStatus.saved_state.update(old_state)
            StatsReporter.writers[:] = old_writers
            learn.EnvironmentParameterManager = old_manager

    def test_adding_practice_on_resume_preserves_weights_step_and_main_lesson(self):
        from phase2_mastery import MasteryWindow
        options = learn.parse_command_line([
            "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase2_StairSearch_Resume.yaml",
            "--run-id=UnitTestPhase2Mastery", "--resume"])
        saved = {"phase2_mastery": MasteryWindow(1, [12] * 100).state(),
                 "stair_search_lesson": {"lesson_num": 1},
                 "Explorer": {"final_checkpoint": {"steps": 3000090}}}
        old_state = dict(GlobalTrainingStatus.saved_state)
        old_writers = list(StatsReporter.writers)
        old_manager = learn.EnvironmentParameterManager

        def fake_cli(opts):
            GlobalTrainingStatus.saved_state.update(saved)
            manager = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
            self.assertEqual(manager.get_current_samplers()["stair_search_lesson"].value, 1)
            self.assertEqual(manager.get_current_samplers()["stair_practice_level"].value, 0)
            self.assertEqual(GlobalTrainingStatus.saved_state["phase2_mastery"]["samples"], [])
            self.assertEqual(GlobalTrainingStatus.saved_state["Explorer"]["final_checkpoint"]["steps"], 3000090)
            writer = StatsReporter.writers[-1]
            for _ in range(100):
                writer.on_add_stat("Explorer", "AI/Mastery Outcome", 13)
                writer.on_add_stat("Explorer", "AI/Stair Practice Outcome", 3)
            self.assertEqual(manager.update_lessons({}, {}, {}), (True, True))
            restored = learn.EnvironmentParameterManager(opts.environment_parameters, restore=True)
            self.assertEqual(restored.get_current_samplers()["stair_practice_level"].value, 1)
            writer.on_add_stat("Explorer", "AI/Stair Practice Outcome", 3)  # stale old-level episode
            self.assertEqual(GlobalTrainingStatus.saved_state["phase2_stair_practice"]["samples"], [])
        try:
            GlobalTrainingStatus.saved_state.clear()
            with patch.object(learn, "parse_command_line", return_value=options), patch.object(Path, "read_text", return_value=json.dumps(saved)), patch.object(learn, "run_cli", fake_cli):
                train_phase2.main()
        finally:
            GlobalTrainingStatus.saved_state.clear()
            GlobalTrainingStatus.saved_state.update(old_state)
            StatsReporter.writers[:] = old_writers
            learn.EnvironmentParameterManager = old_manager


if __name__ == "__main__": unittest.main()
