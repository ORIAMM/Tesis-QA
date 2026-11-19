using System;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class AINearSearchChecks
    {
        public static void Validate()
        {
            int[] tiles = { 1, 2, 2, 2 };
            float[] yaw = { 60f, 60f, 120f, 180f };
            for (int level = 0; level <= AINearSearchSchedule.LastLevel; level++)
                if (AINearSearchSchedule.MaxTiles(level) != tiles[level]
                    || AINearSearchSchedule.YawRange(level) != yaw[level])
                    throw new InvalidOperationException("Near search changed distance and orientation together.");
            if (AINearSearchSchedule.YawRange(3) * 2f != 360f
                || AINearSearchSchedule.MaxTiles(-1) != 1
                || AINearSearchSchedule.MaxTiles(99) != 2)
                throw new InvalidOperationException("Final near-search difficulty/clamping is invalid.");
            Debug.Log("AI NEAR SEARCH CHECKS passed: staged range/yaw, final full-circle coverage, clamping.");
        }
    }
}
