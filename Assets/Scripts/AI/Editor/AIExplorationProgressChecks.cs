using System;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class AIExplorationProgressChecks
    {
        public static void Validate()
        {
            var guard = new AIExplorationProgressGuard();
            foreach (string area in new[] { "A", "B", "C", "D", "C", "B", "A" })
                if (guard.Visit(area, false)) throw new InvalidOperationException("Normal backtracking was penalized.");
            guard.Reset();
            string[] cycle = { "A", "B", "A", "B", "A", "B", "A" };
            for (int i = 0; i < cycle.Length; i++)
                if (guard.Visit(cycle[i], false) != (i == cycle.Length - 1))
                    throw new InvalidOperationException("A-B-A must be free; three complete cycles should be detected.");
            guard.MarkProgress();
            if (guard.Visit("B", false) || guard.Visit("A", false))
                throw new InvalidOperationException("Progress did not clear old loop evidence.");
            guard.Reset();
            string[] longCycle = { "A", "B", "C", "D", "A", "B", "C", "D", "A", "B", "C", "D", "A" };
            bool detected = false;
            foreach (string area in longCycle) detected |= guard.Visit(area, false);
            if (!detected) throw new InvalidOperationException("Four-area repeated loop was not detected.");
            guard.Reset();
            guard.BeginBacktracking("A");
            guard.BeginBacktracking("A");
            if (guard.GraceGranted != 10f) throw new InvalidOperationException("Revisits farmed grace.");
            if (!guard.RegisterMilestone("up") || guard.RegisterMilestone("up")
                || !guard.RegisterMilestone("down")) throw new InvalidOperationException("Repeated floor transition farmed grace.");
            guard.RegisterMilestone("another-stair");
            if (guard.GraceGranted != 30f) throw new InvalidOperationException("Grace exceeded episode budget.");
            if (guard.Tick(40f, 25f) || guard.GraceUsed != 15f)
                throw new InvalidOperationException("Backtracking time allowance is incorrect.");
            guard.MarkProgress();
            if (guard.Tick(39f, 25f) || !guard.Tick(2f, 25f) || guard.GraceUsed != 30f)
                throw new InvalidOperationException("Progress refilled the episode grace budget.");
            guard.Reset();
            if (guard.GraceGranted != 0f || guard.GraceUsed != 0f || !guard.Tick(25f, 25f))
                throw new InvalidOperationException("Episode reset/normal timeout is incorrect.");
            guard.Reset();
            foreach (string area in new[] { "A", "B", "C", "D", "E", "F", "G", "H" })
                guard.BeginBacktracking(area);
            if (guard.GraceGranted != 30f) throw new InvalidOperationException("Long return route did not receive bounded grace.");
            Debug.Log("AI EXPLORATION CHECKS passed: backtracking, repeated loops, progress, grace cap, no stair farming.");
        }
    }
}
