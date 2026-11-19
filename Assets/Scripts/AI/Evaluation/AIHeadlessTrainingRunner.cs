using System;
using System.IO;
using Unity.MLAgents;
using UnityEngine;

namespace TesisQA.AI
{
    // Exists only in the temporary unsaved scene of the isolated batch Editor.
    [DefaultExecutionOrder(-10000)]
    public sealed class AIHeadlessTrainingRunner : MonoBehaviour
    {
        [Serializable]
        private sealed class Status
        {
            public string utc;
            public bool connected;
            public int episodes;
            public int mainLesson;
            public int mixedLevel;
            public int currentEpisodeStep;
            public string error;
        }
        public ExplorationAgent agent;
        public AIWFCTrainingAdapter adapter;
        public AIEpisodeMetrics metrics;
        public string statusDirectory;
        private string failure;
        private int episodes;
        private double nextStatus;
        private bool ending;

        private void Awake()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            metrics.EpisodeStarted += OnEpisode;
            Application.logMessageReceived += OnLog;
            // The trainer sets time scale through its normal engine channel.
            var academy = Academy.Instance;
            SaveStatus();
            Debug.Log("AI HEADLESS TRAINING started; production episode limits unchanged.");
        }
        private void OnEpisode() => episodes++;
        private void OnLog(string message, string trace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception) return;
            if (trace != null && trace.Contains("UnityEditor.Search.SearchDatabase")
                && trace.Contains("UnityEditor.Search.SearchInit.IndexationOnStartup")) return;
            failure = message + "\n" + trace;
        }
        private void SaveStatus()
        {
            Directory.CreateDirectory(statusDirectory);
            var status = new Status
            {
                utc = DateTime.UtcNow.ToString("O"),
                connected = Academy.IsInitialized && Academy.Instance.IsCommunicatorOn,
                episodes = episodes, mainLesson = adapter.MainTrainingStage,
                mixedLevel = adapter.MixedSearchLevel,
                currentEpisodeStep = agent.StepCount, error = failure
            };
            File.WriteAllText(Path.Combine(statusDirectory, "unity_status.json"), JsonUtility.ToJson(status, true));
        }
        private void Update()
        {
            if (ending) return;
            if (agent == null || !agent.enabled) failure = failure ?? "Headless agent is disabled.";
            bool stop = File.Exists(Path.Combine(statusDirectory, "stop.request"));
            if (Time.realtimeSinceStartupAsDouble >= nextStatus || stop || failure != null)
            {
                SaveStatus();
                nextStatus = Time.realtimeSinceStartupAsDouble + 10;
            }
            if (!stop && failure == null) return;
            ending = true;
            if (failure != null) File.WriteAllText(Path.Combine(statusDirectory, "unity_failed.txt"), failure);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(failure == null ? 0 : 1);
#else
            Application.Quit(failure == null ? 0 : 1);
#endif
        }
        private void OnDestroy()
        {
            if (metrics != null) metrics.EpisodeStarted -= OnEpisode;
            Application.logMessageReceived -= OnLog;
        }
    }
}
