from pathlib import Path
import unittest
from unittest.mock import patch

CONFIG = "Assets/Scripts/AI/TrainingConfigs/Explorer_Phase3_Balanced.yaml"


class BalancedPhase3Tests(unittest.TestCase):
    def options(self, *args):
        from mlagents.trainers import learn
        return learn.parse_command_line([CONFIG, "--run-id=UnitTestBalancedPhase3", *args])

    def test_exact_source_177_schema_without_starting_training(self):
        import train_phase2_terrain as driver
        from mlagents.trainers import learn
        with patch.object(learn, "parse_command_line", return_value=self.options()), patch.object(learn, "run_cli") as cli:
            driver.main(phase3=True, check_only=True)
            cli.assert_not_called()
        self.assertFalse(Path("results/UnitTestBalancedPhase3").exists())

    def test_no_automatic_training_without_budget(self):
        import train_phase2_terrain as driver
        from mlagents.trainers import learn
        opts = self.options()
        opts.behaviors["Explorer"].max_steps = 0  # Simulate missing approval, never edit the actual YAML.
        with patch.object(learn, "parse_command_line", return_value=opts), patch.object(learn, "run_cli") as cli:
            with self.assertRaisesRegex(ValueError, "No additional budget approved"):
                driver.main(phase3=True)
            cli.assert_not_called()

    def test_legacy_run_cannot_be_resumed_under_new_reward(self):
        import train_phase2_terrain as driver
        from mlagents.trainers import learn
        opts = learn.parse_command_line([CONFIG, "--run-id=Phase3", "--resume"])
        with patch.object(learn, "parse_command_line", return_value=opts), patch.object(learn, "run_cli") as cli:
            with self.assertRaisesRegex(ValueError, "Run mode differs"):
                driver.main(phase3=True)
            cli.assert_not_called()

    def test_no_architecture_gamma_or_learning_rate_change(self):
        opts = self.options(); b = opts.behaviors["Explorer"]
        self.assertEqual(b.max_steps, 2000000)
        self.assertEqual(opts.engine_settings.time_scale, 40)
        self.assertEqual(b.hyperparameters.learning_rate, 1e-4)
        self.assertEqual(b.network_settings.memory.memory_size, 128)
        self.assertEqual(b.network_settings.memory.sequence_length, 64)
        self.assertEqual(b.time_horizon, 128)
        for signal in b.reward_signals.values(): self.assertEqual(signal.gamma, .99)

    def test_reward_helper_never_controls_policy_or_observations(self):
        code = Path("Assets/Scripts/AI/AIPhase3CoverageReward.cs").read_text()
        for forbidden in ["motor.Move(", "SetDestination(", "CollectObservations(", "VectorSensor", "WFCGenerator"]:
            self.assertNotIn(forbidden, code)


if __name__ == "__main__": unittest.main()
