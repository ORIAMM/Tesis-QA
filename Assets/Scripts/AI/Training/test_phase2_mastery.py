import unittest
from phase2_mastery import MasteryWindow, StairPracticeWindow, NearSearchWindow, MixedSearchWindow


class MasteryTests(unittest.TestCase):
    def test_mixed_search_requires_far_success_not_only_easy_examples(self):
        for level in range(1, 5):
            # 80% pooled success must not hide failure on every far example.
            outcomes = ([level * 100 + 3] * 60 + [level * 100 + 6] * 15
                        + [level * 100 + 1] * 20 + [level * 100 + 4] * 5)
            window = MixedSearchWindow(level, outcomes)
            self.assertGreaterEqual(window.summary()["up_success"], 0.75)
            self.assertGreaterEqual(window.summary()["down_success"], 0.75)
            self.assertFalse(window.ready())
            self.assertEqual(window.summary()["far_success"], 0)
            passed = MixedSearchWindow(level, [level * 100 + 3] * 55
                                       + [level * 100 + 7] * 20 + [level * 100 + 1] * 25)
            self.assertTrue(passed.ready())
            self.assertEqual(MixedSearchWindow.restore(passed.state(), level).summary(), passed.summary())

    def test_mixed_search_needs_full_window_both_directions_and_ignores_stale_levels(self):
        self.assertFalse(MixedSearchWindow(0, [3] * 75 + [1] * 24).ready())
        self.assertTrue(MixedSearchWindow(0, [3] * 75 + [1] * 25).ready())
        self.assertFalse(MixedSearchWindow(0, [3] * 100).ready())
        stale = MixedSearchWindow(1, [3] * 100)
        self.assertEqual(stale.summary()["episodes"], 0)
        for code in (1.2, -1, 108, 500):
            with self.assertRaises(ValueError): stale.add(code)
        with self.assertRaises(ValueError): MixedSearchWindow.restore(None, 0)

    def test_single_floor_needs_full_window_and_actual_success(self):
        bank = MasteryWindow()
        for _ in range(99): bank.add(1)
        self.assertFalse(bank.ready())
        bank.add(1)
        self.assertTrue(bank.ready())
        bank = MasteryWindow(0, [1] * 74 + [0] * 26)
        self.assertFalse(bank.ready())
        bank.add(1)  # rolls out a success, not a failure
        self.assertFalse(bank.ready())
        self.assertTrue(MasteryWindow(0, [1] * 75 + [0] * 25).ready())

    def test_ascent_must_be_mastered_before_near_search(self):
        self.assertFalse(MasteryWindow(1, [13] * 79 + [12] * 21).ready())
        self.assertTrue(MasteryWindow(1, [13] * 80 + [12] * 20).ready())
        self.assertFalse(MasteryWindow(1, [11] * 100).ready())

    def test_pooled_success_cannot_hide_failed_direction(self):
        self.assertFalse(MasteryWindow(2, [23] * 75 + [20] * 25).ready())
        self.assertFalse(MasteryWindow(2, [23] * 90 + [21] * 10).ready())
        bank = MasteryWindow(2, [23] * 60 + [12] + [22] * 15 + [21] * 19 + [20] * 6)
        self.assertTrue(bank.ready())  # stale stage-1 sample ignored; both >=75%

    def test_terminal_stage_never_advances_and_invalid_outcomes_fail(self):
        self.assertFalse(MasteryWindow(3, [33] * 100).ready())
        for code in (1.2, -1, 24, 60):
            with self.assertRaises(ValueError): MasteryWindow().add(code)

    def test_resume_restores_window_and_rejects_old_curriculum(self):
        bank = MasteryWindow(2, [23] * 50 + [21] * 50)
        restored = MasteryWindow.restore(bank.state(), 2)
        self.assertEqual(bank.summary(), restored.summary())
        self.assertTrue(restored.ready())
        for state, stage in ((None, 0), (bank.state(), 1), ({"revision": 0}, 0)):
            with self.assertRaises(ValueError): MasteryWindow.restore(state, stage)

    def test_coverage_promotes_only_on_actual_balanced_success(self):
        self.assertFalse(MasteryWindow(4, [41] * 74 + [40] * 26).ready())
        self.assertTrue(MasteryWindow(4, [41] * 75 + [40] * 25).ready())
        self.assertFalse(MasteryWindow(5, [51] * 100).ready())

    def test_practice_needs_each_level_and_does_not_accept_stale_level_packets(self):
        for level in range(3):
            self.assertFalse(StairPracticeWindow(level, [level * 10 + 3] * 79 + [level * 10 + 2] * 21).ready())
            self.assertTrue(StairPracticeWindow(level, [level * 10 + 3] * 80 + [level * 10 + 2] * 20).ready())
        practice = StairPracticeWindow(1)
        for _ in range(100): practice.add(3)
        self.assertFalse(practice.ready())
        self.assertEqual(practice.summary()["episodes"], 0)

    def test_near_search_requires_both_directions_at_every_level(self):
        for level in range(4):
            # Overall 75% success is insufficient if descent is never successful.
            self.assertFalse(NearSearchWindow(level, [level * 10 + 3] * 75 + [level * 10] * 25).ready())
            self.assertFalse(NearSearchWindow(level, [level * 10 + 3] * 90 + [level * 10 + 1] * 10).ready())
            window = NearSearchWindow(level, [level * 10 + 3] * 60 + [level * 10 + 2] * 15
                                      + [level * 10 + 1] * 19 + [level * 10] * 6)
            self.assertTrue(window.ready())
            self.assertEqual(NearSearchWindow.restore(window.state(), level).summary(), window.summary())
        stale = NearSearchWindow(1)
        for _ in range(100): stale.add(3)
        self.assertFalse(stale.ready())
        self.assertEqual(stale.summary()["episodes"], 0)


if __name__ == "__main__": unittest.main()
