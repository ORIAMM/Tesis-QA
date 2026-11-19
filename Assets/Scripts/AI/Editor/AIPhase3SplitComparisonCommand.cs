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
    public static class AIPhase3SplitComparisonCommand
    {
        private const string Active = "SplitComparison.Active", Index = "SplitComparison.Index", Output = "SplitComparison.Output";
        private static readonly string[] Labels = {"Phase3R_2000063", "Phase3S_499998", "Phase3S_1000061"};
        private static readonly string[] Conditions = {"baseline2000063", "split499998", "split1000061"};
        static AIPhase3SplitComparisonCommand() { EditorApplication.playModeStateChanged += Changed; }
        public static void Run()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use isolated .ai-eval batch project only.");
            string output = null; string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-comparisonOutput") output = Path.GetFullPath(args[i + 1]);
            string root = Directory.GetParent(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))).FullName;
            if (output == null || Path.GetDirectoryName(output) != Path.Combine(root, "results", "Phase3S")
                || !Path.GetFileName(output).StartsWith("paired_comparison_") || Directory.Exists(output))
                throw new InvalidOperationException("Use a NEW results/Phase3S/paired_comparison_ directory; no overwrite.");
            Directory.CreateDirectory(output);
            SessionState.SetBool(Active, true); SessionState.SetInt(Index, 0); SessionState.SetString(Output, output);
            Start();
        }
        private static void Start()
        {
            try
            {
                int index = SessionState.GetInt(Index, 0);
                CommunicatorFactory.Enabled = false;
                EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity", OpenSceneMode.Single);
                string path = "Assets/Scripts/AI/Evaluation/Models/" + Labels[index] + ".onnx";
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(path);
                if (model == null) throw new InvalidOperationException("Missing comparison model: " + path);
                var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
                var adapter = UnityEngine.Object.FindAnyObjectByType<AIWFCTrainingAdapter>();
                var env = UnityEngine.Object.FindAnyObjectByType<AITrainingEnvironment>();
                var metrics = UnityEngine.Object.FindAnyObjectByType<AIEpisodeMetrics>();
                Set(agent, "MaxStep", 12000); Set(adapter, "regenerateEveryEpisodes", 10);
                Set(env, "randomSeed", 27421); Set(env, "phase3RewardRevision", 2);
                var behavior = new SerializedObject(agent.GetComponent<BehaviorParameters>());
                behavior.FindProperty("m_Model").objectReferenceValue = model;
                behavior.FindProperty("m_BehaviorType").enumValueIndex = (int)BehaviorType.InferenceOnly;
                behavior.FindProperty("m_InferenceDevice").enumValueIndex = (int)InferenceDevice.Burst;
                behavior.FindProperty("m_DeterministicInference").boolValue = true;
                behavior.ApplyModifiedPropertiesWithoutUndo();
                var runner = new GameObject("Temporary split reward paired inference").AddComponent<AIEpisodeBudgetRunner>();
                runner.agent = agent; runner.adapter = adapter; runner.metrics = metrics;
                runner.condition = Conditions[index]; runner.maximum = 12000; runner.modelLabel = Labels[index];
                runner.requestedEpisodes = 50; runner.regenerationInterval = 10; runner.rewardRevision = 2;
                runner.protocol = "phase3s-paired-three-models-level8-v1";
                runner.outputDirectory = SessionState.GetString(Output, "");
                Debug.Log("SPLIT COMPARISON starting " + runner.condition + ": 50 inference episodes, revision 2 for ALL models, no trainer, no scene saves.");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error)
            { Debug.LogException(error); SessionState.SetBool(Active, false); EditorApplication.Exit(1); }
        }
        private static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Active, false) || state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.delayCall += () =>
            {
                int index = SessionState.GetInt(Index, 0);
                string done = Path.Combine(SessionState.GetString(Output, ""), Conditions[index] + ".done");
                if (!File.Exists(done) || File.ReadAllText(done) != "complete")
                { SessionState.SetBool(Active, false); EditorApplication.Exit(1); return; }
                if (index + 1 < Labels.Length) { SessionState.SetInt(Index, index + 1); Start(); }
                else { SessionState.SetBool(Active, false); EditorApplication.Exit(0); }
            };
        }
        private static void Set(UnityEngine.Object target, string field, int value)
        { var s = new SerializedObject(target); s.FindProperty(field).intValue = value; s.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
