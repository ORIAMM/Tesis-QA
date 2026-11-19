using UnityEngine;

namespace TesisQA.AI
{
    // A warning gives the policy time to recover. Movement alone cannot keep
    // refreshing the larger window while circling inside the same small area.
    public sealed class AINavigationMotionGuard
    {
        public enum Signal { None, StationaryWarning, RangeWarning, StationaryFailure, RangeFailure }
        private Vector3 motionAnchor;
        private Vector3 rangeAnchor;
        private float motionTime;
        private float rangeTime;
        private bool motionWarned;
        private bool rangeWarned;
        private int progressVersion;

        public void Reset(Vector3 position, int version)
        {
            motionAnchor = rangeAnchor = position;
            motionTime = rangeTime = 0f;
            motionWarned = rangeWarned = false;
            progressVersion = version;
        }

        public Signal Tick(Vector3 position, int version, float seconds,
            float requiredDisplacement, float rangeTimeout)
        {
            if (version != progressVersion)
            {
                Reset(position, version);
                return Signal.None;
            }
            seconds = Mathf.Max(0f, seconds);
            Signal signal = Signal.None;
            // Include height for genuine ramp motion, not collider velocity/jitter.
            if (Vector3.Distance(position, motionAnchor) >= 0.5f)
            {
                motionAnchor = position;
                motionTime = 0f;
                motionWarned = false;
            }
            else
            {
                motionTime += seconds;
                if (motionTime >= 4f)
                {
                    motionTime = 0f;
                    if (motionWarned) return Signal.StationaryFailure;
                    motionWarned = true;
                    signal = Signal.StationaryWarning;
                }
            }
            Vector3 offset = position - rangeAnchor;
            offset.y = 0f;
            if (offset.magnitude >= requiredDisplacement)
            {
                rangeAnchor = position;
                rangeTime = 0f;
                rangeWarned = false;
            }
            else
            {
                rangeTime += seconds;
                if (rangeTime >= rangeTimeout)
                {
                    rangeTime = 0f;
                    if (rangeWarned) return Signal.RangeFailure;
                    rangeWarned = true;
                    signal = Signal.RangeWarning;
                }
            }
            return signal;
        }
    }
}
