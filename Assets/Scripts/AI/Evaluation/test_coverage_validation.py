import json
from pathlib import Path
import unittest
from summarize_coverage_validation import aggregate, MODEL_SHA256, EXPECTED_SEEDS


class CoverageValidationTests(unittest.TestCase):
    def test_real_small_report_is_complete_and_fixed_seed(self):
        r=json.loads(Path('results/Phase2E/coverage_validation_20261026/small.json').read_text())
        self.assertEqual(len(r['episodes']),30)
        self.assertEqual(r['modelSha256'],MODEL_SHA256)
        self.assertEqual(sorted({e['mapSeed'] for e in r['episodes']}),EXPECTED_SEEDS)
        s=aggregate(r['episodes'])
        self.assertEqual(s['successes'],10)
        self.assertEqual(s['ascent'],{'episodes':12,'reached':10})
        self.assertEqual(s['descent'],{'episodes':18,'reached':12})

    def test_real_large_report_does_not_confuse_stair_entry_with_success(self):
        r=json.loads(Path('results/Phase2E/coverage_validation_20261026/large.json').read_text())
        self.assertEqual(len(r['episodes']),10)
        self.assertEqual(r['modelSha256'],MODEL_SHA256)
        s=aggregate(r['episodes'])
        self.assertEqual(s['successes'],0)
        self.assertEqual(s['other_floor_reached'],7)
        self.assertEqual(s['stair_entries'],14)
        self.assertEqual(s['step_limit_episodes'],9)

    def test_balanced_mean_is_not_per_episode_balance(self):
        base={'success':False,'startFloor':0,'ascentTransitions':0,'descentTransitions':0,
            'stairTraversals':0,'steps':100,'revisits':0,'shortLoops':0,'collisions':0,
            'noNewAreaTimeouts':0,'noDisplacementFailures':0,'stepLimits':0}
        rows=[{'result':dict(base,floorZeroCoverage=1,floorOneCoverage=0)},
              {'result':dict(base,floorZeroCoverage=0,floorOneCoverage=1)}]
        s=aggregate(rows)
        self.assertEqual(s['floor0'],s['floor1'])
        self.assertEqual(s['minimum_floor'],0)
        self.assertEqual(s['floor_difference'],1)


if __name__=='__main__': unittest.main()
