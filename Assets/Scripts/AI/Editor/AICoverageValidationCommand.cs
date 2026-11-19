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
    public static class AICoverageValidationCommand
    {
        private const string Active = "CoverageValidation.Active";
        private const string Index = "CoverageValidation.Index";
        private const string Output = "CoverageValidation.Output";
        private const string Label = "Phase2E_7000033";
        static AICoverageValidationCommand()
        { EditorApplication.playModeStateChanged += Changed; }

        public static void Run()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use isolated .ai-eval batch project.");
            string output = null; string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-coverageOutput") output = Path.GetFullPath(args[i+1]);
            string project = Directory.GetParent(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))).FullName;
            if (output == null || !output.StartsWith(Path.Combine(project, "results", "Phase2E", "coverage_validation"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Output must be a new Phase2E coverage_validation directory.");
            if (Directory.Exists(output)) throw new InvalidOperationException("Evaluation output exists; refusing overwrite.");
            Directory.CreateDirectory(output);
            SessionState.SetBool(Active, true); SessionState.SetInt(Index, 0); SessionState.SetString(Output, output);
            Start();
        }

        private static void Start()
        {
            try
            {
                bool small = SessionState.GetInt(Index, 0) == 0;
                CommunicatorFactory.Enabled = false;
                EditorSceneManager.OpenScene(small ? "Assets/Scenes/AI/AI_Phase2_SmallWFC.unity"
                    : "Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity", OpenSceneMode.Single);
                string modelPath = "Assets/Scripts/AI/Evaluation/Models/" + Label + ".onnx";
                AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
                var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(modelPath);
                if (model == null) throw new InvalidOperationException("Missing validation model.");
                var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
                var adapter = UnityEngine.Object.FindAnyObjectByType<AIWFCTrainingAdapter>();
                var env = UnityEngine.Object.FindAnyObjectByType<AITrainingEnvironment>();
                var metrics = UnityEngine.Object.FindAnyObjectByType<AIEpisodeMetrics>();
                Set(agent, "useTerrainProfileSensor", true); Set(agent, "useBranchMemorySensor", true);
                Set(agent, "useDistantTerrainSensor", true); Set(agent, "useRampMemorySensor", true);
                Set(agent, "useLongRangeTerrainProfile", false);
                Set(adapter, "useFloorCountCurriculum", true); Set(adapter, "useFloorTransferLesson", true);
                Set(adapter, "defaultTerrainCurriculum", true); Set(adapter, "defaultTerrainLevel", 9);
                Set(adapter, "defaultGraduatedTransferLessons", true);
                Set(adapter, "regenerateEveryEpisodes", small ? 6 : 2);
                Set(adapter, "baseMapSeed", 913701); Set(env, "randomSeed", 27421);
                Set(env, "defaultStairSearchShaping", false);
                Set(env, "phase3RewardRevision", 0); // Historical Phase2E transfer protocol.
                var behavior = new SerializedObject(agent.GetComponent<BehaviorParameters>());
                behavior.FindProperty("m_Model").objectReferenceValue = model;
                behavior.FindProperty("m_BehaviorType").enumValueIndex = (int)BehaviorType.InferenceOnly;
                behavior.FindProperty("m_InferenceDevice").enumValueIndex = (int)InferenceDevice.Burst;
                behavior.FindProperty("m_DeterministicInference").boolValue = true;
                behavior.ApplyModifiedPropertiesWithoutUndo();
                var runner = new GameObject("Temporary coverage validation").AddComponent<AICoverageValidationRunner>();
                runner.agent = agent; runner.adapter = adapter; runner.metrics = metrics;
                runner.condition = small ? "small" : "large"; runner.modelLabel = Label;
                runner.outputDirectory = SessionState.GetString(Output, "");
                runner.episodeCount = small ? 30 : 10; runner.regenerationInterval = small ? 6 : 2;
                Debug.Log("COVERAGE VALIDATION starting " + runner.condition + "; model=" + Label);
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
                string file = Path.Combine(SessionState.GetString(Output, ""), (index == 0 ? "small" : "large") + ".done");
                if (!File.Exists(file) || File.ReadAllText(file) != "complete")
                { SessionState.SetBool(Active, false); EditorApplication.Exit(1); return; }
                if (index == 0) { SessionState.SetInt(Index, 1); Start(); }
                else { SessionState.SetBool(Active, false); EditorApplication.Exit(0); }
            };
        }
        private static void Set(UnityEngine.Object target, string field, bool value)
        { var s = new SerializedObject(target); s.FindProperty(field).boolValue = value; s.ApplyModifiedPropertiesWithoutUndo(); }
        private static void Set(UnityEngine.Object target, string field, int value)
        { var s = new SerializedObject(target); s.FindProperty(field).intValue = value; s.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
