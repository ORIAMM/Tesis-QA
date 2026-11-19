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
    public static class AIFloorTimingCommand
    {
        private const string Active = "FloorTiming.Active", Output = "FloorTiming.Output";
        static AIFloorTimingCommand() { EditorApplication.playModeStateChanged += Changed; }
        public static void Run()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Only isolated .ai-eval batch project is supported.");
            string output = null; var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-timingOutput") output = Path.GetFullPath(args[i + 1]);
            string root = Directory.GetParent(Path.GetFullPath(Path.Combine(Application.dataPath, ".."))).FullName;
            if (output == null || Path.GetDirectoryName(output) != Path.Combine(root, "results", "Phase3R")
                || !Path.GetFileName(output).StartsWith("exploration_timing_") || Directory.Exists(output))
                throw new InvalidOperationException("Use a NEW results/Phase3R/exploration_timing_ directory; no overwrite.");
            Directory.CreateDirectory(output);
            SessionState.SetString(Output, output);
            CommunicatorFactory.Enabled = false;
            EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity", OpenSceneMode.Single);
            const string label = "Phase3R_2000063";
            string path = "Assets/Scripts/AI/Evaluation/Models/" + label + ".onnx";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(path);
            if (model == null) throw new InvalidOperationException("Missing model.");
            var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
            var adapter = UnityEngine.Object.FindAnyObjectByType<AIWFCTrainingAdapter>();
            var env = UnityEngine.Object.FindAnyObjectByType<AITrainingEnvironment>();
            var metrics = UnityEngine.Object.FindAnyObjectByType<AIEpisodeMetrics>();
            Set(agent, "MaxStep", 12000); Set(adapter, "regenerateEveryEpisodes", 10);
            Set(env, "randomSeed", 27421); Set(env, "phase3RewardRevision", 1);
            var behavior = new SerializedObject(agent.GetComponent<BehaviorParameters>());
            behavior.FindProperty("m_Model").objectReferenceValue = model;
            behavior.FindProperty("m_BehaviorType").enumValueIndex = (int)BehaviorType.InferenceOnly;
            behavior.FindProperty("m_InferenceDevice").enumValueIndex = (int)InferenceDevice.Burst;
            behavior.FindProperty("m_DeterministicInference").boolValue = true;
            behavior.ApplyModifiedPropertiesWithoutUndo();
            ValidateCounter();
            var runner = new GameObject("Temporary floor timing diagnostics").AddComponent<AIEpisodeBudgetRunner>();
            runner.agent = agent; runner.adapter = adapter; runner.metrics = metrics;
            runner.condition = "timing2000063"; runner.maximum = 12000;
            runner.modelLabel = label; runner.requestedEpisodes = 50; runner.regenerationInterval = 10;
            runner.rewardRevision = 1; runner.protocol = "phase3r-physical-floor-timing-level8-v1";
            runner.outputDirectory = output;
            SessionState.SetBool(Active, true);
            Debug.Log("FLOOR TIMING: 50 inference episodes; no trainer, no scene save, profile 1, 40x.");
            EditorApplication.EnterPlaymode();
        }
        private static void ValidateCounter()
        {
            var clock = new AIFloorTiming(); clock.Begin(0, .02);
            clock.Sample(100, 0, .1f, 0); clock.Sample(150, -1, .1f, 0);
            clock.Sample(200, 1, .1f, .01f); clock.Sample(200, 1, .1f, .02f);
            clock.Sample(250, 0, .12f, .02f); clock.Sample(300, 1, .12f, .03f);
            var r = clock.Finish(300, .12f, .03f);
            if (r.firstOtherFloorStep != 200 || r.floor0Steps != 150 || r.floor1Steps != 100
                || r.transitOrUnclassifiedSteps != 50 || Math.Abs(r.elapsedSeconds - 6) > 1e-6
                || Math.Abs(r.secondsAfterFirstTransfer - 2) > 1e-6
                || Math.Abs(r.floor1CoverageGainAfterTransfer - .02f) > 1e-6)
                throw new InvalidOperationException("Timing counter regression.");
            clock.Begin(1, .02); clock.Sample(20, 1, 0, .1f); r = clock.Finish(20, 0, .1f);
            if (r.firstOtherFloorStep != -1 || r.floor1Seconds != .4 || r.floor0CoverageGainAfterTransfer != -1)
                throw new InvalidOperationException("Never-transferred episode regression.");
            clock.Begin(1, .02); clock.Sample(20, 0, .01f, .1f); r = clock.Finish(21, .02f, .1f);
            if (r.firstOtherFloorIndex != 0 || r.firstOtherFloorStep != 20 || r.floor0Steps != 21)
                throw new InvalidOperationException("Descending or unsampled tail regression.");
            Debug.Log("FLOOR TIMING COUNTER CHECKS PASSED: step conservation, both directions, no transfer, immutable first snapshot, tail.");
        }
        private static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Active, false) || state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.delayCall += () =>
            {
                SessionState.SetBool(Active, false);
                string done = Path.Combine(SessionState.GetString(Output, ""), "timing2000063.done");
                EditorApplication.Exit(File.Exists(done) && File.ReadAllText(done) == "complete" ? 0 : 1);
            };
        }
        private static void Set(UnityEngine.Object target, string field, int value)
        { var s = new SerializedObject(target); s.FindProperty(field).intValue = value; s.ApplyModifiedPropertiesWithoutUndo(); }
    }
}
