using System;

namespace TesisQA.AI
{
    // Apply at policy decisions, not physics ticks. Terminal potential is zero.
    public sealed class AIStairSearchPotential
    {
        public const float Gamma = 0.99f;
        public const float Scale = 0.3f;
        public const float DistanceHorizon = 8f;
        private float previous;
        private bool hasDecision;

        public void Reset()
        {
            previous = 0f;
            hasDecision = false;
        }

        public float Observe(float distanceInTiles)
        {
            float potential = Scale * Math.Max(0f, 1f - Math.Max(0f, distanceInTiles) / DistanceHorizon);
            float reward = hasDecision ? Gamma * potential - previous : 0f;
            previous = potential;
            hasDecision = true;
            return reward;
        }

        public float Finish()
        {
            float reward = hasDecision ? -previous : 0f;
            Reset();
            return reward;
        }
    }
}
