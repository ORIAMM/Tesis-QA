import copy
from pathlib import Path
import unittest
from unittest.mock import patch

CONFIG = "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3_Split.yaml"
BASELINE = "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3_Balanced.yaml"


class SplitPhase3Tests(unittest.TestCase):
    def options(self, *args):
        from mlagents.trainers import learn
        return learn.parse_command_line([CONFIG, "--run-id=UnitTestSplitPhase3", *args])

    def test_pinned_source_full_compatibility_preflight(self):
        import train_phase2_terrain as driver
        from mlagents.trainers import learn
        with patch.object(learn, "parse_command_line", return_value=self.options()), patch.object(learn, "run_cli") as cli:
            driver.main(phase3=True, check_only=True)
            cli.assert_not_called()
        self.assertFalse(Path("results/UnitTestSplitPhase3").exists())

    def test_no_training_budget_no_start(self):
        import train_phase2_terrain as driver
        from mlagents.trainers import learn
        opts = self.options()
        opts.behaviors["Explorer"].max_steps = 0  # Simulate missing approval without changing the YAML.
        with patch.object(learn, "parse_command_line", return_value=opts), patch.object(learn, "run_cli") as cli:
            with self.assertRaisesRegex(ValueError, "No additional budget approved"):
                driver.main(phase3=True)
            cli.assert_not_called()

    def test_cannot_resume_phase3r_under_new_reward(self):
        import train_phase2_terrain as driver
        from mlagents.trainers import learn
        opts = learn.parse_command_line([CONFIG, "--run-id=Phase3R", "--resume"])
        with patch.object(learn, "parse_command_line", return_value=opts), patch.object(learn, "run_cli") as cli:
            with self.assertRaisesRegex(ValueError, "Run mode differs"):
                driver.main(phase3=True)
            cli.assert_not_called()

    def test_configuration_only_changes_revision_source_and_budget(self):
        import yaml
        before = yaml.safe_load(Path(BASELINE).read_text())
        after = yaml.safe_load(Path(CONFIG).read_text())
        expected = copy.deepcopy(before)
        expected["environment_parameters"]["phase3_reward_revision"] = 2
        expected["behaviors"]["Explorer"]["max_steps"] = 1000000
        from train_phase3 import SPLIT_SOURCE
        expected["behaviors"]["Explorer"]["init_path"] = SPLIT_SOURCE
        self.assertEqual(after, expected)

    def test_scene_and_builder_select_revision2(self):
        scene = Path("Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity").read_text()
        builder = Path("Assets/Scripts/AI/Editor/AITrainingSceneSetup.cs").read_text()
        self.assertEqual(scene.count("  phase3RewardRevision: 2\n"), 1)
        self.assertIn('"phase3RewardRevision", phase3Coverage ? 2 : 0', builder)

    def test_stair_milestones_still_present(self):
        # Values in the real scene are the same as the evaluated Phase3R scene.
        import yaml
        raw = Path("Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity").read_text()
        for field in ["firstStairSeenReward: 0", "firstStairTraversalReward: 0.08", "firstStairMidpointReward: 0.08", "firstOtherFloorReward: 0.2"]:
            self.assertIn(field, raw)


if __name__ == "__main__": unittest.main()
