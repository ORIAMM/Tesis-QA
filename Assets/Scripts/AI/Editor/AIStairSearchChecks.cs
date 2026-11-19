using System;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class AIStairSearchChecks
    {
        public static void Validate()
        {
            var reward = new AIStairSearchPotential();
            if (reward.Observe(4f) != 0f || reward.Observe(4f) >= 0f)
                throw new InvalidOperationException("Stationary decisions must not farm positive reward.");
            CheckTrajectory(new[] { 4f, 3f, 2f, 1f, 0f });
            CheckTrajectory(new[] { 4f, 4f, 4f, 3f, 4f, 3f, 2f, 1f, 0f });
            CheckTrajectory(new[] { 4f, 3f, 4f, 3f, 4f }); // Failure/loop also terminates at Phi=0.
            reward.Finish();
            if (reward.Finish() != 0f) throw new InvalidOperationException("Terminal correction applied twice.");
            reward.Reset();
            if (reward.Finish() != 0f) throw new InvalidOperationException("Empty episode must have zero shaping.");
            Debug.Log("AI STAIR SEARCH CHECKS passed: decision discount, terminal correction, loops, reset.");
        }

        private static void CheckTrajectory(float[] distances)
        {
            var reward = new AIStairSearchPotential();
            reward.Observe(distances[0]);
            double total = 0d;
            double discount = 1d;
            for (int i = 1; i < distances.Length; i++)
            {
                total += discount * reward.Observe(distances[i]);
                discount *= AIStairSearchPotential.Gamma;
            }
            total += discount * reward.Finish();
            double initial = AIStairSearchPotential.Scale * (1d - distances[0] / AIStairSearchPotential.DistanceHorizon);
            if (Math.Abs(total + initial) > 0.00001)
                throw new InvalidOperationException("Discounted shaping failed to telescope at terminal state.");
        }
    }
}
