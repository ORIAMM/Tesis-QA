import json
from pathlib import Path
import unittest

from compare_split_checkpoints import MODELS, PROTOCOL, compare, validate
from test_floor_timing import fixture as timing_fixture


def fixture():
    reports = {}
    for label, (model, digest) in MODELS.items():
        r = timing_fixture()
        r.update(protocol=PROTOCOL, model=model, modelSha256=digest,
                 rewardRevision=2, condition=label, mapSeedBase=1427003)
        for i, entry in enumerate(r["episodes"]):
            entry.update(has7500Snapshot=False, snapshotStep=0, snapshotFloor0=0, snapshotFloor1=0)
            entry["episode"].update(spawn=dict(x=float(i), y=0., z=0.),
                target=dict(x=10., y=4., z=0.), spawnYaw=30.)
        reports[label] = r
    for entry in reports["baseline2000063"]["episodes"][:5]:
        entry["episode"]["result"]["success"] = False
    for entry in reports["split499998"]["episodes"][:2]:
        entry["episode"]["result"]["success"] = False
    return reports


class SplitComparisonTests(unittest.TestCase):
    def test_valid_and_paired_success(self):
        s = compare(fixture())
        self.assertTrue(s["paired_seed_spawn_target_yaw_verified"])
        self.assertEqual(s["results"]["baseline2000063"]["overall"]["aggregate"]["successes"], 45)
        self.assertEqual(s["paired"]["baseline2000063__split499998"]["only_b_success"], 3)
        self.assertEqual(s["paired"]["baseline2000063__split1000061"]["only_b_success"], 5)
        self.assertEqual(s["paired"]["baseline2000063__split1000061"]["both_physical_arrival_episodes"], 50)
        self.assertEqual(s["paired"]["baseline2000063__split1000061"]["mean_first_arrival_step_delta_b_minus_a_common_arrivals"], 0)

    def test_model_or_revision_mismatch_rejected(self):
        for key, value in [("rewardRevision", 1), ("modelSha256", "wrong"),
                           ("model", "Phase3S_wrong"), ("condition", "baseline2000063"),
                           ("deterministicInference", False), ("protocol", "wrong")]:
            r = fixture(); r["split499998"][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError): validate(r)

    def test_incomplete_or_missing_model_rejected(self):
        r = fixture(); del r["split499998"]
        with self.assertRaises(ValueError): validate(r)
        r = fixture(); r["split499998"]["episodes"].pop()
        with self.assertRaises(ValueError): validate(r)

    def test_unpaired_spawn_target_yaw_rejected(self):
        for key in ("spawn", "target", "spawnYaw"):
            r = fixture(); e = r["split1000061"]["episodes"][0]["episode"]
            if key == "spawnYaw": e[key] = 31.
            else: e[key]["z"] = 1.
            with self.subTest(key=key), self.assertRaisesRegex(ValueError, "Unpaired"): validate(r)

    def test_invalid_timing_or_transfer_rejected(self):
        r = fixture(); r["split499998"]["episodes"][0]["episode"]["result"]["floorTiming"]["floor0Steps"] = -1
        with self.assertRaises(ValueError): validate(r)
        r = fixture(); r["split1000061"]["episodes"][0]["episode"]["result"]["ascentTransitions"] = 0
        with self.assertRaisesRegex(ValueError, "without a confirmed"): validate(r)
        r = fixture(); r["split499998"]["episodes"][0]["episode"]["spawn"]["x"] = float("nan")
        with self.assertRaisesRegex(ValueError, "Non-finite"): validate(r)

    def test_missing_snapshot_rejected(self):
        r = fixture(); e = r["split499998"]["episodes"][0]
        e["episode"]["result"]["steps"] = 12000
        t = e["episode"]["result"]["floorTiming"]
        t.update(floor0Steps=11880, floor0Seconds=237.6, elapsedSeconds=240,
                 secondsAfterFirstTransfer=238)
        with self.assertRaisesRegex(ValueError, "Missing 7500"): validate(r)

    def test_actual_outputs_when_available(self):
        root = Path("results/Phase3S/paired_comparison_20261028")
        if not all((root/(label+".done")).exists() for label in MODELS):
            self.skipTest("Bounded paired evaluation still running.")
        reports = {label: json.loads((root/(label+".json")).read_text(encoding="utf-8-sig")) for label in MODELS}
        result = compare(reports)
        self.assertEqual(sum(x["overall"]["aggregate"]["episodes"] for x in result["results"].values()), 150)


if __name__ == "__main__": unittest.main()
