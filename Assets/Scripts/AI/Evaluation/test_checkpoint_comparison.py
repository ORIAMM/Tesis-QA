import json
from pathlib import Path
import unittest
from summarize_checkpoint_comparison import MODELS, compare, validate


def fixture():
    reports = {}
    for label,(model,digest) in MODELS.items():
        newer = label == "checkpoint6000091"
        rows = []
        for i in range(50):
            success = i < (30 if newer else 25)
            result = dict(success=success, steps=7000 if success else 12000,
                lesson=4, startFloor=i%2, searchShapingActive=False,
                stairPracticeGuided=False, stairRehearsal=False,
                floorZeroCoverage=.11 if success else .02,
                floorOneCoverage=.12 if success else .09,
                ascentTransitions=1,descentTransitions=1,stairTraversals=1,
                revisits=3,shortLoops=0,collisions=1,noNewAreaTimeouts=0,
                noDisplacementFailures=0,stepLimits=0 if success else 1)
            rows.append(dict(episode=dict(episode=i+1,mapSeed=1427003+(i//10)*7919,
                spawn=dict(x=float(i),y=4.0*(i%2),z=0.),target=dict(x=10.,y=4.,z=0.),
                spawnYaw=30.,result=result),has7500Snapshot=not success,
                snapshotStep=7500,snapshotFloor0=.01,snapshotFloor1=.05))
        reports[label] = dict(protocol="phase3-paired-checkpoints-level8-v1",model=model,
            modelSha256=digest,deterministicInference=True,requestedEpisodes=50,
            maxStep=12000,mapRegenerationInterval=10,mapSeedBase=1427003,
            spawnSeed=27421,inferenceSeed=58231,timeScale=40,fixedDeltaTime=.02,
            coverageTarget=.1,maximumFloorDifference=.1,error=None,episodes=rows)
    return reports


class CheckpointTests(unittest.TestCase):
    def test_actual_hundred_episodes_are_complete_and_paired(self):
        directory=Path("results/Phase3/checkpoint_comparison_20261027")
        reports={label:json.loads((directory/(label+".json")).read_text()) for label in MODELS}
        s=compare(reports)
        self.assertTrue(s["paired_seed_spawn_target_yaw_verified"])
        self.assertEqual(s["results"]["checkpoint4499994"]["aggregate"]["successes"],23)
        self.assertEqual(s["results"]["checkpoint6000091"]["aggregate"]["successes"],26)
        self.assertEqual(s["paired"]["only4499994_success"],8)
        self.assertEqual(s["paired"]["only6000091_success"],11)

    def test_valid_comparison_and_paired_wins(self):
        s=compare(fixture())
        self.assertEqual(s["results"]["checkpoint4499994"]["aggregate"]["successes"],25)
        self.assertEqual(s["results"]["checkpoint6000091"]["aggregate"]["successes"],30)
        self.assertEqual(s["paired"]["only6000091_success"],5)
        self.assertEqual(s["paired"]["only4499994_success"],0)
        self.assertAlmostEqual(s["paired"]["mean_minimum_floor_delta6000091_minus4499994"],.009)

    def test_swapped_hash_and_partial_report_rejected(self):
        for changes in [dict(modelSha256=MODELS["checkpoint4499994"][1]),
                        dict(error="Agent disabled"),dict(requestedEpisodes=49),dict(maxStep=7500)]:
            r=fixture();r["checkpoint6000091"].update(changes)
            with self.assertRaisesRegex(ValueError,"Incomplete/changed"):validate(r)

    def test_five_seeds_not_just_fifty_repeated_episodes(self):
        r=fixture()
        for x in r["checkpoint6000091"]["episodes"]:x["episode"]["mapSeed"]=1
        with self.assertRaisesRegex(ValueError,"five map batches"):validate(r)

    def test_spawn_and_yaw_mismatch_rejected(self):
        r=fixture();r["checkpoint6000091"]["episodes"][0]["episode"]["spawn"]["z"]+=1
        with self.assertRaisesRegex(ValueError,"Unpaired spawn"):validate(r)
        r=fixture();r["checkpoint6000091"]["episodes"][0]["episode"]["spawnYaw"]+=1
        with self.assertRaisesRegex(ValueError,"Unpaired yaw"):validate(r)

    def test_success_cannot_ignore_other_floor_or_transfer(self):
        for changes in [dict(floorOneCoverage=0.),dict(ascentTransitions=0,descentTransitions=0)]:
            r=fixture();r["checkpoint6000091"]["episodes"][0]["episode"]["result"].update(changes)
            with self.assertRaisesRegex(ValueError,"Success violates"):validate(r)

    def test_missing_snapshot_and_guidance_rejected(self):
        r=fixture();r["checkpoint6000091"]["episodes"][-1]["has7500Snapshot"]=False
        with self.assertRaisesRegex(ValueError,"Missing 7500"):validate(r)
        r=fixture();r["checkpoint6000091"]["episodes"][0]["episode"]["result"]["stairPracticeGuided"]=True
        with self.assertRaisesRegex(ValueError,"unexpected guidance"):validate(r)


if __name__ == "__main__": unittest.main()
