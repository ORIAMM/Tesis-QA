using System;
using System.IO;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    [InitializeOnLoad]
    public static class AIEpisodeBudgetCommand
    {
        private const string Active = "EpisodeBudget.Active", Index = "EpisodeBudget.Index", Output = "EpisodeBudget.Output";
        private const string Comparison = "EpisodeBudget.Comparison";
        static AIEpisodeBudgetCommand() { EditorApplication.playModeStateChanged += Changed; }
        public static void Run() => BeginRun(false);
        public static void RunCheckpointComparison() => BeginRun(true);
        private static void BeginRun(bool comparison)
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use isolated .ai-eval batch project.");
            string output = null; string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == (comparison ? "-comparisonOutput" : "-budgetOutput")) output = Path.GetFullPath(args[i + 1]);
            string project = Directory.GetParent(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))).FullName;
            string parent = Path.Combine(project, "results", "Phase3");
            if (output == null || Path.GetDirectoryName(output) != parent || !Path.GetFileName(output).StartsWith(comparison ? "checkpoint_comparison_" : "budget_validation_"))
                throw new InvalidOperationException("Use a NEW results/Phase3/budget_validation_ directory.");
            if (Directory.Exists(output)) throw new InvalidOperationException("Output already exists; refusing overwrite.");
            Directory.CreateDirectory(output);
            SessionState.SetBool(Comparison, comparison);
            SessionState.SetBool(Active, true); SessionState.SetInt(Index, 0); SessionState.SetString(Output, output);
            Start();
        }

        private static void Start()
        {
            try
            {
                bool baseline = SessionState.GetInt(Index, 0) == 0;
                bool comparison = SessionState.GetBool(Comparison, false);
                CommunicatorFactory.Enabled = false;
                EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity", OpenSceneMode.Single);
                string label = comparison ? (baseline ? "Phase3_4499994" : "Phase3_6000091") : "Phase3_4000073";
                string path = "Assets/Scripts/AI/Evaluation/Models/" + label + ".onnx";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(path);
                if (model == null) throw new InvalidOperationException("Missing evaluation model.");
                var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
                var adapter = UnityEngine.Object.FindAnyObjectByType<AIWFCTrainingAdapter>();
                var env = UnityEngine.Object.FindAnyObjectByType<AITrainingEnvironment>();
                var metrics = UnityEngine.Object.FindAnyObjectByType<AIEpisodeMetrics>();
                int maximum = comparison ? 12000 : baseline ? 7500 : 12000;
                int interval = comparison ? 10 : 4;
                Set(agent, "MaxStep", maximum); Set(adapter, "regenerateEveryEpisodes", interval);
                Set(env, "randomSeed", 27421);
                Set(env, "phase3RewardRevision", 0); // Reproduce the historical reward/timeout profile.
                var behavior = new SerializedObject(agent.GetComponent<BehaviorParameters>());
                behavior.FindProperty("m_Model").objectReferenceValue = model;
                behavior.FindProperty("m_BehaviorType").enumValueIndex = (int)BehaviorType.InferenceOnly;
                behavior.FindProperty("m_InferenceDevice").enumValueIndex = (int)InferenceDevice.Burst;
                behavior.FindProperty("m_DeterministicInference").boolValue = true;
                behavior.ApplyModifiedPropertiesWithoutUndo();
                var runner = new GameObject("Temporary paired episode budget validation").AddComponent<AIEpisodeBudgetRunner>();
                runner.agent = agent; runner.adapter = adapter; runner.metrics = metrics;
                runner.condition = Condition(baseline, comparison); runner.maximum = maximum;
                runner.modelLabel = label; runner.requestedEpisodes = comparison ? 50 : 20;
                runner.regenerationInterval = interval;
                if (comparison) runner.protocol = "phase3-paired-checkpoints-level8-v1";
                runner.outputDirectory = SessionState.GetString(Output, "");
                Debug.Log("BUDGET EVALUATION starting " + runner.condition + "; model " + label + "; no trainer.");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { Debug.LogException(e); SessionState.SetBool(Active, false); EditorApplication.Exit(1); }
        }

        private static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Active, false) || state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.delayCall += () =>
            {
                int index = SessionState.GetInt(Index, 0);
                string done = Path.Combine(SessionState.GetString(Output, ""), Condition(index == 0, SessionState.GetBool(Comparison, false)) + ".done");
                if (!File.Exists(done) || File.ReadAllText(done) != "complete")
                { SessionState.SetBool(Active, false); EditorApplication.Exit(1); return; }
                if (index == 0) { SessionState.SetInt(Index, 1); Start(); }
                else { SessionState.SetBool(Active, false); EditorApplication.Exit(0); }
            };
        }
        private static string Condition(bool baseline, bool comparison) => comparison
            ? baseline ? "checkpoint4499994" : "checkpoint6000091"
            : baseline ? "steps7500" : "steps12000";
        private static void Set(UnityEngine.Object target, string field, int value)
        { var s = new SerializedObject(target); s.FindProperty(field).intValue = value; s.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
