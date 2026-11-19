using System;
using System.IO;
using Unity.InferenceEngine;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    [InitializeOnLoad]
    public static class AIModelEvaluationCommand
    {
        private const string ActiveKey = "TesisQA.Evaluation.Active";
        private const string IndexKey = "TesisQA.Evaluation.Index";
        private const string OutputKey = "TesisQA.Evaluation.Output";
        private const string ModelsKey = "TesisQA.Evaluation.Models";
        private const string EpisodesKey = "TesisQA.Evaluation.Episodes";
        private const string ShapingSmokeKey = "TesisQA.Evaluation.ShapingSmoke";
        private const string TraceKey = "TesisQA.Evaluation.Trace";
        private const string HeadlessActiveKey = "TesisQA.Headless.Active";
        private const string HeadlessDirectoryKey = "TesisQA.Headless.Directory";
        private const string DefaultModels = "Phase2M3_5499912,Phase2M3_6000112";
        private static string[] Models => SessionState.GetString(ModelsKey, DefaultModels).Split(',');

        static AIModelEvaluationCommand()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.update += CheckTrainingSmokeDeadline;
            EditorApplication.update += CheckHeadlessStop;
        }

        private static void CheckHeadlessStop()
        {
            if (!SessionState.GetBool(HeadlessActiveKey, false)) return;
            string directory = SessionState.GetString(HeadlessDirectoryKey, "");
            bool failed = File.Exists(Path.Combine(directory, "unity_failed.txt"));
            if (!failed && !File.Exists(Path.Combine(directory, "stop.request"))) return;
            // ML-Agents may leave Play Mode as soon as the Python communicator
            // closes. An Editor callback must still close this isolated process.
            SessionState.SetBool(HeadlessActiveKey, false);
            EditorApplication.Exit(failed ? 1 : 0);
        }

        public static void RunHeadlessTraining()
        {
            string isolatedRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            if (!Application.isBatchMode || Path.GetFileName(isolatedRoot) != ".ai-eval")
                throw new InvalidOperationException("Headless training requires the isolated .ai-eval batch project.");
            string statusPath = null;
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; i++)
                if (arguments[i] == "-aiHeadlessStatus") statusPath = Path.GetFullPath(arguments[i + 1]);
            string allowed = Path.Combine(Directory.GetParent(isolatedRoot).FullName, "results", "HeadlessRuns")
                + Path.DirectorySeparatorChar;
            if (statusPath == null || !statusPath.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Headless status must be inside results/HeadlessRuns.");
            AINavigationMotionChecks.Validate();
            AIStairPracticeChecks.Validate();
            AINearSearchChecks.Validate();
            AIMixedSearchChecks.Validate();
            SessionState.SetBool("TesisQA.TrainingSmoke", false);
            EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase2_SmallWFC.unity", OpenSceneMode.Single);
            var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
            var behavior = agent.GetComponent<BehaviorParameters>();
            Configure(agent, "useTerrainProfileSensor", WantsTerrainSensor());
            behavior.Model = null;
            behavior.BehaviorType = BehaviorType.Default;
            var runner = new GameObject("Temporary headless training").AddComponent<AIHeadlessTrainingRunner>();
            runner.agent = agent;
            runner.adapter = UnityEngine.Object.FindAnyObjectByType<AIWFCTrainingAdapter>();
            runner.metrics = UnityEngine.Object.FindAnyObjectByType<AIEpisodeMetrics>();
            runner.statusDirectory = statusPath;
            SessionState.SetBool(HeadlessActiveKey, true);
            SessionState.SetString(HeadlessDirectoryKey, statusPath);
            // Do not override MaxStep, motor, reward, sensors, collider or scene settings.
            EditorApplication.EnterPlaymode();
        }

        // Only for short trainer transport/checkpoint tests in an isolated project.
        public static void RunMasteryTrainingSmoke()
        {
            if (!Application.isBatchMode || !Path.GetFullPath(Application.dataPath).Contains(".ai-eval"))
                throw new InvalidOperationException("Training smoke requires the isolated .ai-eval project in batch mode.");
            AINavigationMotionChecks.Validate();
            AIStairPracticeChecks.Validate();
            AINearSearchChecks.Validate();
            AIMixedSearchChecks.Validate();
            EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase2_SmallWFC.unity", OpenSceneMode.Single);
            var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
            int episodeSteps = 200;
            string[] smokeArguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < smokeArguments.Length; i++)
                if (smokeArguments[i] == "-aiSmokeEpisodeSteps") episodeSteps = int.Parse(smokeArguments[i + 1]);
            agent.MaxStep = episodeSteps;
            Configure(agent, "twoFloorMaxStep", episodeSteps);
            Configure(agent, "useTerrainProfileSensor", WantsTerrainSensor());
            var behavior = agent.GetComponent<BehaviorParameters>();
            behavior.Model = null;
            behavior.BehaviorType = BehaviorType.Default;
            SessionState.SetBool("TesisQA.TrainingSmoke", true);
            SessionState.SetFloat("TesisQA.TrainingSmoke.Start", (float)EditorApplication.timeSinceStartup);
            EditorApplication.EnterPlaymode();
        }

        private static void CheckTrainingSmokeDeadline()
        {
            if (SessionState.GetBool("TesisQA.TrainingSmoke", false)
                && EditorApplication.timeSinceStartup - SessionState.GetFloat("TesisQA.TrainingSmoke.Start", 0f) > 75f)
            {
                SessionState.SetBool("TesisQA.TrainingSmoke", false);
                EditorApplication.Exit(0);
            }
        }

        // Unity -batchmode -nographics -projectPath <project> -executeMethod
        // TesisQA.AI.Editor.AIModelEvaluationCommand.RunComparison (without -quit).
        public static void RunComparison()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Run evaluation in a separate batch-mode project copy.");
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before starting evaluation.");
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string output = Path.Combine(root, "results", "Phase2M3", "evaluation_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
            string[] arguments = Environment.GetCommandLineArgs();
            string models = DefaultModels;
            int episodes = 50;
            bool shapingSmoke = false;
            bool trace = false;
            for (int i = 0; i + 1 < arguments.Length; i++)
            {
                if (arguments[i] == "-aiEvaluationOutput") output = Path.GetFullPath(arguments[i + 1]);
                if (arguments[i] == "-aiEvaluationModels") models = arguments[i + 1];
                if (arguments[i] == "-aiEvaluationEpisodes") episodes = int.Parse(arguments[i + 1]);
                if (arguments[i] == "-aiSearchShapingSmoke") shapingSmoke = bool.Parse(arguments[i + 1]);
                if (arguments[i] == "-aiEvaluationTrace") trace = bool.Parse(arguments[i + 1]);
            }
            AIStairSearchChecks.Validate();
            AIMixedSearchChecks.Validate();
            AIExplorationProgressChecks.Validate();
            AINavigationMotionChecks.Validate();
            AIStairPracticeChecks.Validate();
            AINearSearchChecks.Validate();
            Directory.CreateDirectory(output);
            SessionState.SetString(OutputKey, output);
            SessionState.SetString(ModelsKey, models);
            SessionState.SetInt(EpisodesKey, episodes);
            SessionState.SetBool(ShapingSmokeKey, shapingSmoke);
            SessionState.SetBool(TraceKey, trace);
            SessionState.SetInt(IndexKey, 0);
            SessionState.SetBool(ActiveKey, true);
            Debug.Log("AI EVALUATION output: " + output);
            StartModel();
        }

        private static void StartModel()
        {
            try
            {
                int index = SessionState.GetInt(IndexKey, 0);
                string label = Models[index];
                string path = "Assets/Scripts/AI/Evaluation/Models/" + label + ".onnx";
                EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase2_SmallWFC.unity", OpenSceneMode.Single);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(path);
                if (model == null) throw new InvalidOperationException("Cannot import model " + path);
                var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
                var adapter = UnityEngine.Object.FindAnyObjectByType<AIWFCTrainingAdapter>();
                var environment = UnityEngine.Object.FindAnyObjectByType<AITrainingEnvironment>();
                var metrics = UnityEngine.Object.FindAnyObjectByType<AIEpisodeMetrics>();
                if (agent == null || adapter == null || environment == null || metrics == null)
                    throw new InvalidOperationException("The Phase 2 scene is missing evaluation components.");
                // Wider transfer now follows the new near-spawn lesson.
                // Existing baseline evaluations keep their original input schema.
                Configure(agent, "useTerrainProfileSensor", WantsTerrainSensor());
                Configure(adapter, "defaultFloorLesson", 3);
                Configure(adapter, "defaultGraduatedTransferLessons", true);
                Configure(adapter, "regenerateEveryEpisodes", 10);
                Configure(adapter, "baseMapSeed", 913701);
                Configure(environment, "randomSeed", 27421);
                Configure(environment, "defaultStairSearchShaping", SessionState.GetBool(ShapingSmokeKey, false));
                var behavior = new SerializedObject(agent.GetComponent<BehaviorParameters>());
                behavior.FindProperty("m_Model").objectReferenceValue = model;
                behavior.FindProperty("m_BehaviorType").enumValueIndex = (int)BehaviorType.InferenceOnly;
                behavior.FindProperty("m_InferenceDevice").enumValueIndex = (int)InferenceDevice.Burst;
                behavior.FindProperty("m_DeterministicInference").boolValue = false;
                behavior.ApplyModifiedPropertiesWithoutUndo();
                var driver = new GameObject("Temporary model evaluation").AddComponent<AIModelEvaluationRunner>();
                driver.agent = agent;
                driver.adapter = adapter;
                driver.metrics = metrics;
                driver.modelLabel = label;
                driver.outputDirectory = SessionState.GetString(OutputKey, "");
                driver.episodeCount = SessionState.GetInt(EpisodesKey, 50);
                driver.captureTrace = SessionState.GetBool(TraceKey, false);
                Debug.Log("AI EVALUATION starting " + label);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Finish(1);
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(ActiveKey, false) || state != PlayModeStateChange.EnteredEditMode) return;
            EditorApplication.delayCall += ContinueComparison;
        }

        private static void ContinueComparison()
        {
            if (!SessionState.GetBool(ActiveKey, false)) return;
            int index = SessionState.GetInt(IndexKey, 0);
            string output = SessionState.GetString(OutputKey, "");
            string done = Path.Combine(output, Models[index] + ".done");
            if (!File.Exists(done) || File.ReadAllText(done) != "complete")
            {
                Debug.LogError("AI EVALUATION failed; inspect report in " + output);
                Finish(1);
                return;
            }
            if (++index < Models.Length)
            {
                SessionState.SetInt(IndexKey, index);
                StartModel();
                return;
            }
            Debug.Log("AI EVALUATION comparison complete: " + output);
            Finish(0);
        }

        private static void Finish(int exitCode)
        {
            SessionState.SetBool(ActiveKey, false);
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        private static bool WantsTerrainSensor()
        {
            return Array.IndexOf(Environment.GetCommandLineArgs(), "-aiTerrainSensor") >= 0;
        }

        private static void Configure(UnityEngine.Object target, string field, int value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).intValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Configure(UnityEngine.Object target, string field, bool value)
        {
            var data = new SerializedObject(target);
            data.FindProperty(field).boolValue = value;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
