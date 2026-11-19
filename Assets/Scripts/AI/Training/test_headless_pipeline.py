import importlib.util
from pathlib import Path
import unittest

from run_phase2_headless import choose_steps

MODULE = Path(__file__).resolve().parents[1] / "Evaluation/headless_report.py"
spec = importlib.util.spec_from_file_location("headless_report", MODULE)
report = importlib.util.module_from_spec(spec)
spec.loader.exec_module(report)


class HeadlessTests(unittest.TestCase):
    def test_checkpoint_selection_preserves_mid_late_final_and_deduplicates(self):
        steps = [250000, 500000, 1500000, 2750000, 3000072]
        self.assertEqual(choose_steps(steps, 3000072), [1500000, 2750000, 3000072])
        self.assertEqual(choose_steps([1024], 1024), [1024])
        with self.assertRaises(ValueError): choose_steps([], 100)

    def test_selection_does_not_reward_coverage_of_long_failed_episodes(self):
        good = {"ascent": {"rate": .8}, "descent": {"rate": .8}, "success": {"rate": .8},
                "minimum_floor": .01, "mean_steps": 1000}
        failed_explorer = {"ascent": {"rate": .1}, "descent": {"rate": .9}, "success": {"rate": .3},
                           "minimum_floor": .8, "mean_steps": 6000}
        self.assertGreater(report.selection_score(good), report.selection_score(failed_explorer))

    def test_stair_entries_are_not_counted_as_completed_transitions(self):
        episodes = [{"result": {"direction": 1, "success": False, "coverage": .2,
                     "floorZeroCoverage": .4, "floorOneCoverage": 0,
                     "stairTraversals": 1, "ascentTransitions": 0, "descentTransitions": 0,
                     "steps": 6000, "stepLimits": 1}}]
        result = report.aggregate(episodes)
        self.assertEqual(result["stair_entries"], 1)
        self.assertEqual(result["ascent_transitions"], 0)
        self.assertEqual(result["minimum_floor"], 0)


if __name__ == "__main__":
    unittest.main()
