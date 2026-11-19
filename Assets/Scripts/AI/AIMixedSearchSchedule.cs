using UnityEngine;

namespace TesisQA.AI
{
    // Training-only spawn schedule. It never steers the policy or adds observations.
    public static class AIMixedSearchSchedule
    {
        public const int LastLevel = 4;
        public static int MaxTiles(int level)
        {
            level = Mathf.Clamp(level, 0, LastLevel);
            return level == 0 ? 2 : level <= 2 ? 3 : 4;
        }
        public static float FarProbability(int level)
        {
            level = Mathf.Clamp(level, 0, LastLevel);
            return level == 0 ? 0f : level == 1 || level == 3 ? 0.2f : 0.5f;
        }
        public static bool IsHeldOutSeed(int seed) => seed == 913701 || seed == 921620
            || seed == 929539 || seed == 937458 || seed == 945377;
    }
}
