using System;
using Unity.MLAgents;
using UnityEngine;

namespace TesisQA.AI
{
    [Serializable]
    public sealed class AIEpisodeResult
    {
        public bool success;
        public int steps;
        public int lesson;
        public int startFloor;
        public int direction;
        public int ascentTransitions;
        public int descentTransitions;
        public int collisions;
        public int revisits;
        public int shortLoops;
        public int noNewAreaTimeouts;
        public int noDisplacementFailures;
        public int motionWarnings;
        public int stationaryFailures;
        public bool stairPractice;
        public int stairPracticeLevel;
        public bool stairPracticeGuided;
        public bool stairRehearsal;
        public float stairClimbProgress;
        public float stairClimbReward;
        public float stairApproachReward;
        public bool nearSearch;
        public int nearSearchLevel;
        public bool mixedSearch;
        public int mixedSearchLevel;
        public int stairTraversals;
        public int spawnStairSteps;
        public float pathLength;
        public float coverage;
        public float floorZeroCoverage;
        public float floorOneCoverage;
        public bool searchShapingActive;
        public float searchShapingReward;
        public float initialSearchDistance;
        public float finalSearchDistance;
        public int stepLimits;
        public float backtrackingGraceGranted;
        public float backtrackingGraceUsed;
        public AIFloorTimingResult floorTiming;
    }

    [DisallowMultipleComponent]
    public sealed class AIEpisodeMetrics : MonoBehaviour
    {
        private bool episodeActive;
        private Vector3 previousPosition;
        private float pathLength;
        private float directDistance;
        private int collisionCount;
        private int visitedAreaCount;
        private int revisitCount;
        private int shortLoopCount;
        private int noNewAreaTimeoutCount;
        private int noDisplacementFailureCount;
        private int motionWarningCount;
        private int stationaryFailureCount;
        private bool practiceActive;
        private int practiceLevel;
        private bool practiceGuided;
        private bool stairRehearsal;
        private int mainLesson;
        private float stairClimbProgress;
        private float stairClimbReward;
        private float stairApproachReward;
        private bool nearSearchActive;
        private int nearSearchLevel;
        private bool mixedSearchActive;
        private int mixedSearchLevel;
        private int stairTraversalCount;
        private int stairSeenCount;
        private int stairMidpointCount;
        private int stepsToStairSeen;
        private int stepsToStairEntry;
        private int stepsToOtherFloor;
        private int otherFloorReachedCount;
        private int targetReachedCount;
        private int startFloorIndex;
        private int lastConfirmedFloorIndex;
        private int pendingFloorIndex;
        private float pendingFloorElevation;
        private int floorTransitionCount;
        private int ascentTransitionCount;
        private int descentTransitionCount;
        private bool crossFloorEpisode;
        private int verticalRouteDirection;
        private int spawnCurriculumStage;
        private int spawnStairSteps;
        private int floorTransferSpawnSteps;
        private bool stairGuideEnabled;
        private float episodeCoverageTarget;
        private bool episodeRequiresEveryFloorCoverage;
        private bool searchShapingActive;
        private float searchShapingReward;
        private float initialSearchDistance;
        private float finalSearchDistance;
        private int stepLimits;
        private float backtrackingGraceGranted;
        private float backtrackingGraceUsed;
        private int trainingLesson;
        private bool terrainCurriculum;
        private bool terrainRehearsal;
        private int terrainLevel;
        private int terrainEpisodeLevel;
        private int terrainSpawnSteps;
        private const float FloorArrivalTolerance = 1.25f;
        private readonly AIFloorTiming floorTiming = new AIFloorTiming();
        public bool NeedsFloorTimingSnapshot(int floor) => floorTiming.NeedsSnapshot(floor);
        public void RecordFloorTiming(int step, int physicalFloor, float coverage0, float coverage1)
        {
            if (episodeActive) floorTiming.Sample(step, physicalFloor, coverage0, coverage1);
        }
        public int ConfirmedFloorIndex => episodeActive ? lastConfirmedFloorIndex : -1;
        public event Action EpisodeStarted;
        public event Action<AIEpisodeResult> EpisodeFinished;
        public int DiagnosticStairEntries => stairTraversalCount;

        public void BeginEpisode(
            Vector3 start,
            Vector3 target,
            bool isCrossFloorEpisode,
            int initialFloorIndex)
        {
            episodeActive = true;
            previousPosition = start;
            pathLength = 0f;
            directDistance = Vector3.Distance(start, target);
            collisionCount = 0;
            visitedAreaCount = 0;
            revisitCount = 0;
            shortLoopCount = 0;
            noNewAreaTimeoutCount = 0;
            noDisplacementFailureCount = 0;
            motionWarningCount = stationaryFailureCount = 0;
            practiceActive = practiceGuided = stairRehearsal = false;
            practiceLevel = -1;
            mainLesson = -1;
            stairClimbProgress = stairClimbReward = 0f;
            stairApproachReward = 0f;
            nearSearchActive = false;
            nearSearchLevel = -1;
            mixedSearchActive = false;
            mixedSearchLevel = -1;
            stairTraversalCount = 0;
            stairSeenCount = 0;
            stairMidpointCount = 0;
            stepsToStairSeen = -1;
            stepsToStairEntry = -1;
            stepsToOtherFloor = -1;
            otherFloorReachedCount = 0;
            targetReachedCount = 0;
            startFloorIndex = initialFloorIndex;
            lastConfirmedFloorIndex = initialFloorIndex;
            floorTiming.Begin(initialFloorIndex, Time.fixedDeltaTime);
            pendingFloorIndex = -1;
            pendingFloorElevation = 0f;
            floorTransitionCount = 0;
            ascentTransitionCount = 0;
            descentTransitionCount = 0;
            spawnCurriculumStage = -1;
            floorTransferSpawnSteps = -1;
            trainingLesson = -1;
            terrainCurriculum = terrainRehearsal = false;
            stairGuideEnabled = false;
            episodeRequiresEveryFloorCoverage = true;
            searchShapingActive = false;
            searchShapingReward = 0f;
            initialSearchDistance = finalSearchDistance = 0f;
            stepLimits = 0;
            backtrackingGraceGranted = backtrackingGraceUsed = 0f;
            crossFloorEpisode = isCrossFloorEpisode;
            float heightDifference = target.y - start.y;
            verticalRouteDirection = heightDifference > 1f
                ? 1
                : heightDifference < -1f
                    ? -1
                    : 0;
            EpisodeStarted?.Invoke();
        }

        public void SetSpawnCurriculum(
            int stage,
            int stairSteps,
            float coverageTarget,
            bool requiresEveryFloorCoverage,
            bool hasStairGuide)
        {
            spawnCurriculumStage = stage;
            spawnStairSteps = stairSteps;
            episodeCoverageTarget = coverageTarget;
            episodeRequiresEveryFloorCoverage = requiresEveryFloorCoverage;
            stairGuideEnabled = hasStairGuide;
        }

        public void SetSearchShaping(bool active, float initialDistance)
        {
            searchShapingActive = active;
            initialSearchDistance = finalSearchDistance = initialDistance;
        }

        public void RegisterSearchShaping(float reward, float distance)
        {
            searchShapingReward += reward;
            finalSearchDistance = distance;
        }

        public void SetBacktrackingGrace(float granted, float used)
        {
            backtrackingGraceGranted = granted;
            backtrackingGraceUsed = used;
        }

        public void RegisterStepLimit() => stepLimits++;

        public void SetTrainingLesson(int lesson)
        {
            trainingLesson = lesson;
        }

        public void SetTerrainCurriculum(int level, int episodeLevel, bool rehearsal, int spawnSteps)
        {
            terrainCurriculum = true;
            terrainLevel = level;
            terrainEpisodeLevel = episodeLevel;
            terrainRehearsal = rehearsal;
            terrainSpawnSteps = spawnSteps;
        }

        public void RegisterMotionWarning() => motionWarningCount++;
        public void RegisterStationaryFailure() => stationaryFailureCount++;

        public void SetStairPractice(bool active, int level, bool guided, bool rehearsal, int mainStage)
        {
            practiceActive = active;
            practiceLevel = level;
            practiceGuided = guided;
            stairRehearsal = rehearsal;
            mainLesson = mainStage;
        }

        public void SetStairClimbProgress(float progress, float reward)
        {
            stairClimbProgress = progress;
            stairClimbReward = reward;
        }

        public void SetStairApproachReward(float reward) => stairApproachReward = reward;
        public void SetNearSearch(bool active, int level) { nearSearchActive = active; nearSearchLevel = level; }
        public void SetMixedSearch(bool active, int level) { mixedSearchActive = active; mixedSearchLevel = level; }

        public void SetCoverageRequirement(float target, bool onEveryFloor)
        {
            episodeCoverageTarget = target;
            episodeRequiresEveryFloorCoverage = onEveryFloor;
        }

        public void SetFloorTransferSpawnSteps(int steps)
        {
            floorTransferSpawnSteps = steps;
        }

        public void TrackPosition(Vector3 position)
        {
            if (!episodeActive)
            {
                return;
            }

            pathLength += Vector3.Distance(previousPosition, position);
            previousPosition = position;
            ConfirmPendingFloorArrival(position.y);
        }

        public void RegisterCollision()
        {
            collisionCount++;
        }

        public void RegisterVisitedArea()
        {
            visitedAreaCount++;
        }

        public void RegisterRevisit()
        {
            revisitCount++;
        }

        public void RegisterShortLoop()
        {
            shortLoopCount++;
        }

        public void RegisterNoNewAreaTimeout()
        {
            noNewAreaTimeoutCount++;
        }

        public void RegisterNoDisplacementFailure()
        {
            noDisplacementFailureCount++;
        }

        public void RegisterStairSeen(int steps)
        {
            stairSeenCount++;
            if (stepsToStairSeen < 0)
            {
                stepsToStairSeen = steps;
            }
        }

        public void RegisterStairTraversal(int steps)
        {
            stairTraversalCount++;
            if (stepsToStairEntry < 0)
            {
                stepsToStairEntry = steps;
            }
        }

        public void RegisterStairMidpoint(int steps)
        {
            stairMidpointCount++;
        }

        public void RegisterOtherFloorReached(int steps)
        {
            otherFloorReachedCount++;
            if (stepsToOtherFloor < 0)
            {
                stepsToOtherFloor = steps;
            }
        }

        public void RegisterFloorAreaEntered(
            int floorIndex,
            float floorElevation,
            float agentElevation)
        {
            if (!episodeActive || startFloorIndex < 0 || floorIndex < 0
                || floorIndex == lastConfirmedFloorIndex)
            {
                return;
            }

            pendingFloorIndex = floorIndex;
            pendingFloorElevation = floorElevation;
            ConfirmPendingFloorArrival(agentElevation);
        }

        private void ConfirmPendingFloorArrival(float agentElevation)
        {
            if (!episodeActive || pendingFloorIndex < 0
                || Mathf.Abs(agentElevation - pendingFloorElevation)
                    > FloorArrivalTolerance)
            {
                return;
            }

            if (pendingFloorIndex > lastConfirmedFloorIndex)
            {
                ascentTransitionCount++;
            }
            else
            {
                descentTransitionCount++;
            }

            floorTransitionCount++;
            lastConfirmedFloorIndex = pendingFloorIndex;
            pendingFloorIndex = -1;
        }

        public void RegisterTargetReached()
        {
            targetReachedCount++;
        }

        public void FinishEpisode(
            bool success,
            int steps,
            float coverage,
            float floorZeroCoverage,
            float floorOneCoverage)
        {
            if (!episodeActive || !Academy.IsInitialized)
            {
                episodeActive = false;
                return;
            }

            episodeActive = false;
            AIFloorTimingResult timing = floorTiming.Finish(steps, floorZeroCoverage, floorOneCoverage);
            float efficiency = success
                ? directDistance / Mathf.Max(directDistance, pathLength)
                : 0f;

            StatsRecorder stats = Academy.Instance.StatsRecorder;
            stats.Add("AI/Timing Other Floor Reached", timing.firstOtherFloorStep >= 0 ? 1f : 0f);
            stats.Add("AI/Seconds On Floor 0", (float)timing.floor0Seconds);
            stats.Add("AI/Seconds On Floor 1", (float)timing.floor1Seconds);
            stats.Add("AI/Seconds In Transit Or Unclassified", (float)timing.transitOrUnclassifiedSeconds);
            if (timing.firstOtherFloorStep >= 0)
            {
                // Never mix -1 (not reached) into the conditional means.
                stats.Add("AI/First Physical Floor Transfer Step", timing.firstOtherFloorStep);
                stats.Add("AI/Seconds After First Floor Transfer", (float)timing.secondsAfterFirstTransfer);
                stats.Add("AI/Floor 0 Coverage Gain After Transfer", timing.floor0CoverageGainAfterTransfer);
                stats.Add("AI/Floor 1 Coverage Gain After Transfer", timing.floor1CoverageGainAfterTransfer);
            }
            stats.Add("AI/Success", success ? 1f : 0f);
            if (terrainCurriculum)
            {
                stats.Add("AI/Terrain Level", terrainLevel);
                stats.Add("AI/Terrain Episode Level", terrainEpisodeLevel);
                stats.Add("AI/Terrain Rehearsal", terrainRehearsal ? 1f : 0f);
                // Atomic packet: level*100 + replay*16 + far*8 + upper-start*4 + up*2 + success.
                stats.Add("AI/Terrain Outcome", terrainLevel * 100 + (terrainRehearsal ? 16 : 0)
                    + (terrainSpawnSteps > 2 ? 8 : 0) + (startFloorIndex == 1 ? 4 : 0)
                    + (verticalRouteDirection > 0 ? 2 : 0) + (success ? 1 : 0), StatAggregationMethod.Sum);
            }
            stats.Add("AI/Stair Search Shaping Active", searchShapingActive ? 1f : 0f);
            stats.Add("AI/Stair Search Shaping Reward", searchShapingReward);
            stats.Add("AI/Stair Search Initial Distance", initialSearchDistance);
            stats.Add("AI/Stair Search Final Distance", finalSearchDistance);
            stats.Add("AI/Step Limit", stepLimits);
            stats.Add("AI/Backtracking Grace Granted", backtrackingGraceGranted);
            stats.Add("AI/Backtracking Grace Used", backtrackingGraceUsed);
            if (trainingLesson >= 0)
            {
                stats.Add("AI/Lesson", trainingLesson);
            }
            if (floorTransferSpawnSteps >= 0)
            {
                stats.Add("AI/Floor Transfer Spawn Steps", floorTransferSpawnSteps);
            }
            stats.Add("AI/Episode Steps", steps);
            stats.Add("AI/Path Length", pathLength);
            stats.Add("AI/Path Efficiency", efficiency);
            stats.Add("AI/Collisions", collisionCount);
            stats.Add("AI/Visited Areas", visitedAreaCount);
            stats.Add("AI/Revisits", revisitCount);
            stats.Add("AI/Short Loops", shortLoopCount);
            stats.Add("AI/No New Area Timeout", noNewAreaTimeoutCount);
            stats.Add("AI/No Displacement Failure", noDisplacementFailureCount);
            stats.Add("AI/Anti Spin Warnings", motionWarningCount);
            stats.Add("AI/Stationary Failure", stationaryFailureCount);
            if (mainLesson >= 0) stats.Add("AI/Main Lesson", mainLesson);
            if (mixedSearchActive && !stairRehearsal)
            {
                stats.Add("AI/Mixed Search Level", mixedSearchLevel);
                stats.Add("AI/Mixed Search Max Tiles", AIMixedSearchSchedule.MaxTiles(mixedSearchLevel));
                stats.Add("AI/Mixed Search Far Probability", AIMixedSearchSchedule.FarProbability(mixedSearchLevel));
                stats.Add("AI/Mixed Search Outcome", mixedSearchLevel * 100
                    + (floorTransferSpawnSteps > 2 ? 4 : 0)
                    + (verticalRouteDirection > 0 ? 2 : 0) + (success ? 1 : 0), StatAggregationMethod.Sum);
            }
            stats.Add("AI/Stair Rehearsal", stairRehearsal ? 1f : 0f);
            if (nearSearchActive && !stairRehearsal)
            {
                stats.Add("AI/Near Search Level", nearSearchLevel);
                stats.Add("AI/Near Search Yaw Range", AINearSearchSchedule.YawRange(nearSearchLevel));
                stats.Add("AI/Near Search Max Tiles", AINearSearchSchedule.MaxTiles(nearSearchLevel));
                stats.Add("AI/Near Search Outcome", nearSearchLevel * 10
                    + (verticalRouteDirection > 0 ? 2 : 0) + (success ? 1 : 0), StatAggregationMethod.Sum);
            }
            if (practiceActive)
            {
                stats.Add("AI/Stair Practice Level", practiceLevel);
                stats.Add("AI/Stair Practice Guided", practiceGuided ? 1f : 0f);
                stats.Add("AI/Stair Climb Progress", stairClimbProgress);
                stats.Add("AI/Stair Climb Reward", stairClimbReward);
                stats.Add("AI/Stair Approach Reward", stairApproachReward);
                if (stairRehearsal) stats.Add("AI/Stair Rehearsal Success", success ? 1f : 0f);
                if (trainingLesson == 1)
                    stats.Add("AI/Stair Practice Outcome", practiceLevel * 10
                        + (verticalRouteDirection > 0 ? 2 : 0) + (success ? 1 : 0), StatAggregationMethod.Sum);
            }
            if (Academy.Instance.EnvironmentParameters.GetWithDefault("mastery_protocol", 0f) >= 1f
                && trainingLesson >= 0 && trainingLesson <= 5)
            {
                // Atomic per-episode outcome: stage*10 + (up ? 2 : 0) + success.
                // SUM preserves the outcome in the stats side channel (one agent).
                stats.Add("AI/Mastery Outcome", trainingLesson * 10
                    + (verticalRouteDirection > 0 ? 2 : 0) + (success ? 1 : 0),
                    StatAggregationMethod.Sum);
            }
            stats.Add("AI/Coverage", coverage);
            stats.Add("AI/Coverage Target", episodeCoverageTarget);
            stats.Add("AI/Floor 0 Coverage", floorZeroCoverage);
            stats.Add("AI/Floor 1 Coverage", floorOneCoverage);
            stats.Add(
                "AI/Minimum Floor Coverage",
                episodeRequiresEveryFloorCoverage
                    ? Mathf.Min(floorZeroCoverage, floorOneCoverage)
                    : floorZeroCoverage);
            stats.Add(
                "AI/Floor Coverage Difference",
                episodeRequiresEveryFloorCoverage
                    ? Mathf.Abs(floorZeroCoverage - floorOneCoverage)
                    : 0f);
            stats.Add("AI/Stair Traversals", stairTraversalCount);
            stats.Add("AI/Stair Seen", stairSeenCount);
            stats.Add("AI/Stair Midpoint", stairMidpointCount);
            stats.Add("AI/Steps To Stair Seen", stepsToStairSeen);
            stats.Add("AI/Steps To Stair Entry", stepsToStairEntry);
            stats.Add("AI/Steps To Other Floor", stepsToOtherFloor);
            stats.Add("AI/Other Floor Reached", otherFloorReachedCount);
            stats.Add("AI/Target Reached", targetReachedCount);
            if (startFloorIndex >= 0)
            {
                stats.Add("AI/Floor Transitions", floorTransitionCount);
                stats.Add("AI/Ascent Transitions", ascentTransitionCount);
                stats.Add("AI/Descent Transitions", descentTransitionCount);
            }
            if (spawnCurriculumStage >= 0)
            {
                stats.Add("AI/Training Stage", spawnCurriculumStage);
                stats.Add("AI/Spawn Stair Path Steps", spawnStairSteps);
                stats.Add("AI/Stair Guide Enabled", stairGuideEnabled ? 1f : 0f);
            }
            stats.Add("AI/Cross Floor Episode", crossFloorEpisode ? 1f : 0f);
            stats.Add(
                "AI/Cross Floor Success",
                crossFloorEpisode && success ? 1f : 0f);
            if (verticalRouteDirection > 0)
            {
                if (startFloorIndex >= 0)
                {
                    stats.Add("AI/Ascent Reached", ascentTransitionCount > 0 ? 1f : 0f);
                }
                stats.Add("AI/Stair Ascent Success", success ? 1f : 0f);
            }
            else if (verticalRouteDirection < 0)
            {
                if (startFloorIndex >= 0)
                {
                    stats.Add("AI/Descent Reached", descentTransitionCount > 0 ? 1f : 0f);
                }
                stats.Add("AI/Stair Descent Success", success ? 1f : 0f);
            }
            EpisodeFinished?.Invoke(new AIEpisodeResult
            {
                success = success,
                steps = steps,
                lesson = trainingLesson,
                startFloor = startFloorIndex,
                direction = verticalRouteDirection,
                ascentTransitions = ascentTransitionCount,
                descentTransitions = descentTransitionCount,
                collisions = collisionCount,
                revisits = revisitCount,
                shortLoops = shortLoopCount,
                noNewAreaTimeouts = noNewAreaTimeoutCount,
                noDisplacementFailures = noDisplacementFailureCount,
                motionWarnings = motionWarningCount,
                stationaryFailures = stationaryFailureCount,
                stairPractice = practiceActive,
                stairPracticeLevel = practiceLevel,
                stairPracticeGuided = practiceGuided,
                stairRehearsal = stairRehearsal,
                stairClimbProgress = stairClimbProgress,
                stairClimbReward = stairClimbReward,
                stairApproachReward = stairApproachReward,
                nearSearch = nearSearchActive,
                nearSearchLevel = nearSearchLevel,
                mixedSearch = mixedSearchActive,
                mixedSearchLevel = mixedSearchLevel,
                stairTraversals = stairTraversalCount,
                spawnStairSteps = floorTransferSpawnSteps,
                pathLength = pathLength,
                coverage = coverage,
                floorZeroCoverage = floorZeroCoverage,
                floorOneCoverage = floorOneCoverage,
                searchShapingActive = searchShapingActive,
                searchShapingReward = searchShapingReward,
                initialSearchDistance = initialSearchDistance,
                finalSearchDistance = finalSearchDistance,
                stepLimits = stepLimits,
                backtrackingGraceGranted = backtrackingGraceGranted,
                backtrackingGraceUsed = backtrackingGraceUsed,
                floorTiming = timing
            });
        }
    }
}
