using UnityEngine;

namespace TesisQA.AI
{
    // Training-only auxiliary reward. Only a NEW BEST traversal fraction pays;
    // oscillating on the same part of a stair cannot collect the bonus again.
    public sealed class AIStairPracticeProgress
    {
        public const float RewardBudget = 0.3f;
        public const float ApproachBudget = 0.1f;
        public float BestFraction { get; private set; }
        public float AwardedReward { get; private set; }
        public float BestApproach { get; private set; }
        public float AwardedApproach { get; private set; }

        public void Reset() { BestFraction = AwardedReward = BestApproach = AwardedApproach = 0f; }

        public float ObserveApproach(float fraction, bool eligible)
        {
            if (!eligible) return 0f;
            float next = Mathf.Clamp01(fraction);
            float reward = Mathf.Max(0f, next - BestApproach) * ApproachBudget;
            BestApproach = Mathf.Max(BestApproach, next);
            AwardedApproach += reward;
            return reward;
        }

        public float Observe(float fraction, bool groundedOnSelectedStair)
        {
            if (!groundedOnSelectedStair) return 0f;
            float next = Mathf.Clamp01(fraction);
            float reward = Mathf.Max(0f, next - BestFraction) * RewardBudget;
            BestFraction = Mathf.Max(BestFraction, next);
            AwardedReward += reward;
            return reward;
        }

        public static float Fraction(Vector3 position, Vector3 source, Vector3 destination)
        {
            float rise = destination.y - source.y;
            Vector3 axis = destination - source;
            axis.y = 0f;
            if (Mathf.Abs(rise) < 1f || axis.sqrMagnitude < 0.01f) return 0f;
            Vector3 offset = position - source;
            offset.y = 0f;
            float along = Vector3.Dot(offset, axis) / axis.sqrMagnitude;
            Vector3 lateral = offset - axis * along;
            if (lateral.magnitude > 2.25f || along < -0.1f || along > 1.1f) return 0f;
            float climbed = (position.y - source.y) * Mathf.Sign(rise);
            float height = (climbed - 0.15f) / (Mathf.Abs(rise) - 0.15f);
            // Horizontal movement underneath the upper floor is not ascent.
            return Mathf.Min(Mathf.Clamp01(along), Mathf.Clamp01(height));
        }
    }
}
