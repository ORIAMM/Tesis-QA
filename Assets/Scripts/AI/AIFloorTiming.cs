using System;

namespace TesisQA.AI
{
    [Serializable]
    public sealed class AIFloorTimingResult
    {
        public int firstOtherFloorStep = -1, firstOtherFloorIndex = -1;
        public double firstOtherFloorSeconds = -1, elapsedSeconds;
        public int floor0Steps, floor1Steps, transitOrUnclassifiedSteps;
        public double floor0Seconds, floor1Seconds, transitOrUnclassifiedSeconds;
        public double secondsAfterFirstTransfer = -1;
        public double floor0SecondsAfterTransfer, floor1SecondsAfterTransfer;
        public float floor0CoverageAtTransfer = -1, floor1CoverageAtTransfer = -1;
        public float floor0CoverageGainAfterTransfer = -1, floor1CoverageGainAfterTransfer = -1;
    }

    // Diagnostics only. End-of-physics-step occupancy, not an observation/reward.
    // Floor -1 means ramp, airborne, or otherwise not confirmed on a flat floor.
    public sealed class AIFloorTiming
    {
        private int startFloor, lastStep, lastFloor, after0, after1;
        private double delta;
        public AIFloorTimingResult Result { get; private set; }
        public void Begin(int floor, double fixedDelta)
        {
            if (fixedDelta <= 0) throw new ArgumentOutOfRangeException(nameof(fixedDelta));
            startFloor = floor; delta = fixedDelta; lastStep = after0 = after1 = 0;
            lastFloor = -1; Result = new AIFloorTimingResult();
        }
        public bool NeedsSnapshot(int floor) => Result != null && Result.firstOtherFloorStep < 0
            && startFloor >= 0 && floor >= 0 && floor != startFloor;
        public void Sample(int step, int physicalFloor, float coverage0, float coverage1)
        {
            if (Result == null || step < lastStep) return;
            Advance(step, physicalFloor);
            lastFloor = physicalFloor;
            if (step > 0 && NeedsSnapshot(physicalFloor))
            {
                Result.firstOtherFloorStep = step; Result.firstOtherFloorIndex = physicalFloor;
                Result.firstOtherFloorSeconds = step * delta;
                Result.floor0CoverageAtTransfer = coverage0;
                Result.floor1CoverageAtTransfer = coverage1;
            }
        }
        private void Advance(int step, int floor)
        {
            int count = step - lastStep;
            if (floor == 0) Result.floor0Steps += count;
            else if (floor == 1) Result.floor1Steps += count;
            else Result.transitOrUnclassifiedSteps += count;
            if (Result.firstOtherFloorStep >= 0)
            {
                if (floor == 0) after0 += count;
                if (floor == 1) after1 += count;
            }
            lastStep = step;
        }
        public AIFloorTimingResult Finish(int steps, float coverage0, float coverage1)
        {
            if (Result == null) return null;
            // Normally zero: final callback can precede a physics sample by one tick.
            // Keep unsampled tail in its last measured state; never invent a transfer.
            if (steps > lastStep) Advance(steps, lastFloor);
            Result.elapsedSeconds = lastStep * delta;
            Result.floor0Seconds = Result.floor0Steps * delta;
            Result.floor1Seconds = Result.floor1Steps * delta;
            Result.transitOrUnclassifiedSeconds = Result.transitOrUnclassifiedSteps * delta;
            Result.floor0SecondsAfterTransfer = after0 * delta;
            Result.floor1SecondsAfterTransfer = after1 * delta;
            if (Result.firstOtherFloorStep >= 0)
            {
                Result.secondsAfterFirstTransfer = (lastStep - Result.firstOtherFloorStep) * delta;
                Result.floor0CoverageGainAfterTransfer = coverage0 - Result.floor0CoverageAtTransfer;
                Result.floor1CoverageGainAfterTransfer = coverage1 - Result.floor1CoverageAtTransfer;
            }
            return Result;
        }
    }
}
