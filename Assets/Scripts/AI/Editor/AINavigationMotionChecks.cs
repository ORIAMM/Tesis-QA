using System;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class AINavigationMotionChecks
    {
        public static void Validate()
        {
            var guard = new AINavigationMotionGuard();
            guard.Reset(Vector3.zero, 0);
            if (guard.Tick(Vector3.zero, 0, 4f, 4f, 18f) != AINavigationMotionGuard.Signal.StationaryWarning
                || guard.Tick(Vector3.zero, 0, 4f, 4f, 18f) != AINavigationMotionGuard.Signal.StationaryFailure)
                throw new InvalidOperationException("Pure spinning must warn before ending.");
            guard.Reset(Vector3.zero, 0);
            for (int i = 1; i <= 36; i++)
            {
                // A small moving circle bypasses a velocity-based stuck detector.
                Vector3 point = i % 2 == 0 ? Vector3.left : Vector3.right;
                var signal = guard.Tick(point, 0, 1f, 4f, 18f);
                var expected = i == 18 ? AINavigationMotionGuard.Signal.RangeWarning
                    : i == 36 ? AINavigationMotionGuard.Signal.RangeFailure
                    : AINavigationMotionGuard.Signal.None;
                if (signal != expected) throw new InvalidOperationException("Repeated small circles escaped the range limit.");
            }
            guard.Reset(Vector3.zero, 0);
            guard.Tick(Vector3.zero, 0, 4f, 4f, 18f);
            if (guard.Tick(Vector3.zero, 1, 4f, 4f, 18f) != AINavigationMotionGuard.Signal.None
                || guard.Tick(Vector3.zero, 1, 4f, 4f, 18f) != AINavigationMotionGuard.Signal.StationaryWarning)
                throw new InvalidOperationException("Useful progress failed to reset the warning.");
            guard.Reset(Vector3.zero, 0);
            for (int i = 1; i <= 10; i++)
                if (guard.Tick(new Vector3(0f, i * 0.6f, i * 0.5f), 0, 1f, 4f, 18f)
                    != AINavigationMotionGuard.Signal.None)
                    throw new InvalidOperationException("Valid ramp movement was treated as stationary.");
            guard.Reset(Vector3.zero, 0);
            guard.Tick(Vector3.right, 0, 3f, 4f, 12f);
            guard.Reset(Vector3.zero, 0);
            if (guard.Tick(Vector3.zero, 0, 1f, 4f, 12f) != AINavigationMotionGuard.Signal.None)
                throw new InvalidOperationException("Motion state leaked across episodes.");
            Debug.Log("AI MOTION CHECKS passed: pure spin, moving circles, warning recovery, progress, ramp, reset.");
        }
    }
}
