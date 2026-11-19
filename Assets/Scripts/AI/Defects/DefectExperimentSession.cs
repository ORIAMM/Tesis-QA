using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TesisQA.WFC;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TesisQA.AI
{
    [DefaultExecutionOrder(-20000)]
    public sealed partial class DefectExperimentSession : MonoBehaviour
    {
        public DefectExperimentProtocol protocol;
        public ExplorationAgent agent;
        public AITrainingEnvironment environment;
        public AIWFCTrainingAdapter adapter;
        public WFCGenerator generator;
        public Camera viewCamera;
        public DefectExperimentMode mode = DefectExperimentMode.Manual;
        public string participant = "P001";
        [Range(0, 9)] public int participantOrder;
        public bool autoStart, batchRun;
        [Tooltip("Participant game presentation; hides researcher controls and participant IDs.")]
        public bool participantPresentation;
        public string googleFormUrl = "https://forms.gle/D6iTTSbCAvPstjng9";
        public string outputDirectory;
        [Min(.01f)] public float automaticTimeScale = 1f;
        [Tooltip("Validation only. Zero uses the frozen protocol duration.")]
        public float smokeDurationOverride;
        [HideInInspector] public int[] calibrationScenarioIndices;
        public Quaternion SpawnRotation => Quaternion.Euler(0f, build?.manifest.spawnYaw ?? 0f, 0f);
        public bool Running { get; private set; }

        private DefectScenarioBuild build;
        private LocalCollisionDetector detector;
        private DefectTrialReport current;
        private readonly List<DefectTrialReport> completed = new List<DefectTrialReport>();
        private readonly HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
        private System.Random actionRandom;
        private int ordinal, repetition;
        private int calibrationCursor;
        private bool campaignStarted, needNewScenario = true, pendingAdvance, pendingRespawn, finished, writing, ownsOutput;
        private float nextScan, nextTrace, nextCheckpoint, lastSafeHeight, randomForward, randomTurn, nextRandomAction;
        private float previousScale, previousFixedDelta;
        private System.Diagnostics.Stopwatch trialClock;
        private float WallSeconds => trialClock == null ? 0f : (float)trialClock.Elapsed.TotalSeconds;
        private Vector3 previousPosition;
        private float cameraPitch = 30f;
        private string feedback = "", error = "", protocolHash;
        private double feedbackUntil;
        private bool previousCommunicatorEnabled;
        private string previewCapturePath;
        private bool previewQueued;

        private void Awake()
        {
            if (!Application.isEditor)
            {
                string[] args = Environment.GetCommandLineArgs();
                for (int i = 0; i + 1 < args.Length; i++)
                {
                    if (args[i] == "-defectMode" && Enum.TryParse(args[i + 1], out DefectExperimentMode selected)) mode = selected;
                    if (args[i] == "-defectOutput") outputDirectory = Path.GetFullPath(args[i + 1]);
                    if (args[i] == "-defectAutoStart") autoStart = bool.Parse(args[i + 1]);
                    if (args[i] == "-defectSmokeSeconds") smokeDurationOverride = float.Parse(args[i + 1], CultureInfo.InvariantCulture);
                    if (args[i] == "-defectTimeScale") automaticTimeScale = float.Parse(args[i + 1], CultureInfo.InvariantCulture);
                    if (args[i] == "-defectPreview") previewCapturePath = Path.GetFullPath(args[i + 1]);
                }
                batchRun = Application.isBatchMode;
            }
            if (participantPresentation) InitializeParticipantGame();
            previousScale = Time.timeScale; previousFixedDelta = Time.fixedDeltaTime;
            previousCommunicatorEnabled = CommunicatorFactory.Enabled;
            CommunicatorFactory.Enabled = false;
            environment.DefectExperiment = this;
            Application.logMessageReceived += OnLog;
            if (protocol == null || protocol.model == null || protocol.meshLibrary == null || protocol.scenarios.Count != 5)
            { Abort("Defect protocol/model/library is missing or scenario count is not five."); return; }
            if (calibrationScenarioIndices != null && calibrationScenarioIndices.Length > 0)
            {
                if (!Application.isEditor || !Application.isBatchMode || mode != DefectExperimentMode.AI || !autoStart
                    || calibrationScenarioIndices.Any(index => index < 0 || index >= 5)
                    || calibrationScenarioIndices.Distinct().Count() != calibrationScenarioIndices.Length)
                { Abort("Scenario subsets are restricted to isolated automatic seed calibration."); return; }
                ordinal = calibrationScenarioIndices[0];
            }
            protocolHash = DefectGeometry.Hash(protocol.protocolId + "|" + protocol.detectorVersion + "|" + protocol.scorerVersion + "|" + protocol.modelSha256 + "|"
                + protocol.durationSeconds.ToString("R", CultureInfo.InvariantCulture) + "|"
                + protocol.fixedDeltaTime.ToString("R", CultureInfo.InvariantCulture) + "|"
                + protocol.scanInterval.ToString("R", CultureInfo.InvariantCulture) + "|"
                + protocol.scanRange.ToString("R", CultureInfo.InvariantCulture) + "|"
                + protocol.policyStepHorizon + "|"
                + (protocol.fixedScenarioOrder ? "fixedScenarioOrder=true|" : "")
                + (protocol.inspectWalls ? "inspectWalls=true|" : "")
                + (protocol.deterministicInference ? "deterministicInference=true|" : "")
                + (protocol.fixedStepTransitions ? "fixedStepTransitions=true|" : "")
                + string.Join("|", protocol.scenarios.Select(s => s.frozenManifest)));
            Time.fixedDeltaTime = protocol.fixedDeltaTime;
            Time.timeScale = mode == DefectExperimentMode.Manual ? 1f : automaticTimeScale;
            Academy.Instance.InferenceSeed = 58231;
            var behavior = agent.GetComponent<BehaviorParameters>();
            behavior.DeterministicInference = protocol.deterministicInference;
            behavior.Model = protocol.model;
            behavior.InferenceDevice = InferenceDevice.Burst;
            behavior.BehaviorType = mode == DefectExperimentMode.AI ? BehaviorType.InferenceOnly : BehaviorType.HeuristicOnly;
            campaignStarted = autoStart;
            if (campaignStarted) OpenOutput();
        }

        public bool PrepareEpisode(out Vector3 spawn, out Vector3 target)
        {
            spawn = target = default;
            if (finished || !string.IsNullOrEmpty(error)) return false;
            try
            {
                if (needNewScenario)
                {
                    build?.Dispose();
                    int index = protocol.ScenarioIndex(ordinal, participantOrder);
                    var scenario = protocol.scenarios[index];
                    build = DefectScenarioBuilder.Build(protocol, scenario, generator, adapter, true);
                    detector = new LocalCollisionDetector(build.geometry, protocol.inspectWalls);
                    current = new DefectTrialReport { trialId = scenario.id + "_r" + (repetition + 1),
                        participant = participant, mode = mode.ToString(), order = ordinal + 1,
                        repetition = repetition + 1, manifest = build.manifest,
                        modelSha256 = protocol.modelSha256, protocolSha256 = protocolHash,
                        inferenceSeed = 58231, randomActionSeed = 58231 + repetition * 7919 + index * 101,
                        status = "ready" };
                    UnityEngine.Random.InitState(current.randomActionSeed);
                    actionRandom = new System.Random(current.randomActionSeed);
                    visited.Clear(); nextScan = nextTrace = nextCheckpoint = nextRandomAction = 0f;
                    needNewScenario = false;
                    Running = campaignStarted;
                    trialClock = System.Diagnostics.Stopwatch.StartNew();
                    if (participantPresentation) InitializeParticipantRecovery(build.manifest.spawn, SpawnRotation);
                    if (Running) current.status = "running";
                }
                spawn = build.manifest.spawn;
                // Anchor remains invisible to policy ray sensors; not a navigation goal.
                target = spawn + Vector3.up * (spawn.y < generator.CellSize.y * .5f ? generator.CellSize.y : -generator.CellSize.y);
                previousPosition = spawn; lastSafeHeight = spawn.y;
                if (protocol.fixedStepTransitions && mode != DefectExperimentMode.Manual)
                {
                    var requester = agent.GetComponent<DecisionRequester>();
                    requester.DecisionStep = Academy.Instance.StepCount % requester.DecisionPeriod;
                }
                return true;
            }
            catch (Exception exception) { Abort(exception.ToString()); return false; }
        }

        public void ObserveMovement()
        {
            if (!Running || current == null) return;
            current.simulationSeconds += Time.fixedDeltaTime;
            current.pathLength += Vector3.Distance(previousPosition, agent.transform.position);
            previousPosition = agent.transform.position;
            if (agent.Motor.IsGrounded)
            {
                lastSafeHeight = agent.transform.position.y;
                if (participantPresentation) RememberParticipantSafePosition();
                var cell = generator.WorldToWalkwayCell(agent.transform.position);
                if (build.coverageCells.TryGetValue(cell, out Vector3 floor)
                    && Mathf.Abs(floor.y - agent.transform.position.y) < .35f) visited.Add(cell);
            }
            if (mode != DefectExperimentMode.Manual && current.simulationSeconds >= nextScan)
            {
                nextScan = current.simulationSeconds + protocol.scanInterval;
                detector.Scan(agent.transform.position + Vector3.up * 1.6f,
                    Quaternion.Euler(0f, agent.transform.eulerAngles.y, 0f), protocol.scanRange,
                    hit => AddFinding(hit, "local_ray_detector"));
            }
            if (current.simulationSeconds >= nextTrace)
            {
                nextTrace = current.simulationSeconds + .5f;
                current.trace.Add(new DefectTrace { seconds = current.simulationSeconds,
                    position = agent.transform.position, yaw = agent.transform.eulerAngles.y });
            }
            if (current.simulationSeconds >= nextCheckpoint)
            {
                nextCheckpoint = current.simulationSeconds + 5f;
                SaveCurrent("running"); // Checkpoint evidence; never show scoring to manual testers.
            }
            float duration = smokeDurationOverride > 0f ? smokeDurationOverride : protocol.durationSeconds;
            float elapsed = participantPresentation ? WallSeconds : current.simulationSeconds;
            if (elapsed + .0001f >= duration)
            {
                FinishTrial(participantPresentation && !ParticipantAllTargetsFound ? "failed" : "complete");
                return;
            }
            if (agent.transform.position.y < environment.KillHeight
                || !agent.Motor.IsGrounded && agent.Motor.VerticalVelocity < -7f
                    && lastSafeHeight - agent.transform.position.y > 1.5f)
            {
                current.falls++; Running = false; pendingRespawn = true;
                Feedback(participantPresentation ? "Terjatuh. Kembali ke pijakan aman sebelum jatuh; waktu trial tetap berjalan."
                    : "Jatuh. Kembali ke titik awal; waktu trial tetap berjalan setelah respawn.");
            }
        }

        public void GetRandomAction(out float forward, out float turn)
        {
            if (actionRandom != null && current != null && current.simulationSeconds >= nextRandomAction)
            {
                nextRandomAction = current.simulationSeconds + .7f + (float)actionRandom.NextDouble() * .8f;
                randomForward = actionRandom.NextDouble() < .85 ? 1f : -.5f;
                randomTurn = (float)actionRandom.NextDouble() * 2f - 1f;
            }
            forward = randomForward; turn = randomTurn;
        }

        private void AddFinding(DefectVisualHit hit, string source)
        {
            if (!Running) return;
            DefectScenarioBuilder.Score(current, new DefectFinding { surfaceId = hit.surface.id,
                position = hit.point, source = source, simulationSeconds = current.simulationSeconds,
                surfaceKind = hit.IsInspectableWall ? "wall" : "floor", normal = hit.normal,
                wallSeconds = WallSeconds });
            Feedback("Laporan lokasi dicatat.");
        }

        public void ReportAim()
        {
            if (!Running || mode != DefectExperimentMode.Manual) return;
            if (participantPresentation && ParticipantPaused) return;
            if (build.geometry.Cast(viewCamera.ViewportPointToRay(new Vector3(.5f, .5f)), protocol.scanRange, out var hit)
                && (hit.IsInspectableFloor || protocol.inspectWalls && hit.IsInspectableWall))
            {
                if (participantPresentation) ReportParticipantAnomaly(hit);
                else AddFinding(hit, "manual_aim");
            }
            else
            {
                if (participantPresentation) RecordParticipantMark(default, protocol.inspectWalls ? "no_surface_in_range" : "no_floor_in_range", false);
                Feedback(protocol.inspectWalls ? "Bidik lantai atau tembok dalam jarak 6 meter."
                    : "Arahkan tanda + ke permukaan lantai dalam jarak 6 meter.");
            }
        }

        private void StartCampaign()
        {
            if (campaignStarted || finished) return;
            participant = string.IsNullOrWhiteSpace(participant) ? "anonymous" : participant.Trim();
            campaignStarted = true;
            Time.timeScale = mode == DefectExperimentMode.Manual ? 1f : automaticTimeScale;
            OpenOutput();
            if (!string.IsNullOrEmpty(error)) return;
            needNewScenario = true;
            var behavior = agent.GetComponent<BehaviorParameters>();
            behavior.BehaviorType = mode == DefectExperimentMode.AI ? BehaviorType.InferenceOnly : BehaviorType.HeuristicOnly;
            agent.EndEpisode();
        }

        private void OpenOutput()
        {
            if (string.IsNullOrEmpty(outputDirectory))
                outputDirectory = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                    "results", "DefectExperiments", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            if (Directory.Exists(outputDirectory)) { Abort("Output already exists; choose a new directory."); return; }
            Directory.CreateDirectory(outputDirectory);
            ownsOutput = true;
            File.WriteAllText(Path.Combine(outputDirectory, "protocol.json"), JsonUtility.ToJson(protocol, true));
            File.WriteAllText(Path.Combine(outputDirectory, "session.txt"), "participant=" + participant + "\nmode=" + mode
                + "\norder=" + participantOrder + "\nUnity=" + Application.unityVersion
                + "\nmodel=" + protocol.modelSource + "\nsha256=" + protocol.modelSha256
                + "\nfixedDeltaTime=" + protocol.fixedDeltaTime.ToString(CultureInfo.InvariantCulture)
                + "\ntimeScale=" + Time.timeScale.ToString(CultureInfo.InvariantCulture)
                + "\ndetectorVersion=" + protocol.detectorVersion
                + "\nscorerVersion=" + protocol.scorerVersion
                + (protocol.deterministicInference
                    ? "\ninferenceStreamSeed=58231; deterministic model action outputs; LSTM resets each episode\n"
                    : "\ninferenceStreamSeed=58231; seeded stochastic stream spans trials; LSTM resets each episode\n")
                + "\nsmokeDurationOverride=" + smokeDurationOverride.ToString(CultureInfo.InvariantCulture)
                + (participantPresentation ? "\nrespawn=last visited healthy tile; fallback nearest healthy tile on previous floor; preserve map, elapsed time and findings\n"
                    : "\nrespawn=original spawn; reset episodic policy memory; preserve trial elapsed time and findings\n"));
            var controller = agent.GetComponent<CharacterController>();
            File.WriteAllText(Path.Combine(outputDirectory, "runtime-settings.txt"),
                "motor=" + JsonUtility.ToJson(agent.Motor) + "\nobservations=" + agent.ExperimentObservationSettings
                + "\ncontroller.radius=" + controller.radius.ToString("R", CultureInfo.InvariantCulture)
                + "\ncontroller.height=" + controller.height.ToString("R", CultureInfo.InvariantCulture)
                + "\ncontroller.center=" + DefectGeometry.Vector(controller.center)
                + "\ncontroller.stepOffset=" + controller.stepOffset.ToString("R", CultureInfo.InvariantCulture)
                + "\ncontroller.slopeLimit=" + controller.slopeLimit.ToString("R", CultureInfo.InvariantCulture)
                + "\ncontroller.skinWidth=" + controller.skinWidth.ToString("R", CultureInfo.InvariantCulture)
                + "\ndecisionPeriod=" + agent.GetComponent<DecisionRequester>().DecisionPeriod
                + "\npolicyStepHorizon=" + protocol.policyStepHorizon + "\n");
            if (protocol.deterministicInference) File.AppendAllText(Path.Combine(outputDirectory, "runtime-settings.txt"), "deterministicInference=true\n");
            if (protocol.fixedStepTransitions) File.AppendAllText(Path.Combine(outputDirectory, "runtime-settings.txt"), "automaticTransitions=physics-step-v1; initial decision aligned per episode\n");
            if (calibrationScenarioIndices != null && calibrationScenarioIndices.Length > 0)
                File.AppendAllText(Path.Combine(outputDirectory, "session.txt"), "calibrationScenarioIndices=" + string.Join(",", calibrationScenarioIndices) + "\n");
            if (participantPresentation) File.AppendAllText(Path.Combine(outputDirectory, "runtime-settings.txt"),
                "participantControls=WASD local planar movement; Space long jump; mouse yaw/pitch; F report; Esc pause\n"
                + "participantJumpHeight=" + agent.Motor.ParticipantJumpHeight.ToString("R", CultureInfo.InvariantCulture) + "\n"
                + "participantJumpDistance=" + agent.Motor.ParticipantJumpDistance.ToString("R", CultureInfo.InvariantCulture) + "\n"
                + "participantTiming=active wall seconds; pause excluded; respawn included\n"
                + "participantCompletion=all unique target IDs detected; automatic success; 150s limit is a failed attempt when incomplete\n"
                + "participantMarking=physical support gate; supported/duplicate attempts logged without markers\n"
                + "participantRecovery=last-healthy-tile-v2; 0.35s input grace; looking/marking and clock continue\n");
        }

        private void FinishTrial(string status)
        {
            if (current == null || completed.Contains(current)) return;
            if (participantPresentation) EndParticipantPause();
            Running = false; pendingRespawn = false;
            trialClock?.Stop();
            if (string.IsNullOrEmpty(current.finishReason)) current.finishReason = "time_limit";
            SaveCurrent(status); completed.Add(current);
            WriteSummary();
            if (participantPresentation) WriteParticipantResults();
            Debug.Log($"DEFECT TRIAL {current.trialId} {mode}: TP={current.truePositives}, FP={current.falsePositives}, coverage={current.coverage:P1}, falls={current.falls}.");
            if (mode != DefectExperimentMode.Manual) pendingAdvance = true;
            else if (participantPresentation && completed.Count == 5) NextTrial();
        }

        private void NextTrial()
        {
            pendingAdvance = false;
            if (calibrationScenarioIndices != null && calibrationScenarioIndices.Length > 0)
            {
                calibrationCursor++;
                if (calibrationCursor >= calibrationScenarioIndices.Length) { calibrationCursor = 0; repetition++; }
                ordinal = calibrationScenarioIndices[calibrationCursor];
            }
            else
            {
                ordinal++;
                if (ordinal >= protocol.scenarios.Count) { ordinal = 0; repetition++; }
            }
            int repetitions = mode == DefectExperimentMode.Manual ? 1 : protocol.automaticRepetitions;
            if (repetition >= repetitions)
            {
                if (participantSmoke)
                {
                    try { ValidateParticipantSmoke(); }
                    catch (Exception exception) { Abort(exception.ToString()); return; }
                }
                finished = true; agent.enabled = false;
                File.WriteAllText(Path.Combine(outputDirectory, "session.done"), "complete");
                Debug.Log("DEFECT EXPERIMENT COMPLETE: " + outputDirectory);
                if (batchRun) StopBatch();
                return;
            }
            needNewScenario = true;
            agent.EndEpisode();
        }

        private void SaveCurrent(string status)
        {
            if (!ownsOutput || !campaignStarted || current == null || string.IsNullOrEmpty(outputDirectory) || writing) return;
            writing = true;
            try
            {
                current.status = status;
                current.wallSeconds = WallSeconds;
                current.coverage = visited.Count / (float)Mathf.Max(1, build.coverageCells.Count);
                current.floorZeroCoverage = FloorCoverage(0); current.floorOneCoverage = FloorCoverage(1);
                int classified = current.truePositives + current.falsePositives;
                current.precision = classified > 0 ? current.truePositives / (float)classified : -1f;
                current.recall = current.manifest.defects.Count > 0 ? current.truePositives / (float)current.manifest.defects.Count : -1f;
                File.WriteAllText(Path.Combine(outputDirectory, current.trialId + ".json"), JsonUtility.ToJson(current, true));
            }
            finally { writing = false; }
        }

        private float FloorCoverage(int floor)
        {
            int total = build.coverageCells.Keys.Count(c => c.y == floor);
            return total == 0 ? 0f : visited.Count(c => c.y == floor) / (float)total;
        }

        private void WriteSummary()
        {
            var csv = new StringBuilder("trial,participant,mode,order,repetition,map_seed,actual_map_seed,defect_seed,spawn_seed,inference_seed,status,planted,tp,fp,duplicates,precision,recall,first_report_seconds,first_true_detection_seconds,simulation_seconds,wall_seconds,path_length,coverage,floor0_coverage,floor1_coverage,falls\n");
            foreach (var trial in completed)
                csv.AppendLine(string.Join(",", trial.trialId, Quote(trial.participant), trial.mode,
                    trial.order, trial.repetition, trial.manifest.requestedMapSeed, trial.manifest.actualMapSeed,
                    trial.manifest.defectSeed, trial.manifest.spawnSeed, trial.inferenceSeed, trial.status,
                    trial.manifest.defects.Count, trial.truePositives, trial.falsePositives, trial.duplicates,
                    Number(trial.precision), Number(trial.recall), Number(trial.firstDetectionSeconds),
                    Number(trial.firstTrueDetectionSeconds), Number(trial.simulationSeconds), Number(trial.wallSeconds),
                    Number(trial.pathLength), Number(trial.coverage), Number(trial.floorZeroCoverage), Number(trial.floorOneCoverage), trial.falls));
            File.WriteAllText(Path.Combine(outputDirectory, "summary.csv"), csv.ToString());
        }
        private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        private static string Number(float value) => value < 0f ? "" : value.ToString("R", CultureInfo.InvariantCulture);
        private void Feedback(string message) { feedback = message; feedbackUntil = Time.realtimeSinceStartupAsDouble + 3f; }

        private void FixedUpdate()
        {
            if (protocol == null || !protocol.fixedStepTransitions || mode == DefectExperimentMode.Manual
                || finished || !string.IsNullOrEmpty(error)) return;
            if (pendingAdvance) { NextTrial(); return; }
            if (pendingRespawn)
            {
                pendingRespawn = false;
                agent.EndEpisode();
                Running = true;
            }
        }

        private void Update()
        {
            if (participantPresentation) UpdateParticipantControls();
            if (finished || !string.IsNullOrEmpty(error)) return;
            if (participantPresentation) UpdateParticipantSmoke();
            if (!string.IsNullOrEmpty(previewCapturePath) && Time.frameCount > 30)
            {
                if (!previewQueued) { ScreenCapture.CaptureScreenshot(previewCapturePath); previewQueued = true; }
                else if (File.Exists(previewCapturePath)) Application.Quit();
                return;
            }
            bool physicsTransitions = protocol != null && protocol.fixedStepTransitions && mode != DefectExperimentMode.Manual;
            if (pendingAdvance && !physicsTransitions) { NextTrial(); return; }
            if (pendingRespawn && !physicsTransitions)
            {
                pendingRespawn = false;
                if (participantPresentation) RecoverParticipantFromFall();
                else agent.EndEpisode(); // Automatic/researcher trials retain their original respawn protocol.
                Running = true;
                if (participantPresentation) RefreshParticipantCursor();
                return;
            }
#if ENABLE_INPUT_SYSTEM
            if (!participantPresentation && Running && mode == DefectExperimentMode.Manual && Keyboard.current != null)
            {
                if (Keyboard.current.fKey.wasPressedThisFrame) ReportAim();
                float pitch = (Keyboard.current.downArrowKey.isPressed ? 1f : 0f) - (Keyboard.current.upArrowKey.isPressed ? 1f : 0f);
                cameraPitch = Mathf.Clamp(cameraPitch + pitch * 55f * Time.unscaledDeltaTime, -15f, 80f);
            }
#endif
        }

        private void LateUpdate()
        {
            if (viewCamera == null || agent == null) return;
            viewCamera.transform.SetPositionAndRotation(agent.transform.position + Vector3.up * 1.6f,
                Quaternion.Euler(cameraPitch, agent.transform.eulerAngles.y, 0f));
        }

        private void OnGUI()
        {
            if (participantPresentation) return;
            if (batchRun && string.IsNullOrEmpty(previewCapturePath)) return;
            GUI.skin.label.fontSize = 16; GUI.skin.button.fontSize = 16;
            GUI.Box(new Rect(16, 16, 460, 190), "");
            GUILayout.BeginArea(new Rect(30, 26, 430, 172));
            GUILayout.Label("Pengujian collision — " + mode);
            if (!string.IsNullOrEmpty(error)) GUILayout.Label("Sesi dihentikan. Lihat Console dan hasil sesi.");
            else if (!campaignStarted)
            {
                GUILayout.BeginHorizontal(); GUILayout.Label("Peserta", GUILayout.Width(65)); participant = GUILayout.TextField(participant, 40); GUILayout.EndHorizontal();
                mode = (DefectExperimentMode)GUILayout.Toolbar((int)mode, new[] { "Manual", "AI", "Random" });
                GUILayout.BeginHorizontal(); GUILayout.Label("Urutan " + participantOrder, GUILayout.Width(90));
                if (GUILayout.Button("−")) participantOrder = (participantOrder + 9) % 10;
                if (GUILayout.Button("+")) participantOrder = (participantOrder + 1) % 10;
                GUILayout.EndHorizontal();
                if (GUILayout.Button("Mulai sesi")) StartCampaign();
            }
            else if (finished) GUILayout.Label("Sesi selesai. Hasil tersimpan di results/DefectExperiments.");
            else
            {
                float duration = smokeDurationOverride > 0f ? smokeDurationOverride : protocol.durationSeconds;
                GUILayout.Label($"Trial {ordinal + 1}/5   Ulangan {repetition + 1}   Sisa {Mathf.Max(0f, duration - (current?.simulationSeconds ?? 0f)):0} detik");
                if (mode == DefectExperimentMode.Manual)
                {
                    GUILayout.Label("W/S: maju/mundur • A/D: belok • ↑/↓: pandangan");
                    GUILayout.Label("F: laporkan lantai yang dibidik (+), jarak ≤ 6 m");
                    if (Running && GUILayout.Button("Laporkan lokasi bidikan")) ReportAim();
                    if (!Running && !pendingRespawn && GUILayout.Button("Trial berikutnya")) NextTrial();
                }
            }
            GUILayout.EndArea();
            GUI.Label(new Rect(Screen.width * .5f - 6, Screen.height * .5f - 12, 24, 24), "+");
            if (Time.realtimeSinceStartupAsDouble < feedbackUntil)
                GUI.Label(new Rect(30, Screen.height - 70, Screen.width - 60, 50), feedback);
        }

        private void OnLog(string message, string stack, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error) return;
            if (stack != null && stack.Contains("UnityEditor.Search.SearchDatabase")) return;
            Abort(message + "\n" + stack);
        }
        private void Abort(string reason)
        {
            if (!string.IsNullOrEmpty(error)) return;
            error = reason; Running = false;
            if (agent != null) agent.enabled = false;
            if (ownsOutput && campaignStarted && !string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
                SaveCurrent("error"); File.WriteAllText(Path.Combine(outputDirectory, "error.txt"), reason);
                File.WriteAllText(Path.Combine(outputDirectory, "session.done"), "failed");
            }
            if (batchRun) StopBatch(1);
        }
        private static void StopBatch(int exitCode = 0)
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(exitCode);
#endif
        }
        private void OnDestroy()
        {
            if (participantPresentation) { EndParticipantPause(); RestoreParticipantCursor(); }
            if (ownsOutput && campaignStarted && !finished && string.IsNullOrEmpty(error))
            { if (current != null && !completed.Contains(current)) { SaveCurrent("interrupted"); completed.Add(current); WriteSummary(); if (participantPresentation) WriteParticipantResults(); } }
            Application.logMessageReceived -= OnLog;
            build?.Dispose();
            if (environment != null) environment.DefectExperiment = null;
            Time.timeScale = previousScale; Time.fixedDeltaTime = previousFixedDelta;
            CommunicatorFactory.Enabled = previousCommunicatorEnabled;
        }
    }
}
