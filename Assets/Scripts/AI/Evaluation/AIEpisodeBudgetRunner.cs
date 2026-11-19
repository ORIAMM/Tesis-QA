using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using Unity.MLAgents;
using UnityEngine;

namespace TesisQA.AI
{
    [Serializable]
    public sealed class AIBudgetEpisode
    {
        public AIModelEvaluationEpisode episode;
        public bool has7500Snapshot;
        public int snapshotStep;
        public float snapshotFloor0, snapshotFloor1;
    }

    [Serializable]
    public sealed class AIEpisodeBudgetReport
    {
        public string protocol = "phase3-paired-budget-level8-v1";
        public string model = "Phase3_4000073", modelSha256, condition, error;
        public int requestedEpisodes = 20, maxStep, mapRegenerationInterval = 4;
        public int mapSeedBase = 1427003, spawnSeed = 27421, inferenceSeed = 58231;
        public float timeScale = 40f, fixedDeltaTime, coverageTarget = 0.1f, maximumFloorDifference = 0.1f;
        public bool deterministicInference = true;
        public int rewardRevision;
        public List<AIBudgetEpisode> episodes = new List<AIBudgetEpisode>();
        public List<string> diagnostics = new List<string>();
    }

    // Temporary inference-only component, installed only in an unsaved .ai-eval
    // scene. Snapshots read metrics, never feed observations or choose actions.
    [DefaultExecutionOrder(-11000)]
    public sealed class AIEpisodeBudgetRunner : MonoBehaviour
    {
        public ExplorationAgent agent;
        public AIWFCTrainingAdapter adapter;
        public AIEpisodeMetrics metrics;
        public string condition, outputDirectory;
        public int maximum;
        public string modelLabel = "Phase3_4000073";
        public string protocol = "phase3-paired-budget-level8-v1";
        public int requestedEpisodes = 20, regenerationInterval = 4;
        public int rewardRevision;
        private AIEpisodeBudgetReport report;
        private AIBudgetEpisode current;
        private AITrainingEnvironment environment;
        private MethodInfo floorCoverage;
        private bool pending, finished;
        private double started;

        private void Awake()
        {
            CommunicatorFactory.Enabled = false;
            UnityEngine.Random.InitState(58231);
            Academy.Instance.InferenceSeed = 58231;
            object channel = typeof(EnvironmentParameters).GetField("m_Channel", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(Academy.Instance.EnvironmentParameters);
            var parameters = (Dictionary<string, Func<float>>)channel.GetType()
                .GetField("m_Parameters", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(channel);
            parameters["terrain_map_seed"] = () => 1427003f;
            parameters["terrain_curriculum"] = () => 1f;
            parameters["phase3_curriculum"] = () => 1f;
            parameters["phase3_reward_revision"] = () => rewardRevision;
            parameters["terrain_level"] = () => 8f;
            // Deliberately no terrain_driver: fixed lesson, no rehearsal.
            parameters["completed_branch_assist"] = () => 0f;
            parameters["branch_memory_observation"] = () => 1f;
            parameters["terrain_distant_observation"] = () => 1f;
            parameters["ramp_memory_observation"] = () => 1f;
            parameters["stair_search_shaping"] = () => 0f;
            Time.timeScale = 40f; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            started = Time.realtimeSinceStartupAsDouble;
            environment = agent.GetComponentInParent<AITrainingEnvironment>();
            floorCoverage = typeof(AITrainingEnvironment).GetMethod("CalculateFloorCoverage", BindingFlags.Instance | BindingFlags.NonPublic);
            report = new AIEpisodeBudgetReport { condition = condition, maxStep = maximum, fixedDeltaTime = Time.fixedDeltaTime,
                model = modelLabel, protocol = protocol, requestedEpisodes = requestedEpisodes,
                mapRegenerationInterval = regenerationInterval, rewardRevision = rewardRevision };
            using (var hash = SHA256.Create())
            using (var input = File.OpenRead(Path.Combine(Application.dataPath, "Scripts/AI/Evaluation/Models/" + modelLabel + ".onnx")))
                report.modelSha256 = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            metrics.EpisodeStarted += Begin; metrics.EpisodeFinished += End;
            Application.logMessageReceived += Log;
        }

        private void Begin()
        {
            if (pending || finished) return;
            if (agent.MaxStep != maximum) { report.error = "Agent MaxStep differs from condition."; pending = true; return; }
            current = new AIBudgetEpisode { episode = new AIModelEvaluationEpisode {
                episode = report.episodes.Count + 1, mapSeed = adapter.LastGeneratedSeed,
                spawn = agent.transform.position, spawnYaw = agent.transform.eulerAngles.y,
                target = environment.TargetTransform.position } };
        }

        private void FixedUpdate()
        {
            if (current == null || current.has7500Snapshot || agent.StepCount < 7500) return;
            current.has7500Snapshot = true; current.snapshotStep = agent.StepCount;
            current.snapshotFloor0 = (float)floorCoverage.Invoke(environment, new object[] {0});
            current.snapshotFloor1 = (float)floorCoverage.Invoke(environment, new object[] {1});
        }

        private void End(AIEpisodeResult result)
        {
            if (pending || finished || current == null) return;
            current.episode.finish = agent.transform.position; current.episode.result = result;
            if (!current.has7500Snapshot && result.steps >= 7500)
            {
                current.has7500Snapshot = true; current.snapshotStep = result.steps;
                current.snapshotFloor0 = result.floorZeroCoverage; current.snapshotFloor1 = result.floorOneCoverage;
            }
            report.episodes.Add(current); current = null; Save();
            Debug.Log($"BUDGET EVALUATION {condition}: {report.episodes.Count}/{requestedEpisodes}; success={result.success}; "
                + $"coverage={result.floorZeroCoverage:F3}/{result.floorOneCoverage:F3}; "
                + $"up/down={result.ascentTransitions}/{result.descentTransitions}; steps={result.steps}; seed={adapter.LastGeneratedSeed}");
            pending = report.episodes.Count >= report.requestedEpisodes;
        }

        private void Log(string message, string trace, LogType type)
        {
            if (finished || report == null || type != LogType.Error && type != LogType.Exception) return;
            if (trace != null && trace.Contains("UnityEditor.Search.SearchDatabase"))
            { report.diagnostics.Add(message + "\n" + trace); return; }
            report.error = message + "\n" + trace; pending = true;
        }

        private void Update()
        {
            if (finished) return;
            if (!agent.enabled) { report.error = "Agent disabled."; pending = true; }
            if (Time.realtimeSinceStartupAsDouble - started > 2400)
            { report.error = "40-minute condition timeout."; pending = true; }
            if (!pending) return;
            finished = true; Save();
            File.WriteAllText(Path.Combine(outputDirectory, condition + ".done"), string.IsNullOrEmpty(report.error) ? "complete" : "failed");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void Save() => File.WriteAllText(Path.Combine(outputDirectory, condition + ".json"), JsonUtility.ToJson(report, true));
        private void OnDestroy()
        {
            if (metrics != null) { metrics.EpisodeStarted -= Begin; metrics.EpisodeFinished -= End; }
            Application.logMessageReceived -= Log;
        }
    }
}
