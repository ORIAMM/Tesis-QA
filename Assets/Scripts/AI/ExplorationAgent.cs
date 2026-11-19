using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TesisQA.AI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(AICharacterMotor))]
    public sealed class ExplorationAgent : Agent
    {
        public const int BaseVectorObservationSize = 10;

        [Header("Environment")]
        [SerializeField] private AITrainingEnvironment environment;
        [SerializeField] private AICharacterMotor motor;
        [SerializeField] private EpisodicVisitMemorySensorComponent visitMemorySensor;

        [Header("Reward")]
        [SerializeField, Range(-0.01f, 0f)] private float stepPenalty = -0.0005f;
        [SerializeField, Range(-1f, 0f)] private float wallCollisionPenalty = -0.02f;
        [SerializeField, Min(0f)] private float collisionRewardCooldown = 0.25f;

        [Header("Movement Learning")]
        [Tooltip("Reward per unit jarak yang berkurang. Hanya aktif pada Fase 1.")]
        [SerializeField, Min(0f)] private float progressRewardScale = 0.02f;
        [SerializeField, Min(0f)] private float maximumProgressRewardPerStep = 0.005f;

        [Header("Failure")]
        [SerializeField, Min(1)] private int twoFloorMaxStep = 6000;
        [SerializeField, Min(0.1f)] private float stuckTimeout = 3f;
        [SerializeField, Min(0f)] private float stuckVelocityThreshold = 0.08f;
        [SerializeField, Range(-1f, 0f)] private float failurePenalty = -0.5f;

        [Header("Anti Spin")]
        [SerializeField, Min(0.5f)] private float requiredDisplacement = 4f;
        [SerializeField, Min(1f)] private float noDisplacementTimeout = 12f;
        [SerializeField, Min(1f)] private float twoFloorNoDisplacementTimeout = 18f;
        [SerializeField, Range(-1f, 0f)] private float noDisplacementPenalty = -0.1f;

        [Header("Observation")]
        [Tooltip("Enable for the NEW terrain-sensor training series. Disable BEFORE Play for old Phase*M models; never toggle during an episode.")]
        [SerializeField] private bool useTerrainProfileSensor;
        [Tooltip("Far terrain probes at 6m instead of 3m, with +/-6m vertical search. Keeps 24 terrain inputs; fine-tune before evaluating an old model. Phase 1 remains legacy range.")]
        [SerializeField] private bool useLongRangeTerrainProfile;
        [Tooltip("Append 12 distant (6m) terrain observations; retain the original 1.5/3m inputs. Requires a migrated 161-input model with branch memory ON; disable the archived Long Range replacement.")]
        [SerializeField] private bool useDistantTerrainSensor;
        [Tooltip("16 episodic ramp-location observations. Requires migrated 177-input checkpoint; no tag-based guide or action override.")]
        [SerializeField] private bool useRampMemorySensor;
        [HideInInspector] // Historical serialization only; action assistance is no longer supported.
        [SerializeField] private bool useCompletedBranchAssist;
        [Tooltip("32 episodic branch observations. PPO alone chooses actions. Disable BEFORE Play for legacy 117-input models.")]
        [SerializeField] private bool useBranchMemorySensor;
        [SerializeField, Min(1f)] private float targetDistanceNormalization = 50f;

        private float nextCollisionRewardTime;
        private float stuckTimer;
        private float previousTargetDistance;
        private readonly AINavigationMotionGuard motionGuard = new AINavigationMotionGuard();
        private bool episodeRunning;
        private int baseMaxStep;
        private BranchMemorySensorComponent branchMemorySensor;
        private RampMemorySensorComponent rampMemorySensor;
        private ForwardTerrainSensorComponent forwardTerrainSensor;

        public AICharacterMotor Motor => motor;
        public string ExperimentObservationSettings => $"terrain={useTerrainProfileSensor};branch={useBranchMemorySensor};distant={useDistantTerrainSensor};ramp={useRampMemorySensor};longRangeReplacement={useLongRangeTerrainProfile};completedBranchAssist={useCompletedBranchAssist}";

        public override void Initialize()
        {
            baseMaxStep = MaxStep;
            if (motor == null)
            {
                motor = GetComponent<AICharacterMotor>();
            }

            if (environment == null)
            {
                environment = GetComponentInParent<AITrainingEnvironment>();
            }

            // InitializeSensors runs after Initialize, so existing scenes acquire
            // the same extra sensor as newly rebuilt scenes before the spec is sent.
            if (visitMemorySensor == null)
            {
                visitMemorySensor = GetComponent<EpisodicVisitMemorySensorComponent>();
            }
            if (visitMemorySensor == null)
            {
                visitMemorySensor = gameObject.AddComponent<EpisodicVisitMemorySensorComponent>();
            }

            ForwardTerrainSensorComponent terrainSensor = GetComponent<ForwardTerrainSensorComponent>();
            if (useTerrainProfileSensor && terrainSensor == null)
                terrainSensor = gameObject.AddComponent<ForwardTerrainSensorComponent>();
            if (terrainSensor != null)
            {
                terrainSensor.ObservationEnabled = useTerrainProfileSensor;
                terrainSensor.ExtendedRangeEnabled = useLongRangeTerrainProfile;
                terrainSensor.DistantObservationEnabled = useDistantTerrainSensor;
            }

            branchMemorySensor = GetComponent<BranchMemorySensorComponent>();
            if (useBranchMemorySensor && branchMemorySensor == null)
                branchMemorySensor = gameObject.AddComponent<BranchMemorySensorComponent>();
            if (branchMemorySensor != null) branchMemorySensor.ObservationEnabled = useBranchMemorySensor;

            rampMemorySensor = GetComponent<RampMemorySensorComponent>();
            if (useRampMemorySensor && rampMemorySensor == null)
                rampMemorySensor = gameObject.AddComponent<RampMemorySensorComponent>();
            if (rampMemorySensor != null) rampMemorySensor.ObservationEnabled = useRampMemorySensor;
            forwardTerrainSensor = terrainSensor;
            if (terrainSensor != null && rampMemorySensor != null)
            {
                terrainSensor.VisibleSurfaceSampled -= rampMemorySensor.ObserveVisibleSurface;
                if (useRampMemorySensor) terrainSensor.VisibleSurfaceSampled += rampMemorySensor.ObserveVisibleSurface;
            }

            motor.ControllerHit += OnMotorHit;
        }

        private void OnDestroy()
        {
            if (forwardTerrainSensor != null && rampMemorySensor != null)
                forwardTerrainSensor.VisibleSurfaceSampled -= rampMemorySensor.ObserveVisibleSurface;
            if (motor != null)
            {
                motor.ControllerHit -= OnMotorHit;
            }
        }

        public override void OnEpisodeBegin()
        {
            if (environment != null && environment.DefectExperiment != null)
                episodeRunning = false;
            float expectedRampMemory = Academy.Instance.EnvironmentParameters.GetWithDefault("ramp_memory_observation", -1f);
            if (useRampMemorySensor && (!useTerrainProfileSensor || !useDistantTerrainSensor
                    || !useBranchMemorySensor || useLongRangeTerrainProfile)
                || expectedRampMemory >= 0f && useRampMemorySensor != (expectedRampMemory >= 0.5f))
            {
                Debug.LogError("Ramp memory membutuhkan model migrasi 177 input, Terrain/Branch/Distant ON, Long Range replacement OFF. Sesuaikan opsi Ramp Memory dengan YAML sebelum Play.", this);
                enabled = false; episodeRunning = false; return;
            }
            float expectedDistantSensor = Academy.Instance.EnvironmentParameters
                .GetWithDefault("terrain_distant_observation", -1f);
            if (useDistantTerrainSensor && (!useTerrainProfileSensor || !useBranchMemorySensor
                    || useLongRangeTerrainProfile)
                || expectedDistantSensor >= 0f && useDistantTerrainSensor != (expectedDistantSensor >= 0.5f))
            {
                Debug.LogError("Multi-range: Terrain dan Branch Memory harus ON, Distant Terrain sesuai YAML, Long Range replacement OFF. Input baru 161 memerlukan checkpoint migrasi, bukan resume model 149 langsung.", this);
                enabled = false;
                episodeRunning = false;
                return;
            }
            float expectedBranchAssist = Academy.Instance.EnvironmentParameters
                .GetWithDefault("completed_branch_assist", -1f);
            if (useCompletedBranchAssist || expectedBranchAssist >= 0.5f)
            {
                Debug.LogError("Run hybrid lama tidak dipakai lagi. Gunakan konfigurasi Branch Observation (completed_branch_assist=0), bukan melanjutkan Phase2B lama.", this);
                enabled = false;
                episodeRunning = false;
                return;
            }
            float expectedBranchSensor = Academy.Instance.EnvironmentParameters.GetWithDefault("branch_memory_observation", -1f);
            if (expectedBranchSensor >= 0f && useBranchMemorySensor != (expectedBranchSensor >= 0.5f))
            {
                Debug.LogError("Use Branch Memory Sensor tidak cocok dengan konfigurasi trainer. Stop Play dan sesuaikan sebelum memulai; model lama 117 input tidak kompatibel langsung dengan 149 input.", this);
                enabled = false;
                episodeRunning = false;
                return;
            }
            bool phase3Curriculum = Academy.Instance.EnvironmentParameters.GetWithDefault("phase3_curriculum", 0f) >= 0.5f;
            if (phase3Curriculum && environment != null
                && Academy.Instance.EnvironmentParameters.GetWithDefault("phase3_reward_revision", 0f)
                    != environment.Phase3RewardRevision)
            {
                Debug.LogError("Phase 3 reward profile berbeda dari YAML. Revision 1: Explorer_Phase3_Balanced.yaml; revision 2: Explorer_Phase3_Split.yaml. Gunakan run baru untuk perubahan reward, jangan resume run dengan revision berbeda.", this);
                enabled = false; episodeRunning = false; return;
            }
            if (Academy.Instance.EnvironmentParameters.GetWithDefault("terrain_curriculum", 0f) >= 0.5f
                && (!useTerrainProfileSensor || environment == null
                    || environment.Phase != (phase3Curriculum
                        ? AITrainingPhase.ComplexRouting : AITrainingPhase.MultiroomNavigation)))
            {
                Debug.LogError(phase3Curriculum
                    ? "Phase 3 membutuhkan scene AI_Phase3_ComplexRouting dan Terrain Profile ON."
                    : "Phase2T membutuhkan scene AI_Phase2_SmallWFC dan Use Terrain Profile Sensor ON sebelum Play.", this);
                enabled = false;
                return;
            }
            if (episodeRunning)
            {
                environment?.RegisterTimedOutEpisode(this);
            }

            if (environment == null || !environment.BeginEpisode(this))
            {
                Debug.LogError("ExplorationAgent gagal memulai episode.", this);
                enabled = false;
                episodeRunning = false;
                return;
            }

            MaxStep = environment.IsTwoFloorCoverageLesson
                ? Mathf.Max(baseMaxStep, twoFloorMaxStep)
                : baseMaxStep;
            if (environment.DefectExperiment != null) MaxStep = 0;
            visitMemorySensor.ResetEpisode(transform.position);
            if (useBranchMemorySensor) branchMemorySensor?.ResetEpisode();
            if (useRampMemorySensor) rampMemorySensor?.ResetEpisode();
            Academy.Instance.StatsRecorder.Add("AI/Ramp Memory Active", useRampMemorySensor ? 1f : 0f);
            Academy.Instance.StatsRecorder.Add("AI/Branch Assist Active", 0f);
            Academy.Instance.StatsRecorder.Add("AI/Terrain Far Distance",
                useTerrainProfileSensor ? (useLongRangeTerrainProfile
                    ? ForwardTerrainSensorComponent.ExtendedFarDistance
                    : ForwardTerrainSensorComponent.LegacyFarDistance) : 0f);
            Academy.Instance.StatsRecorder.Add("AI/Distant Terrain Active", useDistantTerrainSensor ? 1f : 0f);
            Academy.Instance.StatsRecorder.Add("AI/Terrain Distant Distance", useDistantTerrainSensor ? 6f : 0f);
            if (expectedBranchSensor >= 0f)
                Academy.Instance.StatsRecorder.Add("AI/Branch Observation Active", useBranchMemorySensor ? 1f : 0f);
            episodeRunning = true;
        }

        public override void CollectObservations(VectorSensor sensor)
        {
            environment?.ObserveStairSearchDecision(this);
            Vector3 localVelocity = transform.InverseTransformDirection(motor.Velocity);
            float speedScale = Mathf.Max(0.01f, motor.MoveSpeed);
            sensor.AddObservation(Mathf.Clamp(localVelocity.x / speedScale, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(localVelocity.z / speedScale, -1f, 1f));
            sensor.AddObservation(Mathf.Clamp(motor.VerticalVelocity / 25f, -1f, 1f));
            sensor.AddObservation(motor.IsGrounded);

            float slope = motor.TryGetGroundSlope(out float slopeAngle)
                ? Mathf.Clamp01(slopeAngle / 90f)
                : 0f;
            sensor.AddObservation(slope);

            Vector2 stairDirection = Vector2.zero;
            float stairDistance = 0f;
            bool stairGuideVisible = environment != null
                && environment.TryGetStairGuideObservation(this,
                    targetDistanceNormalization, out stairDirection,
                    out stairDistance);
            bool targetVisible = !stairGuideVisible && environment != null
                && environment.IsTargetVisible(this);
            sensor.AddObservation(stairGuideVisible || targetVisible);
            if (stairGuideVisible)
            {
                sensor.AddObservation(stairDirection);
                sensor.AddObservation(stairDistance);
            }
            else if (targetVisible && environment.TargetTransform != null)
            {
                Vector3 relative = transform.InverseTransformPoint(
                    environment.TargetTransform.position);
                Vector2 horizontal = new Vector2(relative.x, relative.z);
                Vector2 direction = horizontal.sqrMagnitude > 0.0001f
                    ? horizontal.normalized
                    : Vector2.zero;
                sensor.AddObservation(direction);
                sensor.AddObservation(Mathf.Clamp01(
                    horizontal.magnitude / targetDistanceNormalization));
            }
            else
            {
                // Keep the vector size at 10 so Phase 3A checkpoints remain
                // compatible. When neither target nor stair guidance is active,
                // reuse the three direction/distance slots for exploration state.
                sensor.AddObservation(new Vector2(
                    environment != null
                        ? environment.FloorTransferProgressObservation
                        : 0f,
                    environment != null
                        ? environment.TimeSinceNewAreaObservation
                        : 0f));
                sensor.AddObservation(environment != null
                    ? environment.RecentRevisitRatioObservation
                    : 0f);
            }

            int observationHorizon = environment != null && environment.DefectExperiment != null
                ? environment.DefectExperiment.protocol.policyStepHorizon : MaxStep;
            sensor.AddObservation(observationHorizon > 0
                ? Mathf.Clamp01(StepCount / (float)observationHorizon)
                : 0f);

        }

        public override void OnActionReceived(ActionBuffers actions)
        {
            if (environment.DefectExperiment != null && !environment.DefectExperiment.Running) return;
            if (environment.DefectExperiment != null && environment.DefectExperiment.participantPresentation
                && environment.DefectExperiment.ParticipantPaused) return;
            float forward = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
            float turn = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
            // No branch steering/filter/mask: execute exactly the PPO/manual action.
            if (environment.DefectExperiment != null && environment.DefectExperiment.participantPresentation)
                motor.MoveParticipant(environment.DefectExperiment.ReadParticipantMovement(),
                    environment.DefectExperiment.ConsumeParticipantJump(), Time.fixedDeltaTime);
            else motor.Move(forward, turn, Time.fixedDeltaTime);
            visitMemorySensor.RecordPosition(transform.position);
            if (useRampMemorySensor && rampMemorySensor != null)
            {
                rampMemorySensor.RecordMotion(Time.fixedDeltaTime);
                if (StepCount % 50 == 0)
                {
                    Academy.Instance.StatsRecorder.Add("AI/Ramps Remembered", rampMemorySensor.RememberedCount);
                    Academy.Instance.StatsRecorder.Add("AI/Ramps Touched", rampMemorySensor.TouchedCount);
                    Academy.Instance.StatsRecorder.Add("AI/Ramps Height Traversed", rampMemorySensor.TraversedCount);
                }
            }
            if (useBranchMemorySensor && branchMemorySensor != null)
            {
                branchMemorySensor.RecordPosition();
                if (StepCount % 50 == 0)
                {
                    Academy.Instance.StatsRecorder.Add("AI/Branch Memory Nodes", branchMemorySensor.ObservedNodes);
                    Academy.Instance.StatsRecorder.Add("AI/Completed Branch Exits", branchMemorySensor.CompletedExits);
                    Academy.Instance.StatsRecorder.Add("AI/Branch Unknown Ports", branchMemorySensor.UnknownPorts);
                    Academy.Instance.StatsRecorder.Add("AI/Branch Unknown Fraction", branchMemorySensor.UnknownFraction);
                }
            }

            AddReward(stepPenalty);
            AddMovementProgressReward();
            environment.TrackAgent(transform.position);
            environment.RecordFloorTiming(this);
            if (StepCount % 5 == 0)
            {
                environment.UpdateStairPerception(this);
            }
            // Observe legitimate progress/success before checking motion limits.
            if (environment.TickExploration(this, Time.fixedDeltaTime))
            {
                return;
            }

            if (environment.DefectExperiment != null)
            {
                environment.DefectExperiment.ObserveMovement();
                return;
            }

            if (TickAntiSpin(Time.fixedDeltaTime))
            {
                return;
            }

            float horizontalSpeed = new Vector2(
                motor.Velocity.x,
                motor.Velocity.z).magnitude;
            if (Mathf.Abs(forward) > 0.5f
                && horizontalSpeed < stuckVelocityThreshold)
            {
                stuckTimer += Time.fixedDeltaTime;
            }
            else
            {
                stuckTimer = 0f;
            }

            if (transform.position.y < environment.KillHeight
                || (!environment.UsesEpisodicMemory && stuckTimer >= stuckTimeout))
            {
                environment.FailEpisode(this, failurePenalty);
                return;
            }
            environment.TryFinishShapedTransferAtStepLimit(this);
        }

        public override void Heuristic(in ActionBuffers actionsOut)
        {
            ActionSegment<float> continuous = actionsOut.ContinuousActions;
            if (environment != null && environment.DefectExperiment != null
                && environment.DefectExperiment.participantPresentation)
            {
                continuous[0] = continuous[1] = 0f;
                return;
            }
            if (environment != null && environment.DefectExperiment != null
                && environment.DefectExperiment.mode == DefectExperimentMode.Random)
            {
                environment.DefectExperiment.GetRandomAction(out float randomForward, out float randomTurn);
                continuous[0] = randomForward;
                continuous[1] = randomTurn;
                return;
            }
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                continuous[0] = 0f;
                continuous[1] = 0f;
                return;
            }

            continuous[0] = (keyboard.wKey.isPressed ? 1f : 0f)
                - (keyboard.sKey.isPressed ? 1f : 0f);
            continuous[1] = (keyboard.dKey.isPressed ? 1f : 0f)
                - (keyboard.aKey.isPressed ? 1f : 0f);
#else
            continuous[0] = Input.GetAxisRaw("Vertical");
            continuous[1] = Input.GetAxisRaw("Horizontal");
#endif
        }

        public void ReachTarget(ExplorationTarget reachedTarget)
        {
            if (environment != null
                && environment.TargetTransform == reachedTarget.transform)
            {
                environment.CompleteEpisode(this);
            }
        }

        public void VisitArea(ExplorationArea area)
        {
            environment?.VisitArea(this, area);
        }

        public void VisitStair(StairTraversalArea stair)
        {
            environment?.VisitStair(this, stair);
        }

        public void VisitStairMidpoint(StairTraversalArea stair)
        {
            environment?.VisitStairMidpoint(this, stair);
        }

        public void ResetEpisodeState()
        {
            stuckTimer = 0f;
            nextCollisionRewardTime = 0f;
            previousTargetDistance = GetHorizontalTargetDistance();
            motionGuard.Reset(transform.position, environment != null
                ? environment.NavigationProgressVersion : 0);
        }

        public void MarkEpisodeFinished()
        {
            episodeRunning = false;
        }

        private void OnMotorHit(ControllerColliderHit hit)
        {
            bool sideCollision = Mathf.Abs(hit.normal.y) < 0.5f;
            if (!sideCollision || Time.time < nextCollisionRewardTime)
            {
                return;
            }

            AddReward(wallCollisionPenalty);
            environment?.RegisterCollision();
            nextCollisionRewardTime = Time.time + collisionRewardCooldown;
        }

        private void AddMovementProgressReward()
        {
            if (environment == null
                || environment.Phase != AITrainingPhase.MovementControl
                || environment.TargetTransform == null
                || progressRewardScale <= 0f)
            {
                return;
            }

            float currentDistance = GetHorizontalTargetDistance();
            float reward = (previousTargetDistance - currentDistance)
                * progressRewardScale;
            AddReward(Mathf.Clamp(
                reward,
                -maximumProgressRewardPerStep,
                maximumProgressRewardPerStep));
            previousTargetDistance = currentDistance;
        }

        private float GetHorizontalTargetDistance()
        {
            if (environment == null || environment.TargetTransform == null)
            {
                return 0f;
            }

            Vector3 offset = environment.TargetTransform.position - transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }

        private bool TickAntiSpin(float deltaTime)
        {
            if (environment == null || !environment.UsesEpisodicMemory)
            {
                return false;
            }

            // Floors also apply to existing serialized scenes with the old 5/8s
            // values, so rebuilding a scene is not necessary to activate the fix.
            float timeout = environment.IsTwoFloorCoverageLesson
                ? Mathf.Max(18f, Mathf.Max(noDisplacementTimeout, twoFloorNoDisplacementTimeout))
                : Mathf.Max(12f, noDisplacementTimeout);
            var signal = motionGuard.Tick(transform.position,
                environment.NavigationProgressVersion, deltaTime, requiredDisplacement, timeout);
            if (signal == AINavigationMotionGuard.Signal.None) return false;
            if (signal == AINavigationMotionGuard.Signal.StationaryWarning
                || signal == AINavigationMotionGuard.Signal.RangeWarning)
            {
                AddReward(-0.02f);
                environment.RegisterMotionWarning();
                return false;
            }
            if (signal == AINavigationMotionGuard.Signal.StationaryFailure)
                environment.RegisterStationaryFailure();
            environment.RegisterNoDisplacementFailure();
            environment.FailEpisode(this, noDisplacementPenalty);
            return true;
        }

        private void OnValidate()
        {
            twoFloorMaxStep = Mathf.Max(1, twoFloorMaxStep);
            stuckTimeout = Mathf.Max(0.1f, stuckTimeout);
            stuckVelocityThreshold = Mathf.Max(0f, stuckVelocityThreshold);
            collisionRewardCooldown = Mathf.Max(0f, collisionRewardCooldown);
            progressRewardScale = Mathf.Max(0f, progressRewardScale);
            maximumProgressRewardPerStep = Mathf.Max(0f, maximumProgressRewardPerStep);
            requiredDisplacement = Mathf.Max(0.5f, requiredDisplacement);
            noDisplacementTimeout = Mathf.Max(1f, noDisplacementTimeout);
            twoFloorNoDisplacementTimeout = Mathf.Max(1f, twoFloorNoDisplacementTimeout);
            targetDistanceNormalization = Mathf.Max(1f, targetDistanceNormalization);
        }
    }
}
