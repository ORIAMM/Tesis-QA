using UnityEngine;

namespace TesisQA.AI
{
    // Opt-in Phase2T only. A level is difficulty, not a new observation schema.
    public static class AITerrainCurriculum
    {
        public const int LastLevel = 11;
        public static bool IsTransfer(int level) => level >= 3 && level <= 7;
        public static int Stage(int level) => level < 3 ? 0 : IsTransfer(level) ? 2 : 4;
        public static float Coverage(int level)
        {
            if (level < 3) return 0.2f + Mathf.Clamp(level, 0, 2) * 0.1f;
            return IsTransfer(level) ? 0f : 0.1f + Mathf.Clamp(level - 8, 0, 3) * 0.1f;
        }
        public static int MaxTiles(int level) => Mathf.Clamp(level - 3, 0, 4);
        public static float YawRange(int level) => level <= 3 ? 30f : level == 4 ? 60f : level == 5 ? 120f : 180f;
        public static float FarProbability(int level) => level < 6 ? 0f : level == 6 ? 0.25f : 0.5f;
        public static bool IsRehearsal(int level, int episode) => level > 0
            && level != 3 && episode % 10 >= 8;
        public static int EpisodeLevel(int level, int episode) => IsRehearsal(level, episode) ? level - 1 : level;
        public static bool BalancedCoverage(float target, float lower, float upper, float maximumDifference)
        {
            const float epsilon = 0.00001f;
            return lower + epsilon >= target && upper + epsilon >= target
                && Mathf.Abs(lower - upper) <= maximumDifference + epsilon;
        }
    }
}
