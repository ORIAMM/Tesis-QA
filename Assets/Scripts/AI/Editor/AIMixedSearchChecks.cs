using System;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class AIMixedSearchChecks
    {
        public static void Validate()
        {
            int[] tiles = { 2, 3, 3, 4, 4 };
            float[] far = { 0f, 0.2f, 0.5f, 0.2f, 0.5f };
            for (int level = 0; level <= AIMixedSearchSchedule.LastLevel; level++)
                if (AIMixedSearchSchedule.MaxTiles(level) != tiles[level]
                    || !Mathf.Approximately(AIMixedSearchSchedule.FarProbability(level), far[level]))
                    throw new InvalidOperationException("Mixed-search bridge changed distance/proportion schedule.");
            foreach (int seed in new[] { 913701, 921620, 929539, 937458, 945377 })
                if (!AIMixedSearchSchedule.IsHeldOutSeed(seed))
                    throw new InvalidOperationException("A held-out evaluation seed leaked into mixed training.");
            if (AIMixedSearchSchedule.IsHeldOutSeed(173003)
                || AIMixedSearchSchedule.MaxTiles(-1) != 2 || AIMixedSearchSchedule.MaxTiles(99) != 4)
                throw new InvalidOperationException("Mixed-search seed/clamping is invalid.");
            Debug.Log("AI MIXED SEARCH CHECKS passed: distance/proportion bridge, held-out seeds, clamping.");
        }
    }
}
