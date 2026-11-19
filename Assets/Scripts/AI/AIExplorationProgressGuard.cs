using System;
using System.Collections.Generic;

namespace TesisQA.AI
{
    // Visit history is information, not a punishment. Only repeated closed
    // cycles count as loop evidence; useful progress clears that evidence.
    public sealed class AIExplorationProgressGuard
    {
        public const float GraceBudget = 30f;
        private readonly List<object> history = new List<object>();
        private readonly HashSet<object> milestones = new HashSet<object>();
        private readonly HashSet<object> backtrackingAreas = new HashSet<object>();
        private int transitionsSincePenalty;
        private float intervalGraceUsed;
        public float Elapsed { get; private set; }
        public float GraceGranted { get; private set; }
        public float GraceUsed { get; private set; }
        public float GraceRemaining => Math.Max(0f, GraceGranted - GraceUsed);
        public int ProgressVersion { get; private set; }

        public void Reset()
        {
            history.Clear();
            milestones.Clear();
            backtrackingAreas.Clear();
            Elapsed = GraceGranted = GraceUsed = intervalGraceUsed = 0f;
            transitionsSincePenalty = 0;
            ProgressVersion = 0;
        }

        public void MarkProgress()
        {
            ProgressVersion++;
            Elapsed = intervalGraceUsed = 0f;
            history.Clear();
            transitionsSincePenalty = 0;
        }

        public void BeginBacktracking(object area)
        {
            if (!backtrackingAreas.Add(area)) return;
            // Longer return routes receive more of the finite budget; bouncing
            // between the same two triggers cannot unlock the extra bands.
            if (backtrackingAreas.Count == 1 || backtrackingAreas.Count == 4 || backtrackingAreas.Count == 8)
                Grant(10f);
        }

        public bool RegisterMilestone(object key)
        {
            if (!milestones.Add(key)) return false;
            Grant(10f);
            MarkProgress();
            return true;
        }

        private void Grant(float seconds)
        {
            GraceGranted += Math.Min(seconds, GraceBudget - GraceGranted);
        }

        public bool Visit(object area, bool isNewArea)
        {
            if (isNewArea) MarkProgress();
            if (history.Count > 0 && Equals(history[history.Count - 1], area)) return false;
            history.Add(area);
            if (history.Count > 13) history.RemoveAt(0);
            transitionsSincePenalty++;
            for (int period = 2; period <= 4; period++)
            {
                int needed = period * 3 + 1; // Three complete cycles and their shared endpoint.
                if (history.Count < needed || transitionsSincePenalty < period) continue;
                int start = history.Count - needed;
                bool repeats = true;
                for (int i = period; i < needed; i++)
                    if (!Equals(history[start + i], history[start + i - period])) { repeats = false; break; }
                if (!repeats) continue;
                transitionsSincePenalty = 0;
                return true;
            }
            return false;
        }

        public bool Tick(float seconds, float timeout)
        {
            Elapsed += Math.Max(0f, seconds);
            float excess = Math.Max(0f, Elapsed - timeout - intervalGraceUsed);
            float spend = Math.Min(excess, GraceRemaining);
            GraceUsed += spend;
            intervalGraceUsed += spend;
            return Elapsed >= timeout + intervalGraceUsed && GraceRemaining <= 0.00001f;
        }
    }
}
