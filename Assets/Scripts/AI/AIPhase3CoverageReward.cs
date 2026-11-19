using UnityEngine;

namespace TesisQA.AI
{
    // Training reward only, never an observation or an action/route planner.
    // Normalized to the active lesson so map size cannot multiply the budget.
    public static class AIPhase3CoverageReward
    {
        public const float ExplorationBudget = 0.25f;
        public const float BalanceBudget = 1f;

        public static float ExplorationTotal(float lower, float upper, float target) =>
            ExplorationBudget * Mathf.Clamp01((Mathf.Clamp01(lower) + Mathf.Clamp01(upper))
                * 0.5f / Mathf.Max(0.01f, target));

        // Revision 2: progress on one floor cannot consume the other's budget.
        // Keep revision 1 above unchanged for historical replay/comparison.
        public static float SplitExplorationTotal(float lower, float upper, float target)
        {
            float denominator = Mathf.Max(0.01f, target);
            return ExplorationBudget * 0.5f * (Mathf.Clamp01(lower / denominator)
                + Mathf.Clamp01(upper / denominator));
        }

        public static float BalanceTotal(float lower, float upper, float target, float maximumDifference = 0.1f)
        {
            float minimum = Mathf.Min(Mathf.Clamp01(lower), Mathf.Clamp01(upper));
            float maximum = Mathf.Max(Mathf.Clamp01(lower), Mathf.Clamp01(upper));
            float targetProgress = minimum / Mathf.Max(0.01f, target);
            float balanceProgress = maximum <= 0f ? 0f
                : (minimum + Mathf.Clamp01(maximumDifference)) / maximum;
            // Full budget requires BOTH per-floor target and allowed imbalance.
            // 30%/10% must still reward catching up to 30%/20%, not saturate at 10%.
            return BalanceBudget * Mathf.Clamp01(Mathf.Min(targetProgress, balanceProgress));
        }

        public static float Increment(float total, float alreadyAwarded) => Mathf.Max(0f, total - alreadyAwarded);
    }
}
