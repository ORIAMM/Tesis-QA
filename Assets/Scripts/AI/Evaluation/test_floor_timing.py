import unittest
from summarize_floor_timing import validate, describe, MODEL_HASH, SEEDS


def fixture():
    episodes = []
    for i in range(50):
        t = dict(firstOtherFloorStep=100, firstOtherFloorIndex=1, firstOtherFloorSeconds=2,
            elapsedSeconds=4, floor0Steps=80, floor1Steps=100, transitOrUnclassifiedSteps=20,
            floor0Seconds=1.6, floor1Seconds=2, transitOrUnclassifiedSeconds=.4,
            secondsAfterFirstTransfer=2, floor0SecondsAfterTransfer=0, floor1SecondsAfterTransfer=2,
            floor0CoverageAtTransfer=.1, floor1CoverageAtTransfer=0,
            floor0CoverageGainAfterTransfer=0, floor1CoverageGainAfterTransfer=.1)
        r = dict(success=True, steps=200, startFloor=0, lesson=4, stairPracticeGuided=False,
            stairRehearsal=False, searchShapingActive=False, floorTiming=t, floorZeroCoverage=.1, floorOneCoverage=.1,
            ascentTransitions=1, descentTransitions=0, stairTraversals=1, revisits=0, shortLoops=0,
            collisions=0, noNewAreaTimeouts=0, noDisplacementFailures=0, stepLimits=0)
        episodes.append({"episode": {"episode": i+1, "mapSeed": SEEDS[i//10], "result": r}})
    return dict(protocol="phase3r-physical-floor-timing-level8-v1", model="Phase3R_2000063", modelSha256=MODEL_HASH,
        requestedEpisodes=50, episodes=episodes, maxStep=12000, rewardRevision=1, mapRegenerationInterval=10,
        spawnSeed=27421, inferenceSeed=58231, deterministicInference=True, timeScale=40, fixedDeltaTime=.02,
        coverageTarget=.1, maximumFloorDifference=.1)


class FloorTimingTests(unittest.TestCase):
    def test_valid_and_summary(self):
        r = fixture(); validate(r); d = describe(r["episodes"])
        self.assertEqual(d["physical_other_floor_reached"], 50)
        self.assertEqual(d["first_arrival_step_reached_only"]["mean"], 100)
        self.assertEqual(d["mean_floor1_gain_after_arrival_reached_only"], .1)

    def test_no_transfer_not_averaged_as_zero(self):
        r = fixture(); s = r["episodes"][0]["episode"]["result"]; s["success"] = False
        t = s["floorTiming"]
        for key in ("firstOtherFloorStep", "firstOtherFloorIndex", "firstOtherFloorSeconds", "secondsAfterFirstTransfer",
                    "floor0CoverageAtTransfer", "floor1CoverageAtTransfer", "floor0CoverageGainAfterTransfer", "floor1CoverageGainAfterTransfer"):
            t[key] = -1
        t.update(floor0Steps=180, floor1Steps=0, floor0Seconds=3.6, floor1Seconds=0,
                 floor0SecondsAfterTransfer=0, floor1SecondsAfterTransfer=0)
        validate(r); d = describe(r["episodes"])
        self.assertEqual(d["physical_other_floor_reached"], 49)
        self.assertEqual(d["never_arrived"]["episodes"], 1)
        self.assertEqual(d["first_arrival_step_reached_only"]["mean"], 100)

    def test_reject_bad_time_gain_step_or_missing(self):
        for field, value in (("floor0Steps", 79), ("elapsedSeconds", .1), ("floor1Seconds", 80),
                             ("firstOtherFloorStep", 201), ("floor1CoverageGainAfterTransfer", .2),
                             ("floor0CoverageAtTransfer", float("nan")), ("firstOtherFloorIndex", 0)):
            r = fixture(); r["episodes"][0]["episode"]["result"]["floorTiming"][field] = value
            with self.subTest(field=field), self.assertRaises(ValueError): validate(r)
        r = fixture(); r["episodes"].pop()
        with self.assertRaises(ValueError): validate(r)

    def test_reject_changed_reward_or_model(self):
        for field, value in (("rewardRevision", 0), ("modelSha256", "wrong"), ("deterministicInference", False)):
            r = fixture(); r[field] = value
            with self.assertRaises(ValueError): validate(r)

    def test_descending(self):
        r = fixture()
        for entry in r["episodes"]:
            s = entry["episode"]["result"]; s["startFloor"] = 1; s["floorTiming"]["firstOtherFloorIndex"] = 0
        validate(r)


if __name__ == "__main__": unittest.main()
