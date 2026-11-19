using System;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class AIStairPracticeChecks
    {
        public static void Validate()
        {
            var source = Vector3.zero;
            var destination = new Vector3(0f, 4f, 16f);
            if (AIStairPracticeProgress.Fraction(new Vector3(0f, 0.04f, 12f), source, destination) != 0f)
                throw new InvalidOperationException("Walking underneath upstairs must not count as ascent.");
            if (AIStairPracticeProgress.Fraction(new Vector3(5f, 3f, 12f), source, destination) != 0f)
                throw new InvalidOperationException("Another route beside the selected stair received progress.");
            var reward = new AIStairPracticeProgress();
            float total = reward.Observe(0.5f, true);
            for (int i = 0; i < 10; i++)
            {
                if (reward.Observe(0.25f, true) != 0f || reward.Observe(0.5f, true) != 0f)
                    throw new InvalidOperationException("Climb oscillations farmed progress reward.");
            }
            if (reward.Observe(1f, false) != 0f)
                throw new InvalidOperationException("Airborne progress received climb bonus.");
            total += reward.Observe(1f, true);
            if (Mathf.Abs(total - AIStairPracticeProgress.RewardBudget) > 0.00001f)
                throw new InvalidOperationException("Climb bonus exceeded its episode budget.");
            reward.Reset();
            if (reward.AwardedReward != 0f || reward.BestFraction != 0f)
                throw new InvalidOperationException("Climb reward leaked between episodes.");
            float approachTotal = reward.ObserveApproach(0.5f, true);
            if (reward.ObserveApproach(0.25f, true) != 0f
                || reward.ObserveApproach(0.5f, true) != 0f
                || reward.ObserveApproach(1f, false) != 0f)
                throw new InvalidOperationException("Approach reward can be farmed by retreating or leaving the route.");
            approachTotal += reward.ObserveApproach(1f, true);
            if (Mathf.Abs(approachTotal - AIStairPracticeProgress.ApproachBudget) > 0.00001f)
                throw new InvalidOperationException("Approach bonus exceeded its cap.");
            float down = AIStairPracticeProgress.Fraction(new Vector3(0f, 2f, 8f), destination, source);
            float up = AIStairPracticeProgress.Fraction(new Vector3(0f, 2f, 8f), source, destination);
            if (up <= 0f || Mathf.Abs(up - down) > 0.00001f)
                throw new InvalidOperationException("Ascent/descent progress is asymmetric.");
            Debug.Log("AI STAIR PRACTICE CHECKS passed: height, selected route, no oscillation farming, grounding, cap, descent, reset.");
        }
    }
}
