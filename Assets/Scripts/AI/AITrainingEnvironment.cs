using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;

namespace TesisQA.AI
{
    [DisallowMultipleComponent]
    public sealed class AITrainingEnvironment : MonoBehaviour
    {
        [Header("Phase")]
        [SerializeField] private AITrainingPhase phase = AITrainingPhase.MovementControl;

        [Tooltip("Phase 3 only: 0 = legacy; 1 = pooled coverage budget; 2 = split per-floor coverage budget. Profiles 1/2 share balance reward and timeouts.")]
        [SerializeField, Range(0, 2)] private int phase3RewardRevision;

        [Header("References")]
        [SerializeField] private ExplorationTarget target;
        [SerializeField] private AIWFCTrainingAdapter wfcAdapter;
        [SerializeField] private AIEpisodeMetrics metrics;

        [Header("Episode")]
        [SerializeField] private int randomSeed = 12345;
        [SerializeField, Min(0f)] private float minimumSpawnDistance = 6f;
        [Tooltip("Nilai 0 berarti tidak ada batas maksimum.")]
        [SerializeField, Min(0f)] private float maximumSpawnDistance;
        [SerializeField] private bool randomizeAgentYaw = true;
        [Tooltip("Untuk scene fixed-spawn dua lantai: episode bergantian bawah-ke-atas dan atas-ke-bawah.")]
        [SerializeField] private bool alternateFixedSpawnDirection;
        [SerializeField] private float killHeight = -5f;
        [SerializeField, Min(0f)] private float targetHeightOffset = 0.5f;

        [Header("Phase 1 Lessons")]
        [SerializeField] private GameObject phaseOneFlatRoot;
        [SerializeField] private GameObject phaseOneStairRoot;
        [SerializeField, Range(0, 1)] private int defaultPhaseOneLesson;

        [Header("Exploration Reward")]
        [SerializeField, Range(0f, 0.1f)] private float newAreaReward = 0.01f;
        [SerializeField, Range(0f, 1f)] private float maximumAreaRewardPerEpisode = 0.3f;
        [Tooltip("Bonus kunjungan pertama ke area lantai 2 (floorIndex 1). Tidak diberikan saat revisit.")]
        [SerializeField, Range(0f, 0.1f)] private float upperFloorNewAreaRewardBonus;
        [Tooltip("Anggaran bonus lantai 2 terpisah dari reward area dasar agar tidak habis di lantai 1.")]
        [SerializeField, Range(0f, 1f)] private float maximumUpperFloorAreaBonusPerEpisode = 1f;

        [Header("Coverage Completion")]
        [SerializeField] private bool requireCoverageBeforeGoal;
        [SerializeField, Range(0.01f, 1f)] private float targetCoverage = 0.25f;
        [Tooltip("Jika aktif, target coverage harus dicapai pada setiap lantai, bukan hanya total map.")]
        [SerializeField] private bool requireCoverageOnEveryFloor;
        [Tooltip("Mencegah Agent menyelesaikan coverage dua lantai hanya dengan menyentuh trigger lantai lain.")]
        [SerializeField] private bool requireStairTraversalForCoverage;
        [Tooltip("Khusus scene seperti Phase 3A: agent harus maju sejauh ini di lantai tujuan sebelum episode selesai. Nilai 0 menonaktifkan syarat.")]
        [SerializeField, Min(0f)] private float minimumTravelOnOtherFloorToComplete;
        [Tooltip("Selisih coverage maksimum antara lantai 1 dan lantai 2 agar eksplorasi tetap seimbang.")]
        [SerializeField, Range(0f, 1f)] private float maximumFloorCoverageDifference = 0.1f;
        [Tooltip("Longgarkan keseimbangan pada lesson awal, lalu ketatkan otomatis saat target coverage meningkat.")]
        [SerializeField] private bool useProgressiveFloorBalance = true;
        [SerializeField, Range(0f, 2f)] private float coverageCompletionReward = 1f;
        [SerializeField, Range(0f, 0.5f)] private float earlyGoalReward = 0.1f;
        [SerializeField, Range(0f, 0.2f)] private float firstStairSeenReward = 0.02f;
        [SerializeField, Range(0f, 0.2f)] private float firstStairTraversalReward = 0.03f;
        [SerializeField, Range(0f, 0.2f)] private float firstStairMidpointReward = 0.08f;
        [SerializeField, Range(0f, 1f)] private float firstOtherFloorReward = 0.2f;
        [Tooltip("Bonus sukses naik tangga hanya untuk curriculum transfer bertahap Phase 2.")]
        [SerializeField, Range(0f, 1f)] private float graduatedTransferAscentBonus = 0.4f;
        [Tooltip("Hanya untuk pengujian tanpa trainer; training memakai parameter stair_search_shaping.")]
        [SerializeField] private bool defaultStairSearchShaping;
        [SerializeField, Range(0f, 5f)] private float balancedCoverageRewardScale = 1.5f;
        [SerializeField, Range(0f, 1f)] private float maximumBalancedCoverageReward = 0.3f;

        [Header("Episodic Anti-Loop Memory")]
        [SerializeField, Min(2)] private int recentAreaMemorySize = 8;
        [SerializeField, Range(-0.05f, 0f)] private float shortLoopPenalty = -0.003f;
        [SerializeField, Min(1f)] private float noNewAreaTimeout = 15f;
        [SerializeField, Min(1f)] private float twoFloorNoNewAreaTimeout = 15f;
        [SerializeField, Range(-1f, 0f)] private float noNewAreaPenalty = -0.1f;
        [SerializeField, Range(-1f, 0f)] private float twoFloorNoNewAreaPenalty = -0.35f;

        [Header("Target Visibility")]
        [SerializeField] private bool phaseOneTargetAlwaysVisible = true;
        [SerializeField, Min(0.1f)] private float targetVisibilityDistance = 30f;
        [SerializeField] private LayerMask visibilityObstacleMask = ~0;
        [SerializeField, Min(0f)] private float eyeHeight = 1.4f;

        private readonly HashSet<ExplorationArea> visitedAreas = new HashSet<ExplorationArea>();
        private readonly Dictionary<ExplorationArea, int> areaVisitCounts =
            new Dictionary<ExplorationArea, int>();
        private readonly Queue<bool> recentRevisits = new Queue<bool>();
        private readonly HashSet<StairTraversalArea> visitedStairs =
            new HashSet<StairTraversalArea>();
        private readonly HashSet<StairTraversalArea> visitedStairMidpoints =
            new HashSet<StairTraversalArea>();
        private readonly HashSet<int> visitedFloors = new HashSet<int>();
        private AITrainingSpawnPoint[] spawnPoints;
        private ExplorationArea[] explorationAreas;
        private System.Random random;
        private int episodeIndex;
        private float awardedAreaReward;
        private float awardedUpperFloorAreaBonus;
        private int recentRevisitCount;
        private int currentAreaVisitCount;
        private float timeSinceLastNewArea;
        private float episodeNoNewAreaTimeout;
        private float episodeTargetCoverage;
        private float episodeMaximumFloorCoverageDifference;
        private float bestMinimumFloorCoverage;
        private float awardedBalancedCoverageReward;
        private bool reachedOtherFloor;
        private bool targetReachedThisEpisode;
        private bool crossFloorEpisode;
        private bool episodeFinished;
        private bool episodeRequiresEveryFloorCoverage;
        private bool episodeRequiresStairTraversal;
        private bool stairSeenThisEpisode;
        private Vector3 otherFloorEntryPosition;
        private bool otherFloorEntryRecorded;
        private int transferDestinationFloorIndex = -1;
        private readonly AIStairSearchPotential searchPotential = new AIStairSearchPotential();
        private bool searchShapingActive;
        private float lastSearchDistance;
        private readonly AIExplorationProgressGuard progressGuard = new AIExplorationProgressGuard();
        private float bestTransferSearchDistance;
        private int lastProgressFloor;
        private readonly AIStairPracticeProgress stairPracticeProgress = new AIStairPracticeProgress();

        private const int StairRaysPerDirection = 8;
        private const float StairRayDegrees = 180f;
        private const float StairRayLength = 32f;
        private const float StairSphereCastRadius = 0.08f;

        public AITrainingPhase Phase => phase;
        public DefectExperimentSession DefectExperiment { get; set; }
        public int Phase3RewardRevision => phase == AITrainingPhase.ComplexRouting ? phase3RewardRevision : 0;
        public bool IsTwoFloorCoverageLesson =>
            (phase == AITrainingPhase.MultiroomNavigation || Phase3RewardRevision >= 1)
            && wfcAdapter != null
            && wfcAdapter.IsTwoFloorLesson;
        private bool UsesBalancedPhase3Reward => Phase3RewardRevision >= 1
            && requireCoverageBeforeGoal && episodeRequiresEveryFloorCoverage
            && (wfcAdapter == null || !wfcAdapter.IsFloorTransferLesson);
        public float KillHeight => killHeight;
        public Transform TargetTransform => target != null ? target.transform : null;
        public bool UsesEpisodicMemory => phase != AITrainingPhase.MovementControl;
        public bool AllowsCompletedBranchAssist => UsesEpisodicMemory && requireCoverageBeforeGoal
            && (wfcAdapter == null || !wfcAdapter.IsFloorTransferLesson);
        public int NavigationProgressVersion => progressGuard.ProgressVersion;
        public float CurrentAreaVisitObservation => UsesEpisodicMemory
            ? Mathf.Clamp01(currentAreaVisitCount / 5f)
            : 0f;
        public float TimeSinceNewAreaObservation => UsesEpisodicMemory
            ? Mathf.Clamp01(timeSinceLastNewArea / Mathf.Max(1f, episodeNoNewAreaTimeout))
            : 0f;
        public float RecentRevisitRatioObservation => UsesEpisodicMemory
            && recentRevisits.Count > 0
                ? recentRevisitCount / (float)recentRevisits.Count
                : 0f;
        public float FloorTransferProgressObservation
        {
            get
            {
                if (!UsesEpisodicMemory)
                {
                    return 0f;
                }

                if (wfcAdapter != null && wfcAdapter.UsesFloorCountCurriculum)
                {
                    return !wfcAdapter.IsTwoFloorLesson ? -1f
                        : reachedOtherFloor ? 0f : 1f;
                }

                if (wfcAdapter != null
                    && wfcAdapter.UsesStairSpawnCurriculum
                    && !wfcAdapter.UsesSingleFloorExplorationLesson)
                {
                    return reachedOtherFloor
                        ? 0f
                        : wfcAdapter.StairTaskDirectionObservation;
                }

                return reachedOtherFloor ? 1f : 0f;
            }
        }

        private void Awake()
        {
            random = new System.Random(randomSeed);
            spawnPoints = GetComponentsInChildren<AITrainingSpawnPoint>(true);
            explorationAreas = GetComponentsInChildren<ExplorationArea>(true);
            if (metrics == null)
            {
                metrics = GetComponent<AIEpisodeMetrics>();
            }
        }

        public bool BeginEpisode(ExplorationAgent agent)
        {
            if (agent == null || target == null)
            {
                Debug.LogError("AI Environment: Agent atau Target belum diisi.", this);
                return false;
            }

            int phaseOneLesson = 0;
            if (phase == AITrainingPhase.MovementControl
                && phaseOneFlatRoot != null && phaseOneStairRoot != null)
            {
                phaseOneLesson = Academy.IsInitialized
                    ? Mathf.Clamp(Mathf.RoundToInt(
                        Academy.Instance.EnvironmentParameters.GetWithDefault(
                            "phase1_lesson", defaultPhaseOneLesson)), 0, 1)
                    : defaultPhaseOneLesson;
                phaseOneFlatRoot.SetActive(phaseOneLesson == 0);
                phaseOneStairRoot.SetActive(phaseOneLesson == 1);
            }

            float curriculumMinimumDistance = minimumSpawnDistance;
            float curriculumMaximumDistance = maximumSpawnDistance;
            episodeTargetCoverage = targetCoverage;
            if (Academy.IsInitialized)
            {
                curriculumMinimumDistance = Academy.Instance.EnvironmentParameters.GetWithDefault(
                    "minimum_spawn_distance",
                    minimumSpawnDistance);
                curriculumMaximumDistance = Academy.Instance.EnvironmentParameters.GetWithDefault(
                    "maximum_spawn_distance",
                    maximumSpawnDistance);
                episodeTargetCoverage = Academy.Instance.EnvironmentParameters.GetWithDefault(
                    "target_coverage",
                    targetCoverage);
            }
            episodeTargetCoverage = Mathf.Clamp01(episodeTargetCoverage);
            episodeRequiresEveryFloorCoverage = requireCoverageOnEveryFloor;
            episodeRequiresStairTraversal = requireStairTraversalForCoverage;

            if (!TryChooseEpisodePositions(
                    curriculumMinimumDistance,
                    curriculumMaximumDistance,
                    out Vector3 agentPosition,
                    out Vector3 targetPosition))
            {
                Debug.LogError("AI Environment: tidak menemukan pasangan spawn Agent dan Target.", this);
                return false;
            }

            if (wfcAdapter != null && wfcAdapter.UsesStairSpawnCurriculum)
            {
                // Lesson 0 isolates exploration on one floor. Later lessons combine
                // stair search with coverage that grows together with spawn radius.
                episodeTargetCoverage = wfcAdapter.CurriculumCoverageTarget;
                episodeRequiresEveryFloorCoverage = requireCoverageOnEveryFloor
                    && wfcAdapter.RequiresEveryFloorCoverage;
                episodeRequiresStairTraversal = requireStairTraversalForCoverage
                    && wfcAdapter.RequiresStairTraversalForCoverage;
                // The target is only a spawn/metrics anchor in this curriculum. Keep
                // it out of RayPerception so the policy must explore for the stair.
                target.gameObject.layer = 2; // Ignore Raycast
            }
            else if (wfcAdapter != null && wfcAdapter.UsesFloorCountCurriculum)
            {
                if (wfcAdapter.IsGraduatedCoverageLesson)
                {
                    episodeTargetCoverage = wfcAdapter.GraduatedFloorCoverageTarget;
                }
                episodeRequiresEveryFloorCoverage = requireCoverageOnEveryFloor
                    && wfcAdapter.IsTwoFloorLesson;
                episodeRequiresStairTraversal = requireStairTraversalForCoverage
                    && wfcAdapter.IsTwoFloorLesson;
                target.gameObject.layer = 2; // Coverage, not target seeking.
            }
            episodeMaximumFloorCoverageDifference = wfcAdapter != null
                && wfcAdapter.IsGraduatedCoverageLesson
                    ? maximumFloorCoverageDifference
                    : useProgressiveFloorBalance
                        ? GetProgressiveFloorCoverageDifference(episodeTargetCoverage)
                        : maximumFloorCoverageDifference;
            if (wfcAdapter != null && wfcAdapter.TerrainCurriculumEnabled)
            {
                episodeTargetCoverage = AITerrainCurriculum.Coverage(wfcAdapter.TerrainEpisodeLevel);
                // Always at most ten percentage points per episode, even at 10% coverage.
                episodeMaximumFloorCoverageDifference = Mathf.Min(maximumFloorCoverageDifference, 0.1f);
            }
            episodeNoNewAreaTimeout = IsTwoFloorCoverageLesson
                ? Mathf.Max(noNewAreaTimeout, twoFloorNoNewAreaTimeout)
                : noNewAreaTimeout;
            if (wfcAdapter != null && wfcAdapter.UsesStairSpawnCurriculum)
            {
                episodeNoNewAreaTimeout = wfcAdapter.CurrentTrainingStage == 1
                    ? 15f
                    : wfcAdapter.CurrentTrainingStage == 2
                        ? 20f
                        : noNewAreaTimeout;
            }

            episodeIndex++;
            explorationAreas = GetComponentsInChildren<ExplorationArea>(true);
            visitedAreas.Clear();
            areaVisitCounts.Clear();
            recentRevisits.Clear();
            visitedStairs.Clear();
            visitedStairMidpoints.Clear();
            visitedFloors.Clear();
            awardedAreaReward = 0f;
            awardedUpperFloorAreaBonus = 0f;
            awardedBalancedCoverageReward = 0f;
            bestMinimumFloorCoverage = 0f;
            reachedOtherFloor = false;
            otherFloorEntryRecorded = false;
            recentRevisitCount = 0;
            currentAreaVisitCount = 0;
            timeSinceLastNewArea = 0f;
            progressGuard.Reset();
            bestTransferSearchDistance = float.PositiveInfinity;
            targetReachedThisEpisode = false;
            episodeFinished = false;
            stairSeenThisEpisode = false;
            crossFloorEpisode = Mathf.Abs(agentPosition.y - targetPosition.y) >= 3f;
            transferDestinationFloorIndex = wfcAdapter != null
                && wfcAdapter.IsFloorTransferLesson
                    ? wfcAdapter.GetFloorIndexAt(targetPosition) : -1;

            target.transform.position = targetPosition + Vector3.up * targetHeightOffset;
            Quaternion rotation;
            if (DefectExperiment != null)
            {
                rotation = DefectExperiment.SpawnRotation;
            }
            else if (wfcAdapter != null
                && wfcAdapter.TryGetCurriculumSpawnRotation(random, out Quaternion curriculumRotation))
            {
                rotation = curriculumRotation;
            }
            else
            {
                rotation = randomizeAgentYaw
                    ? Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f)
                    : Quaternion.identity;
            }
            agent.Motor.ResetMotor(agentPosition, rotation);
            agent.ResetEpisodeState();
            metrics?.BeginEpisode(
                agentPosition,
                target.transform.position,
                crossFloorEpisode,
                wfcAdapter != null ? wfcAdapter.GetFloorIndexAt(agentPosition) : -1);
            metrics?.SetCoverageRequirement(episodeTargetCoverage, episodeRequiresEveryFloorCoverage);
            Academy.Instance.StatsRecorder.Add("AI/Phase3 Reward Revision", Phase3RewardRevision);
            Academy.Instance.StatsRecorder.Add("AI/No New Area Timeout Limit", episodeNoNewAreaTimeout);
            if (wfcAdapter != null && wfcAdapter.TerrainCurriculumEnabled)
                metrics?.SetTerrainCurriculum(wfcAdapter.TerrainLevel, wfcAdapter.TerrainEpisodeLevel,
                    wfcAdapter.TerrainRehearsal, wfcAdapter.LastSpawnStairSteps);
            lastProgressFloor = metrics != null ? metrics.ConfirmedFloorIndex : -1;
            searchPotential.Reset();
            stairPracticeProgress.Reset();
            lastSearchDistance = 0f;
            searchShapingActive = wfcAdapter != null && wfcAdapter.UsesGraduatedTransferLessons
                && wfcAdapter.IsFloorTransferLesson
                && (Academy.IsInitialized ? Academy.Instance.EnvironmentParameters.GetWithDefault(
                    "stair_search_shaping", defaultStairSearchShaping ? 1f : 0f) >= 0.5f
                    : defaultStairSearchShaping)
                && wfcAdapter.TryGetStairSearchDistance(agentPosition, out lastSearchDistance);
            metrics?.SetSearchShaping(searchShapingActive, lastSearchDistance);
            if (wfcAdapter != null)
            {
                metrics?.SetStairPractice(wfcAdapter.StairPracticeEnabled && wfcAdapter.IsFloorTransferLesson,
                    wfcAdapter.EpisodePracticeLevel, wfcAdapter.StairGuideEnabled,
                    wfcAdapter.IsStairRehearsal, wfcAdapter.MainTrainingStage);
                metrics?.SetNearSearch(wfcAdapter.NearSearchEnabled && wfcAdapter.CurrentTrainingStage == 2,
                    wfcAdapter.NearSearchLevel);
                metrics?.SetMixedSearch(wfcAdapter.MixedSearchEnabled && wfcAdapter.CurrentTrainingStage == 3,
                    wfcAdapter.MixedSearchLevel);
            }
            if (wfcAdapter != null && wfcAdapter.IsFloorTransferLesson
                && wfcAdapter.TryGetStairSearchDistance(agentPosition, out float initialRouteDistance))
                bestTransferSearchDistance = initialRouteDistance;
            if (phase == AITrainingPhase.MovementControl)
            {
                metrics?.SetTrainingLesson(phaseOneLesson);
            }
            else if (wfcAdapter != null && wfcAdapter.UsesFloorCountCurriculum)
            {
                metrics?.SetTrainingLesson(wfcAdapter.CurrentTrainingStage);
                if (wfcAdapter.IsFloorTransferLesson)
                {
                    metrics?.SetFloorTransferSpawnSteps(
                        wfcAdapter.LastSpawnStairSteps);
                }
            }
            if (wfcAdapter != null && wfcAdapter.UsesStairSpawnCurriculum)
            {
                metrics?.SetSpawnCurriculum(wfcAdapter.CurrentTrainingStage,
                    wfcAdapter.LastSpawnStairSteps,
                    episodeTargetCoverage,
                    episodeRequiresEveryFloorCoverage,
                    wfcAdapter.StairGuideEnabled);
            }
            return true;
        }

        public bool IsTargetVisible(ExplorationAgent agent)
        {
            if (target == null || agent == null)
            {
                return false;
            }

            if (phase != AITrainingPhase.MovementControl && requireCoverageBeforeGoal)
            {
                return false;
            }

            if (wfcAdapter != null && wfcAdapter.UsesStairSpawnCurriculum)
            {
                return false;
            }

            if (phase == AITrainingPhase.MovementControl && phaseOneTargetAlwaysVisible)
            {
                return true;
            }

            Vector3 origin = agent.transform.position + Vector3.up * eyeHeight;
            Vector3 destination = target.transform.position + Vector3.up * 0.25f;
            if (Vector3.Distance(origin, destination) > targetVisibilityDistance)
            {
                return false;
            }

            Vector3 offset = destination - origin;
            float distance = offset.magnitude;
            if (distance <= 0.001f)
            {
                return true;
            }

            RaycastHit[] hits = Physics.RaycastAll(
                origin,
                offset / distance,
                distance,
                visibilityObstacleMask,
                QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                Transform hitTransform = hit.collider.transform;
                if (hitTransform.IsChildOf(agent.transform)
                    || hitTransform.IsChildOf(target.transform))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        public bool TryGetStairGuideObservation(ExplorationAgent agent,
            float distanceNormalization, out Vector2 direction,
            out float normalizedDistance)
        {
            direction = Vector2.zero;
            normalizedDistance = 0f;
            if (agent == null || reachedOtherFloor || wfcAdapter == null
                || !wfcAdapter.TryGetStairGuideWaypoint(agent.transform.position,
                    visitedStairs.Count > 0, out Vector3 waypoint))
            {
                return false;
            }

            Vector3 relative = agent.transform.InverseTransformPoint(waypoint);
            Vector2 horizontal = new Vector2(relative.x, relative.z);
            direction = horizontal.sqrMagnitude > 0.0001f
                ? horizontal.normalized : Vector2.zero;
            normalizedDistance = Mathf.Clamp01(horizontal.magnitude
                / Mathf.Max(1f, distanceNormalization));
            return true;
        }

        public void VisitArea(ExplorationAgent agent, ExplorationArea area)
        {
            if (agent == null || area == null)
            {
                return;
            }

            if (wfcAdapter != null && area.transform.parent != null)
            {
                metrics?.RegisterFloorAreaEntered(
                    area.FloorIndex,
                    area.transform.parent.position.y,
                    agent.transform.position.y);
            }

            areaVisitCounts.TryGetValue(area, out int visitCount);
            visitCount++;
            areaVisitCounts[area] = visitCount;
            currentAreaVisitCount = visitCount;

            bool isNewArea = visitedAreas.Add(area);
            bool confirmedLoop = UsesEpisodicMemory && progressGuard.Visit(area, isNewArea);
            RecordRecentVisit(!isNewArea);
            if (isNewArea)
            {
                timeSinceLastNewArea = 0f;
                float reward;
                if (UsesBalancedPhase3Reward)
                {
                    float lower = CalculateFloorCoverage(0), upper = CalculateFloorCoverage(1);
                    float total = Phase3RewardRevision == 2
                        ? AIPhase3CoverageReward.SplitExplorationTotal(lower, upper, episodeTargetCoverage)
                        : AIPhase3CoverageReward.ExplorationTotal(lower, upper, episodeTargetCoverage);
                    reward = AIPhase3CoverageReward.Increment(total, awardedAreaReward);
                    awardedAreaReward += reward;
                    // No upper-floor-only bonus. Remaining progress is paid by
                    // the minimum-floor reward in RegisterFloorProgress below.
                }
                else
                {
                    float areaRewardLimit = wfcAdapter != null
                        && wfcAdapter.IsFloorTransferLesson
                            ? Mathf.Min(maximumAreaRewardPerEpisode, 0.25f)
                            : maximumAreaRewardPerEpisode;
                    if (wfcAdapter != null && wfcAdapter.StairPracticeEnabled
                        && wfcAdapter.CurrentTrainingStage == 1) areaRewardLimit = 0f;
                    float remainingReward = Mathf.Max(0f, areaRewardLimit - awardedAreaReward);
                    reward = Mathf.Min(newAreaReward, remainingReward);
                    awardedAreaReward += reward;
                    if (UsesEpisodicMemory && area.FloorIndex == 1)
                    {
                        float upperFloorBonusLimit = wfcAdapter != null
                            && wfcAdapter.IsFloorTransferLesson
                                ? Mathf.Min(maximumUpperFloorAreaBonusPerEpisode, 0.1f)
                                : maximumUpperFloorAreaBonusPerEpisode;
                        if (wfcAdapter != null && wfcAdapter.StairPracticeEnabled
                            && wfcAdapter.CurrentTrainingStage == 1) upperFloorBonusLimit = 0f;
                        float bonus = Mathf.Min(upperFloorNewAreaRewardBonus,
                            Mathf.Max(0f, upperFloorBonusLimit - awardedUpperFloorAreaBonus));
                        awardedUpperFloorAreaBonus += bonus;
                        reward += bonus;
                    }
                }
                if (reward > 0f)
                {
                    agent.AddReward(reward);
                }

                metrics?.RegisterVisitedArea();
                RegisterFloorProgress(agent, area.FloorIndex);

                if (UsesEpisodicMemory
                    && requireCoverageBeforeGoal
                    && (wfcAdapter == null || !wfcAdapter.IsFloorTransferLesson)
                    && HasCompletedCoverageRequirement(agent))
                {
                    FinishSuccessfulEpisode(agent, coverageCompletionReward);
                }
            }
            else if (UsesEpisodicMemory)
            {
                metrics?.RegisterRevisit();
                progressGuard.BeginBacktracking(area);
                if (confirmedLoop)
                {
                    agent.AddReward(shortLoopPenalty);
                    metrics?.RegisterShortLoop();
                }
            }

        }

        public void VisitStair(ExplorationAgent agent, StairTraversalArea stair)
        {
            if (agent == null || stair == null || !visitedStairs.Add(stair))
            {
                return;
            }

            agent.AddReward(firstStairTraversalReward);
            metrics?.RegisterStairTraversal(agent.StepCount);
            progressGuard.RegisterMilestone(stair);
            timeSinceLastNewArea = 0f;

            if (UsesEpisodicMemory
                && requireCoverageBeforeGoal
                && (wfcAdapter == null || !wfcAdapter.IsFloorTransferLesson)
                && HasCompletedCoverageRequirement(agent))
            {
                FinishSuccessfulEpisode(agent, coverageCompletionReward);
            }
        }

        public void VisitStairMidpoint(ExplorationAgent agent, StairTraversalArea stair)
        {
            if (agent == null || stair == null || !visitedStairMidpoints.Add(stair))
            {
                return;
            }

            agent.AddReward(firstStairMidpointReward);
            metrics?.RegisterStairMidpoint(agent.StepCount);
            MarkExplorationProgress();
        }

        public void UpdateStairPerception(ExplorationAgent agent)
        {
            if (agent == null
                || stairSeenThisEpisode
                || !UsesEpisodicMemory
                || wfcAdapter == null
                || !wfcAdapter.UsesStairSpawnCurriculum
                || wfcAdapter.UsesSingleFloorExplorationLesson)
            {
                return;
            }

            Vector3 origin = agent.transform.position + Vector3.up;
            int rayLayerMask = ~(1 << 2); // Match the sensor: exclude Ignore Raycast.
            for (int index = -StairRaysPerDirection;
                 index <= StairRaysPerDirection;
                 index++)
            {
                float angle = StairRayDegrees * index / StairRaysPerDirection;
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up)
                    * agent.transform.forward;
                if (!Physics.SphereCast(
                        origin,
                        StairSphereCastRadius,
                        direction,
                        out RaycastHit hit,
                        StairRayLength,
                        rayLayerMask,
                        QueryTriggerInteraction.Ignore)
                    || !hit.collider.CompareTag("Stair"))
                {
                    continue;
                }

                stairSeenThisEpisode = true;
                agent.AddReward(firstStairSeenReward);
                metrics?.RegisterStairSeen(agent.StepCount);
                return;
            }
        }

        // CollectObservations is called once per decision before ML-Agents
        // captures its reward. Physics ticks between decisions must not apply gamma.
        public void ObserveStairSearchDecision(ExplorationAgent agent)
        {
            if (!searchShapingActive || episodeFinished || agent == null || !agent.isActiveAndEnabled) return;
            if (visitedStairs.Count > 0 && !wfcAdapter.StairPracticeEnabled) lastSearchDistance = 0f;
            else if (wfcAdapter.TryGetStairSearchDistance(agent.transform.position, out float distance))
                lastSearchDistance = distance;
            float reward = searchPotential.Observe(lastSearchDistance);
            agent.AddReward(reward);
            metrics?.RegisterSearchShaping(reward, lastSearchDistance);
        }

        private void FinishStairSearchShaping(ExplorationAgent agent)
        {
            if (!searchShapingActive) return;
            searchShapingActive = false;
            float reward = searchPotential.Finish();
            agent.AddReward(reward);
            metrics?.RegisterSearchShaping(reward, lastSearchDistance);
        }

        public bool TryFinishShapedTransferAtStepLimit(ExplorationAgent agent)
        {
            if (!searchShapingActive || agent.MaxStep <= 0 || agent.StepCount < agent.MaxStep) return false;
            // A shaped transfer has a finite-horizon terminal state. End it here
            // so the zero-terminal-potential correction is sent before reward capture.
            metrics?.RegisterStepLimit();
            FailEpisode(agent, 0f);
            return true;
        }

        private void MarkExplorationProgress()
        {
            progressGuard.MarkProgress();
            timeSinceLastNewArea = 0f;
        }

        private void ObserveNavigationProgress(ExplorationAgent agent)
        {
            int floor = metrics != null ? metrics.ConfirmedFloorIndex : -1;
            if (floor >= 0 && lastProgressFloor >= 0 && floor != lastProgressFloor)
            {
                // Once per direction per episode, not once per trigger entry.
                if (progressGuard.RegisterMilestone($"floor:{lastProgressFloor}:{floor}"))
                    timeSinceLastNewArea = 0f;
            }
            if (floor >= 0) lastProgressFloor = floor;
            if (wfcAdapter != null && wfcAdapter.IsFloorTransferLesson
                && wfcAdapter.TryGetStairSearchDistance(agent.transform.position, out float distance)
                && distance <= bestTransferSearchDistance - 0.25f)
            {
                // Only a new best route distance counts, so retreat/approach
                // cycles cannot repeatedly reset the progress timeout.
                bestTransferSearchDistance = distance;
                MarkExplorationProgress();
            }
        }

        public bool TickExploration(ExplorationAgent agent, float deltaTime)
        {
            if (!UsesEpisodicMemory || agent == null)
            {
                return false;
            }

            ObserveNavigationProgress(agent);

            if (DefectExperiment != null)
            {
                // Retain the trained progress observations; trial duration owns termination.
                progressGuard.Tick(deltaTime, episodeNoNewAreaTimeout);
                timeSinceLastNewArea = progressGuard.Elapsed;
                return false;
            }

            if (wfcAdapter != null && agent.Motor.IsGrounded
                && wfcAdapter.TryGetStairApproachFraction(agent.transform.position, out float approachFraction))
            {
                float approachBonus = stairPracticeProgress.ObserveApproach(approachFraction, true);
                if (approachBonus > 0f) agent.AddReward(approachBonus);
                metrics?.SetStairApproachReward(stairPracticeProgress.AwardedApproach);
            }

            if (wfcAdapter != null && agent.Motor.IsGrounded
                && visitedStairs.Count > 0
                && wfcAdapter.TryGetStairPracticeFraction(agent.transform.position, out float fraction))
            {
                float bonus = stairPracticeProgress.Observe(fraction, true);
                if (bonus > 0f) agent.AddReward(bonus);
                metrics?.SetStairClimbProgress(stairPracticeProgress.BestFraction, stairPracticeProgress.AwardedReward);
            }

            if (wfcAdapter != null && wfcAdapter.IsFloorTransferLesson
                && visitedStairs.Count > 0
                && transferDestinationFloorIndex >= 0
                && metrics != null
                && metrics.ConfirmedFloorIndex == transferDestinationFloorIndex)
            {
                float ascentBonus = wfcAdapter.UsesGraduatedTransferLessons
                    && transferDestinationFloorIndex == 1
                        ? graduatedTransferAscentBonus : 0f;
                FinishSuccessfulEpisode(agent,
                    coverageCompletionReward + ascentBonus);
                return true;
            }

            // Phase 3A has only one area trigger per floor. Recheck after movement
            // so entering the destination floor does not end the episode instantly.
            if (minimumTravelOnOtherFloorToComplete > 0f
                && requireCoverageBeforeGoal
                && (wfcAdapter == null || !wfcAdapter.IsFloorTransferLesson)
                && HasCompletedCoverageRequirement(agent))
            {
                FinishSuccessfulEpisode(agent, coverageCompletionReward);
                return true;
            }

            bool timedOut = progressGuard.Tick(deltaTime, episodeNoNewAreaTimeout);
            timeSinceLastNewArea = progressGuard.Elapsed;
            if (!timedOut)
            {
                return false;
            }

            metrics?.RegisterNoNewAreaTimeout();
            FailEpisode(agent, IsTwoFloorCoverageLesson
                ? twoFloorNoNewAreaPenalty
                : noNewAreaPenalty);
            return true;
        }

        public void RegisterCollision()
        {
            metrics?.RegisterCollision();
        }

        public void RegisterNoDisplacementFailure()
        {
            metrics?.RegisterNoDisplacementFailure();
        }

        public void RegisterMotionWarning() => metrics?.RegisterMotionWarning();
        public void RegisterStationaryFailure() => metrics?.RegisterStationaryFailure();

        public void TrackAgent(Vector3 position)
        {
            metrics?.TrackPosition(position);
        }

        public void RecordFloorTiming(ExplorationAgent agent)
        {
            if (metrics == null || agent == null) return;
            int floor = -1;
            if (wfcAdapter != null && agent.Motor.IsGrounded
                && agent.Motor.TryGetGroundSlope(out float slope) && slope <= 10f)
                floor = wfcAdapter.DiagnosticFlatFloorAt(agent.transform.position);
            bool snapshot = metrics.NeedsFloorTimingSnapshot(floor);
            metrics.RecordFloorTiming(agent.StepCount, floor,
                snapshot ? CalculateFloorCoverage(0) : 0f,
                snapshot ? CalculateFloorCoverage(1) : 0f);
        }

        public void CompleteEpisode(ExplorationAgent agent)
        {
            if (agent == null || episodeFinished)
            {
                return;
            }

            if (UsesEpisodicMemory && requireCoverageBeforeGoal)
            {
                if (wfcAdapter != null && wfcAdapter.IsFloorTransferLesson)
                {
                    return;
                }
                if (!targetReachedThisEpisode)
                {
                    targetReachedThisEpisode = true;
                    agent.AddReward(earlyGoalReward);
                    metrics?.RegisterTargetReached();
                }

                if (!HasCompletedCoverageRequirement(agent))
                {
                    return;
                }
            }

            FinishSuccessfulEpisode(agent, 1f);
        }

        public void FailEpisode(ExplorationAgent agent, float penalty)
        {
            if (agent == null || episodeFinished)
            {
                return;
            }

            episodeFinished = true;
            FinishStairSearchShaping(agent);
            agent.AddReward(Mathf.Clamp(penalty, -1f, 0f));
            RecordFloorTiming(agent);
            RecordBacktrackingMetrics();
            metrics?.FinishEpisode(
                false,
                agent.StepCount,
                CalculateCoverage(),
                CalculateFloorCoverage(0),
                CalculateFloorCoverage(1));
            agent.MarkEpisodeFinished();
            agent.EndEpisode();
        }

        public void RegisterTimedOutEpisode(ExplorationAgent agent)
        {
            if (agent == null)
            {
                return;
            }

            int timeoutSteps = agent.MaxStep > 0 ? agent.MaxStep : agent.StepCount;
            metrics?.RegisterStepLimit();
            RecordBacktrackingMetrics();
            metrics?.FinishEpisode(
                false,
                timeoutSteps,
                CalculateCoverage(),
                CalculateFloorCoverage(0),
                CalculateFloorCoverage(1));
        }

        private bool TryChooseEpisodePositions(
            float minimumDistance,
            float maximumDistance,
            out Vector3 agentPosition,
            out Vector3 targetPosition)
        {
            if (DefectExperiment != null)
                return DefectExperiment.PrepareEpisode(out agentPosition, out targetPosition);
            if (phase != AITrainingPhase.MovementControl && wfcAdapter != null)
            {
                if (!wfcAdapter.PrepareEpisode(episodeIndex))
                {
                    agentPosition = default;
                    targetPosition = default;
                    return false;
                }

                return wfcAdapter.TryChooseSpawnPair(
                    random,
                    minimumDistance,
                    maximumDistance,
                    out agentPosition,
                    out targetPosition);
            }

            var agentPoints = new List<AITrainingSpawnPoint>();
            var targetPoints = new List<AITrainingSpawnPoint>();
            AITrainingSpawnPoint lowerBidirectionalPoint = null;
            AITrainingSpawnPoint upperBidirectionalPoint = null;
            foreach (AITrainingSpawnPoint point in spawnPoints)
            {
                if (point == null || !point.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (point.AllowsAgent) agentPoints.Add(point);
                if (point.AllowsTarget) targetPoints.Add(point);
                if (point.Usage == AISpawnPointUsage.Both)
                {
                    if (lowerBidirectionalPoint == null
                        || point.transform.position.y
                            < lowerBidirectionalPoint.transform.position.y)
                    {
                        lowerBidirectionalPoint = point;
                    }

                    if (upperBidirectionalPoint == null
                        || point.transform.position.y
                            > upperBidirectionalPoint.transform.position.y)
                    {
                        upperBidirectionalPoint = point;
                    }
                }
            }

            if (alternateFixedSpawnDirection
                && lowerBidirectionalPoint != null
                && upperBidirectionalPoint != null
                && lowerBidirectionalPoint != upperBidirectionalPoint)
            {
                bool descendingEpisode = (episodeIndex & 1) == 1;
                Vector3 first = descendingEpisode
                    ? upperBidirectionalPoint.transform.position
                    : lowerBidirectionalPoint.transform.position;
                Vector3 second = descendingEpisode
                    ? lowerBidirectionalPoint.transform.position
                    : upperBidirectionalPoint.transform.position;
                float distance = Vector3.Distance(first, second);
                if (distance >= minimumDistance
                    && (maximumDistance <= 0f || distance <= maximumDistance))
                {
                    agentPosition = first;
                    targetPosition = second;
                    return true;
                }
            }

            for (int attempt = 0; attempt < 128; attempt++)
            {
                if (agentPoints.Count == 0 || targetPoints.Count == 0)
                {
                    break;
                }

                Vector3 first = agentPoints[random.Next(agentPoints.Count)].transform.position;
                Vector3 second = targetPoints[random.Next(targetPoints.Count)].transform.position;
                float distance = Vector3.Distance(first, second);
                if (distance < minimumDistance
                    || (maximumDistance > 0f && distance > maximumDistance))
                {
                    continue;
                }

                agentPosition = first;
                targetPosition = second;
                return true;
            }

            agentPosition = default;
            targetPosition = default;
            return false;
        }

        private float CalculateCoverage()
        {
            if (explorationAreas == null || explorationAreas.Length == 0)
            {
                return 0f;
            }

            int total = 0;
            int visited = 0;
            foreach (ExplorationArea area in explorationAreas)
            {
                if (area == null || !area.gameObject.activeInHierarchy)
                {
                    continue;
                }

                total++;
                if (visitedAreas.Contains(area))
                {
                    visited++;
                }
            }

            return total == 0 ? 0f : visited / (float)total;
        }

        private float CalculateFloorCoverage(int floorIndex)
        {
            if (explorationAreas == null || explorationAreas.Length == 0)
            {
                return 0f;
            }

            int total = 0;
            int visited = 0;
            foreach (ExplorationArea area in explorationAreas)
            {
                if (area == null
                    || !area.gameObject.activeInHierarchy
                    || area.FloorIndex != floorIndex)
                {
                    continue;
                }

                total++;
                if (visitedAreas.Contains(area))
                {
                    visited++;
                }
            }

            return total == 0 ? 0f : visited / (float)total;
        }

        private void RegisterFloorProgress(ExplorationAgent agent, int floorIndex)
        {
            visitedFloors.Add(floorIndex);
            if (!reachedOtherFloor && visitedFloors.Count >= 2)
            {
                reachedOtherFloor = true;
                otherFloorEntryPosition = agent.transform.position;
                otherFloorEntryRecorded = true;
                agent.AddReward(firstOtherFloorReward);
                metrics?.RegisterOtherFloorReached(agent.StepCount);
            }

            // Transfer lesson rewards reaching the opposite floor, not balancing
            // coverage before the agent has learned the staircase route.
            if (wfcAdapter != null && wfcAdapter.IsFloorTransferLesson)
            {
                return;
            }

            float minimumFloorCoverage = episodeRequiresEveryFloorCoverage
                ? Mathf.Min(CalculateFloorCoverage(0), CalculateFloorCoverage(1))
                : CalculateCoverage();
            if (minimumFloorCoverage <= bestMinimumFloorCoverage)
            {
                return;
            }

            float progress = minimumFloorCoverage - bestMinimumFloorCoverage;
            bestMinimumFloorCoverage = minimumFloorCoverage;
            float remainingReward = Mathf.Max(
                0f,
                maximumBalancedCoverageReward - awardedBalancedCoverageReward);
            float reward = Mathf.Min(
                progress * balancedCoverageRewardScale,
                remainingReward);
            if (UsesBalancedPhase3Reward)
            {
                reward = AIPhase3CoverageReward.Increment(
                    AIPhase3CoverageReward.BalanceTotal(CalculateFloorCoverage(0),
                        CalculateFloorCoverage(1), episodeTargetCoverage, episodeMaximumFloorCoverageDifference), awardedBalancedCoverageReward);
            }
            if (reward <= 0f)
            {
                return;
            }

            agent.AddReward(reward);
            awardedBalancedCoverageReward += reward;
        }

        private float GetProgressiveFloorCoverageDifference(float floorCoverageTarget)
        {
            if (floorCoverageTarget <= 0.0201f)
            {
                return 1f;
            }

            if (floorCoverageTarget <= 0.0501f)
            {
                return 0.3f;
            }

            if (floorCoverageTarget <= 0.1001f)
            {
                return 0.2f;
            }

            if (floorCoverageTarget <= 0.1401f)
            {
                return 0.15f;
            }

            return maximumFloorCoverageDifference;
        }

        private bool HasCompletedCoverageRequirement(ExplorationAgent agent)
        {
            if (CalculateCoverage() < episodeTargetCoverage)
            {
                return false;
            }

            if (!episodeRequiresEveryFloorCoverage)
            {
                return (!episodeRequiresStairTraversal || visitedStairs.Count > 0)
                    && HasAdvancedIntoOtherFloor(agent);
            }

            float floorZeroCoverage = CalculateFloorCoverage(0);
            float floorOneCoverage = CalculateFloorCoverage(1);
            bool terrainCoverage = wfcAdapter != null && wfcAdapter.TerrainCurriculumEnabled;
            if (terrainCoverage
                ? !AITerrainCurriculum.BalancedCoverage(episodeTargetCoverage, floorZeroCoverage,
                    floorOneCoverage, episodeMaximumFloorCoverageDifference)
                : floorZeroCoverage < episodeTargetCoverage
                || floorOneCoverage < episodeTargetCoverage
                || Mathf.Abs(floorZeroCoverage - floorOneCoverage)
                    > episodeMaximumFloorCoverageDifference)
            {
                return false;
            }

            return (!episodeRequiresStairTraversal || visitedStairs.Count > 0)
                && HasAdvancedIntoOtherFloor(agent);
        }

        private bool HasAdvancedIntoOtherFloor(ExplorationAgent agent)
        {
            if (minimumTravelOnOtherFloorToComplete <= 0f)
            {
                return true;
            }

            if (agent == null || !otherFloorEntryRecorded
                || Mathf.Abs(agent.transform.position.y - otherFloorEntryPosition.y) > 1f
                || target == null)
            {
                return false;
            }

            Vector3 towardCorridorEnd = target.transform.position - otherFloorEntryPosition;
            towardCorridorEnd.y = 0f;
            if (towardCorridorEnd.sqrMagnitude < 0.001f)
            {
                return false;
            }

            Vector3 travel = agent.transform.position - otherFloorEntryPosition;
            travel.y = 0f;
            return Vector3.Dot(travel, towardCorridorEnd.normalized)
                >= minimumTravelOnOtherFloorToComplete;
        }

        private void FinishSuccessfulEpisode(ExplorationAgent agent, float reward)
        {
            if (DefectExperiment != null) return;
            if (agent == null || episodeFinished)
            {
                return;
            }

            episodeFinished = true;
            FinishStairSearchShaping(agent);
            agent.AddReward(Mathf.Max(0f, reward));
            RecordFloorTiming(agent);
            RecordBacktrackingMetrics();
            metrics?.FinishEpisode(
                true,
                agent.StepCount,
                CalculateCoverage(),
                CalculateFloorCoverage(0),
                CalculateFloorCoverage(1));
            agent.MarkEpisodeFinished();
            agent.EndEpisode();
        }

        private void RecordBacktrackingMetrics()
        {
            Academy.Instance.StatsRecorder.Add("AI/Exploration Reward Awarded", awardedAreaReward);
            Academy.Instance.StatsRecorder.Add("AI/Upper Floor Bonus Awarded", awardedUpperFloorAreaBonus);
            Academy.Instance.StatsRecorder.Add("AI/Minimum Coverage Reward Awarded", awardedBalancedCoverageReward);
            metrics?.SetBacktrackingGrace(progressGuard.GraceGranted, progressGuard.GraceUsed);
        }

        private void RecordRecentVisit(bool isRevisit)
        {
            recentRevisits.Enqueue(isRevisit);
            if (isRevisit)
            {
                recentRevisitCount++;
            }

            while (recentRevisits.Count > recentAreaMemorySize)
            {
                if (recentRevisits.Dequeue())
                {
                    recentRevisitCount--;
                }
            }
        }

        private void OnValidate()
        {
            minimumSpawnDistance = Mathf.Max(0f, minimumSpawnDistance);
            maximumSpawnDistance = Mathf.Max(0f, maximumSpawnDistance);
            targetHeightOffset = Mathf.Max(0f, targetHeightOffset);
            targetVisibilityDistance = Mathf.Max(0.1f, targetVisibilityDistance);
            eyeHeight = Mathf.Max(0f, eyeHeight);
            maximumAreaRewardPerEpisode = Mathf.Max(
                newAreaReward,
                maximumAreaRewardPerEpisode);
            targetCoverage = Mathf.Clamp(targetCoverage, 0.01f, 1f);
            maximumFloorCoverageDifference = Mathf.Clamp01(
                maximumFloorCoverageDifference);
            minimumTravelOnOtherFloorToComplete = Mathf.Max(
                0f, minimumTravelOnOtherFloorToComplete);
            balancedCoverageRewardScale = Mathf.Max(0f, balancedCoverageRewardScale);
            maximumBalancedCoverageReward = Mathf.Clamp01(
                maximumBalancedCoverageReward);
            recentAreaMemorySize = Mathf.Max(2, recentAreaMemorySize);
            noNewAreaTimeout = Mathf.Max(1f, noNewAreaTimeout);
            twoFloorNoNewAreaTimeout = Mathf.Max(1f, twoFloorNoNewAreaTimeout);
        }
    }
}
