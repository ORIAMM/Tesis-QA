using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Unity.MLAgents;
using UnityEngine;

namespace TesisQA.AI
{
    [Serializable]
    public sealed class AIModelTracePoint
    {
        public int step;
        public Vector3 position;
        public float yaw;
        public int routeSteps;
        public int stairEntries;
        public bool grounded;
    }

    [Serializable]
    public sealed class AIModelEvaluationEpisode
    {
        public int episode;
        public int mapSeed;
        public Vector3 spawn;
        public Vector3 target;
        public float spawnYaw;
        public Vector3 finish;
        public AIEpisodeResult result;
        public Vector3 stairMouth;
        public string trajectoryDiagnosis;
        public List<AIModelTracePoint> trace = new List<AIModelTracePoint>();
    }

    [Serializable]
    public sealed class AIModelEvaluationReport
    {
        public string model;
        public string modelSha256;
        public string protocol = "Phase2-transfer-mixed-v2";
        public int spawnSeed = 27421;
        public int mapSeedBase = 913701;
        public int requestedEpisodes;
        public int inferenceSeed;
        public bool deterministicInference;
        public bool traceEnabled;
        public int mapRegenerationInterval;
        public float fixedDeltaTime;
        public float timeScale;
        public string error;
        public List<string> editorDiagnostics = new List<string>();
        public List<AIModelEvaluationEpisode> episodes = new List<AIModelEvaluationEpisode>();
    }

    // Created only in the temporary, unsaved evaluation scene by the editor command.
    [DefaultExecutionOrder(-10000)]
    public sealed class AIModelEvaluationRunner : MonoBehaviour
    {
        public ExplorationAgent agent;
        public AIWFCTrainingAdapter adapter;
        public AIEpisodeMetrics metrics;
        public string modelLabel;
        public string outputDirectory;
        public int episodeCount = 50;
        public int inferenceSeed = 58231;
        public bool deterministicInference;
        public bool captureTrace;

        private AIModelEvaluationReport report;
        private AIModelEvaluationEpisode current;
        private bool pendingFinish;
        private bool finished;
        private double startedAt;

        private void Awake()
        {
            report = new AIModelEvaluationReport
            {
                model = modelLabel,
                requestedEpisodes = episodeCount,
                inferenceSeed = inferenceSeed,
                deterministicInference = deterministicInference,
                traceEnabled = captureTrace,
                mapRegenerationInterval = 10,
                fixedDeltaTime = Time.fixedDeltaTime,
                timeScale = 40f
            };
            startedAt = Time.realtimeSinceStartupAsDouble;
            UnityEngine.Random.InitState(inferenceSeed);
            // An inference-only batch must never connect to a user's live trainer.
            CommunicatorFactory.Enabled = false;
            Academy.Instance.InferenceSeed = inferenceSeed;
            Time.timeScale = 40f;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            metrics.EpisodeStarted += OnEpisodeStarted;
            metrics.EpisodeFinished += OnEpisodeFinished;
            Application.logMessageReceived += OnLog;
#if UNITY_EDITOR
            // Reacquire the asset after Play Mode's domain/scene reload, before
            // the agent initializes its inference policy.
            var model = UnityEditor.AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(
                "Assets/Scripts/AI/Evaluation/Models/" + modelLabel + ".onnx");
            if (model == null)
            {
                report.error = "Evaluation model asset is missing in Play Mode.";
                pendingFinish = true;
                return;
            }
            using (var hash = SHA256.Create())
            using (var input = File.OpenRead(Path.Combine(Application.dataPath,
                "Scripts/AI/Evaluation/Models/" + modelLabel + ".onnx")))
                report.modelSha256 = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            agent.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>().Model = model;
#endif
        }

        private void OnEpisodeStarted()
        {
            if (pendingFinish || finished) return;
            current = new AIModelEvaluationEpisode
            {
                episode = report.episodes.Count + 1,
                mapSeed = adapter.LastGeneratedSeed,
                spawn = agent.transform.position,
                spawnYaw = agent.transform.eulerAngles.y,
                target = agent.GetComponentInParent<AITrainingEnvironment>().TargetTransform.position
            };
            current.stairMouth = adapter.DiagnosticStairMouth;
            CaptureTrace(true);
        }

        private void FixedUpdate() => CaptureTrace(false);

        private void CaptureTrace(bool force)
        {
            if (!captureTrace || current == null || agent == null) return;
            if (!force && current.trace.Count > 0
                && agent.StepCount < current.trace[current.trace.Count - 1].step + 10) return;
            current.trace.Add(new AIModelTracePoint
            {
                step = agent.StepCount, position = agent.transform.position,
                yaw = agent.transform.eulerAngles.y,
                routeSteps = adapter.DiagnosticRouteSteps(agent.transform.position),
                stairEntries = metrics.DiagnosticStairEntries, grounded = agent.Motor.IsGrounded
            });
        }

        private void OnEpisodeFinished(AIEpisodeResult result)
        {
            if (pendingFinish || finished || current == null) return;
            current.finish = agent.transform.position;
            current.result = result;
            CaptureTrace(true);
            if (captureTrace)
            {
                bool reachedMouthTile = current.trace.Exists(p => p.routeSteps == 0);
                current.trajectoryDiagnosis = result.success ? "completed_transfer"
                    : result.stairTraversals > 0 ? "entered_stair_without_transfer"
                    : reachedMouthTile ? "reached_mouth_tile_without_stair_entry"
                    : "did_not_reach_mouth_tile";
            }
            report.episodes.Add(current);
            current = null;
            SaveReport();
            if (report.episodes.Count % 10 == 0)
            {
                Debug.Log($"AI EVALUATION {modelLabel}: {report.episodes.Count}/{episodeCount} episodes completed.");
            }
            pendingFinish = report.episodes.Count >= episodeCount;
        }

        private void OnLog(string condition, string trace, LogType type)
        {
            if (finished || report == null || (type != LogType.Error && type != LogType.Exception)) return;
#if UNITY_EDITOR
            // Unity 6000.5 can fail its editor-only search index on the first
            // startup of a copied project. Retain the diagnostic; it does not
            // originate in inference, physics, or the episode environment.
            if (trace != null && trace.Contains("UnityEditor.Search.SearchDatabase")
                && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup"))
            {
                report.editorDiagnostics.Add(condition + "\n" + trace);
                return;
            }
#endif
            report.error = condition + "\n" + trace;
            pendingFinish = true;
        }

        private void Update()
        {
            if (!pendingFinish && (agent == null || !agent.enabled))
            {
                report.error = "Agent became disabled before evaluation completed.";
                pendingFinish = true;
            }
            if (!pendingFinish && Time.realtimeSinceStartupAsDouble - startedAt > 3600)
            {
                report.error = "Evaluation exceeded the one-hour limit.";
                pendingFinish = true;
            }
            if (!pendingFinish || finished) return;
            finished = true;
            SaveReport();
            File.WriteAllText(Path.Combine(outputDirectory, modelLabel + ".done"),
                string.IsNullOrEmpty(report.error) ? "complete" : "failed");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(string.IsNullOrEmpty(report.error) ? 0 : 1);
#endif
        }

        private void SaveReport()
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, modelLabel + ".json"), JsonUtility.ToJson(report, true));
            var csv = new StringBuilder("episode,map_seed,direction,success,steps,spawn_stair_steps,ascent_transitions,descent_transitions,collisions,revisits,short_loops,no_new_area_timeout,no_displacement_failure,path_length,coverage,floor_0_coverage,floor_1_coverage\n");
            foreach (var e in report.episodes)
            {
                var r = e.result;
                csv.AppendLine(string.Join(",", e.episode, e.mapSeed, r.direction, r.success ? 1 : 0,
                    r.steps, r.spawnStairSteps, r.ascentTransitions, r.descentTransitions,
                    r.collisions, r.revisits, r.shortLoops, r.noNewAreaTimeouts, r.noDisplacementFailures,
                    r.pathLength.ToString("R", CultureInfo.InvariantCulture),
                    r.coverage.ToString("R", CultureInfo.InvariantCulture),
                    r.floorZeroCoverage.ToString("R", CultureInfo.InvariantCulture),
                    r.floorOneCoverage.ToString("R", CultureInfo.InvariantCulture)));
            }
            File.WriteAllText(Path.Combine(outputDirectory, modelLabel + ".csv"), csv.ToString());
        }

        private void OnDestroy()
        {
            if (metrics != null)
            {
                metrics.EpisodeStarted -= OnEpisodeStarted;
                metrics.EpisodeFinished -= OnEpisodeFinished;
            }
            Application.logMessageReceived -= OnLog;
        }
    }
}
