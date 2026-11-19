import json
from pathlib import Path
import unittest
from summarize_episode_budget import MODEL_SHA256, compare, validate_pair


def fixture():
    reports = {}
    for label, maximum in [("steps7500", 7500), ("steps12000", 12000)]:
        episodes = []
        for i in range(20):
            result = dict(success=True, steps=7000 if maximum == 7500 else 8500,
                lesson=4, searchShapingActive=False, stairPracticeGuided=False, stairRehearsal=False,
                startFloor=i%2, floorZeroCoverage=0.12, floorOneCoverage=0.10,
                ascentTransitions=1, descentTransitions=1, stairTraversals=1,
                revisits=2, shortLoops=0, collisions=0, noNewAreaTimeouts=0,
                noDisplacementFailures=0, stepLimits=0)
            episodes.append(dict(episode=dict(episode=i+1, mapSeed=1427003 + (i//4)*7919,
                spawn=dict(x=float(i),y=4.0*(i%2),z=0.0), target=dict(x=1.0,y=0.0,z=0.0),
                spawnYaw=60.0,result=result), has7500Snapshot=maximum==12000,
                snapshotStep=7500,snapshotFloor0=0.09,snapshotFloor1=0.08))
        reports[label] = dict(protocol="phase3-paired-budget-level8-v1", modelSha256=MODEL_SHA256,
            model="Phase3_4000073", deterministicInference=True, requestedEpisodes=20,
            maxStep=maximum,mapRegenerationInterval=4,mapSeedBase=1427003,spawnSeed=27421,
            inferenceSeed=58231,fixedDeltaTime=0.02,timeScale=40,coverageTarget=0.1,
            maximumFloorDifference=0.1,error=None,episodes=episodes)
    return reports


class BudgetTests(unittest.TestCase):
    def test_actual_forty_episodes_are_complete_and_paired(self):
        directory = Path("results/Phase3/budget_validation_20261027")
        reports = {label: json.loads((directory/(label+".json")).read_text())
                   for label in ("steps7500", "steps12000")}
        s = compare(reports)
        self.assertTrue(s["paired_seed_spawn_target_yaw_verified"])
        self.assertEqual(s["results"]["steps7500"]["aggregate"]["successes"], 2)
        self.assertEqual(s["results"]["steps12000"]["aggregate"]["successes"], 9)
        self.assertEqual(s["paired"]["new_successes"], 8)
        self.assertEqual(s["paired"]["lost_successes"], 1)

    def test_valid_pair_and_long_progress(self):
        s = compare(fixture())
        self.assertEqual(s["paired"]["new_successes"], 0)
        self.assertEqual(s["long_run_after_7500"]["improved"], 20)
        self.assertAlmostEqual(s["long_run_after_7500"]["mean_minimum_floor_gain"], 0.02)

    def test_different_spawns_are_rejected(self):
        r = fixture(); r["steps12000"]["episodes"][0]["episode"]["spawn"]["z"] = 1
        with self.assertRaisesRegex(ValueError, "Unpaired spawn"): validate_pair(r)

    def test_different_seeds_are_rejected(self):
        r = fixture()
        for x in r["steps12000"]["episodes"][:4]: x["episode"]["mapSeed"] += 1
        with self.assertRaisesRegex(ValueError, "Unpaired map"): validate_pair(r)

    def test_different_yaw_is_rejected(self):
        r = fixture(); r["steps12000"]["episodes"][0]["episode"]["spawnYaw"] = 61
        with self.assertRaisesRegex(ValueError, "Unpaired yaw"): validate_pair(r)

    def test_success_needs_both_floors_and_transfer(self):
        for changes in [dict(floorOneCoverage=0.01),dict(ascentTransitions=0,descentTransitions=0)]:
            r = fixture(); r["steps7500"]["episodes"][0]["episode"]["result"].update(changes)
            with self.assertRaisesRegex(ValueError, "Success violates"): validate_pair(r)

    def test_missing_snapshot_and_nan_rejected(self):
        r = fixture(); r["steps12000"]["episodes"][0]["has7500Snapshot"] = False
        with self.assertRaisesRegex(ValueError, "Missing 7500"): validate_pair(r)
        r = fixture(); r["steps12000"]["episodes"][0]["snapshotFloor0"] = float("nan")
        with self.assertRaisesRegex(ValueError, "Invalid snapshot coverage"): validate_pair(r)

    def test_incomplete_or_wrong_model_cannot_be_summarized(self):
        for changes in [dict(modelSha256="wrong"),dict(error="Agent disabled"),dict(timeScale=30)]:
            r=fixture();r["steps7500"].update(changes)
            with self.assertRaisesRegex(ValueError,"Incomplete or changed"): validate_pair(r)


if __name__ == "__main__": unittest.main()
