using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Reflection;
using Unity.MLAgents;
using UnityEngine;

namespace TesisQA.AI
{
    [Serializable]
    public sealed class AICoverageValidationReport
    {
        public string protocol = "coverage-two-floor-fixed-level9-v1";
        public string model, modelSha256, condition, error;
        public int requestedEpisodes, maxStep, mapRegenerationInterval, spawnSeed = 27421, inferenceSeed = 58231;
        public float timeScale = 30f, fixedDeltaTime, coverageTarget = 0.2f, maximumFloorDifference = 0.1f;
        public bool deterministicInference = true;
        public List<AIModelEvaluationEpisode> episodes = new List<AIModelEvaluationEpisode>();
        public List<string> diagnostics = new List<string>();
    }

    // Isolated inference only. No parameters are sent to a trainer and no scene
    // is saved. Oracle diagnostics (seed, coverage) never feed agent observations.
    [DefaultExecutionOrder(-11000)]
    public sealed class AICoverageValidationRunner : MonoBehaviour
    {
        public ExplorationAgent agent;
        public AIWFCTrainingAdapter adapter;
        public AIEpisodeMetrics metrics;
        public string condition, outputDirectory, modelLabel;
        public int episodeCount, regenerationInterval;
        private AICoverageValidationReport report;
        private AIModelEvaluationEpisode current;
        private bool pending, finished;
        private double started;

        private void Awake()
        {
            CommunicatorFactory.Enabled = false;
            UnityEngine.Random.InitState(58231);
            Academy.Instance.InferenceSeed = 58231;
            // The adapter intentionally takes terrain_map_seed from environment
            // parameters when Academy is initialized; Inspector seed alone is not enough.
            var parameters = Academy.Instance.EnvironmentParameters;
            object channel = typeof(EnvironmentParameters).GetField("m_Channel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(parameters);
            var entries = (Dictionary<string, Func<float>>)channel.GetType()
                .GetField("m_Parameters", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(channel);
            entries["terrain_map_seed"] = () => 913701f;
            entries["terrain_level"] = () => 9f;
            entries["completed_branch_assist"] = () => 0f;
            entries["branch_memory_observation"] = () => 1f;
            entries["terrain_distant_observation"] = () => 1f;
            entries["ramp_memory_observation"] = () => 1f;
            Time.timeScale = 30f; QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            started = Time.realtimeSinceStartupAsDouble;
            report = new AICoverageValidationReport { model = modelLabel, condition = condition,
                requestedEpisodes = episodeCount, maxStep = condition == "small" ? 6000 : 7500,
                mapRegenerationInterval = regenerationInterval, fixedDeltaTime = Time.fixedDeltaTime };
            using (var hash = SHA256.Create())
            using (var input = File.OpenRead(Path.Combine(Application.dataPath,
                "Scripts/AI/Evaluation/Models/" + modelLabel + ".onnx")))
                report.modelSha256 = BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            metrics.EpisodeStarted += Begin;
            metrics.EpisodeFinished += End;
            Application.logMessageReceived += Log;
        }

        private void Begin()
        {
            if (pending || finished) return;
            current = new AIModelEvaluationEpisode { episode = report.episodes.Count + 1,
                mapSeed = adapter.LastGeneratedSeed, spawn = agent.transform.position,
                spawnYaw = agent.transform.eulerAngles.y,
                target = agent.GetComponentInParent<AITrainingEnvironment>().TargetTransform.position };
        }

        private void End(AIEpisodeResult result)
        {
            if (pending || finished || current == null) return;
            current.finish = agent.transform.position; current.result = result;
            report.episodes.Add(current); current = null; Save();
            Debug.Log($"COVERAGE VALIDATION {condition}: {report.episodes.Count}/{episodeCount}; "
                + $"success={result.success}; floor0={result.floorZeroCoverage:F3}; floor1={result.floorOneCoverage:F3}; "
                + $"transitions={result.ascentTransitions}/{result.descentTransitions}; seed={adapter.LastGeneratedSeed}");
            pending = report.episodes.Count >= episodeCount;
        }

        private void Log(string message, string trace, LogType type)
        {
            if (finished || report == null || type != LogType.Error && type != LogType.Exception) return;
            if (trace != null && trace.Contains("UnityEditor.Search.SearchDatabase"))
            { report.diagnostics.Add(message); return; }
            report.error = message + "\n" + trace; pending = true;
        }

        private void Update()
        {
            if (finished) return;
            if (!agent.enabled) { report.error = "Agent disabled before completion."; pending = true; }
            if (Time.realtimeSinceStartupAsDouble - started > 3600)
            { report.error = "One-hour evaluation timeout."; pending = true; }
            if (!pending) return;
            finished = true; Save();
            File.WriteAllText(Path.Combine(outputDirectory, condition + ".done"),
                string.IsNullOrEmpty(report.error) ? "complete" : "failed");
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private void Save()
        { Directory.CreateDirectory(outputDirectory); File.WriteAllText(Path.Combine(outputDirectory, condition + ".json"), JsonUtility.ToJson(report, true)); }
        private void OnDestroy()
        {
            if (metrics != null) { metrics.EpisodeStarted -= Begin; metrics.EpisodeFinished -= End; }
            Application.logMessageReceived -= Log;
        }
    }
}
