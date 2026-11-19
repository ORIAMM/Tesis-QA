using UnityEngine;

namespace TesisQA.AI
{
    // Change one difficulty axis at a time. Level 3 matches the former
    // full-yaw near-search task; no new policy observation is introduced.
    public static class AINearSearchSchedule
    {
        public const int LastLevel = 3;
        public static int MaxTiles(int level) => Mathf.Clamp(level, 0, LastLevel) == 0 ? 1 : 2;
        public static float YawRange(int level)
        {
            switch (Mathf.Clamp(level, 0, LastLevel))
            {
                case 0:
                case 1: return 60f;
                case 2: return 120f;
                default: return 180f;
            }
        }
    }
}
