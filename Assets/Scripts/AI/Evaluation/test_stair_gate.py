import copy
import unittest
from check_stair_gate import validate_report, coverage_config


def passing_report():
    seeds = sorted((913701, 921620, 929539, 937458, 945377))
    episodes = [{"mapSeed": seeds[i // 10], "result": {
        "lesson": 3, "direction": 1 if i < 38 else -1,
        "success": True, "spawnStairSteps": i % 5, "searchShapingActive": False,
    }} for i in range(50)]
    return {"model": "Phase2M4_test", "modelSha256": "a" * 64,
            "protocol": "Phase2-transfer-mixed-v2", "spawnSeed": 27421,
            "mapSeedBase": 913701, "inferenceSeed": 58231,
            "mapRegenerationInterval": 10, "deterministicInference": False,
            "requestedEpisodes": 50, "error": "", "episodes": episodes}


class GateTests(unittest.TestCase):
    def test_passing_and_exact_descent_threshold(self):
        report = passing_report()
        for i in (47, 48, 49): report["episodes"][i]["result"]["success"] = False
        summary, errors = validate_report(report, "a" * 64)
        self.assertEqual(errors, [])
        self.assertEqual(summary["descent"]["rate"], 0.75)

    def test_high_overall_reward_or_success_cannot_hide_failed_descent(self):
        report = passing_report()
        for e in report["episodes"][38:]: e["result"]["success"] = False
        self.assertTrue(validate_report(report)[1])

    def test_stale_model_or_missing_hash_is_rejected(self):
        self.assertTrue(validate_report(passing_report(), "b" * 64)[1])
        report = passing_report(); report.pop("modelSha256")
        self.assertTrue(validate_report(report)[1])

    def test_insufficient_or_invalid_protocol_is_rejected(self):
        for field, value in (("requestedEpisodes", 60), ("error", "inference failed"),
                             ("spawnSeed", 1), ("deterministicInference", True)):
            report = passing_report(); report[field] = value
            self.assertTrue(validate_report(report)[1])
        for field, value in (("searchShapingActive", True), ("lesson", 2), ("spawnStairSteps", 1)):
            report = passing_report()
            for e in report["episodes"]: e["result"][field] = value
            self.assertTrue(validate_report(report)[1])

    def test_coverage_switch_uses_independent_parameter_and_disables_shaping(self):
        source = {"behaviors": {"Explorer": {"init_path": "old", "max_steps": 2_000_000}},
                  "environment_parameters": {"stair_search_lesson": {"curriculum": []}, "stair_search_shaping": 1}}
        before = copy.deepcopy(source)
        config = coverage_config(source, 2_000_000)
        self.assertEqual(source, before)
        self.assertNotIn("init_path", config["behaviors"]["Explorer"])
        self.assertNotIn("stair_search_lesson", config["environment_parameters"])
        self.assertEqual(config["environment_parameters"]["stair_search_shaping"], 0)
        self.assertEqual(config["environment_parameters"]["stair_eval_gate_passed"], 1)
        self.assertEqual(config["environment_parameters"]["mastery_protocol"], 1)
        self.assertEqual([x["value"] for x in config["environment_parameters"]["floor_lesson"]["curriculum"]], [4, 5])


if __name__ == "__main__": unittest.main()
