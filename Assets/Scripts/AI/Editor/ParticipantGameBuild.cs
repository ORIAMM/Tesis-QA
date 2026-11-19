using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    [InitializeOnLoad]
    public static class ParticipantGameBuild
    {
        public const string ScenePath = "Assets/Scenes/AI/QA_ParticipantGame.unity";

        static ParticipantGameBuild()
        {
            if (!Application.isBatchMode) EditorApplication.delayCall += OpenRequestedPreview;
        }

        [MenuItem("Tesis QA/Defect Experiment/Open participant game scene")]
        public static void OpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            { Debug.LogWarning("Stop Play Mode before opening the participant game scene."); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
        }

        [MenuItem("Tesis QA/Defect Experiment/Prepare participant Canvas")]
        public static void PrepareCanvas()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before preparing the Canvas.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            var session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
            if (session == null) throw new InvalidOperationException("Participant scene has no session.");
            ParticipantCanvasUI.Create(session).RebuildResults();
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            EditorSceneManager.SaveScene(session.gameObject.scene);
            Debug.Log("PARTICIPANT CANVAS PREPARED: " + ScenePath);
        }

        // One-time local request for the currently running Editor. Never discard
        // an unsaved scene or interrupt Play Mode to service a preview request.
        private static void OpenRequestedPreview()
        {
            const string request = "Temp/ColliderQuestPreview.request";
            if (!File.Exists(request)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += OpenRequestedPreview; return; }
            File.Delete(request);
            string status;
            bool dirty = false;
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                dirty |= UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty;
            if (dirty || EditorApplication.isPlayingOrWillChangePlaymode)
                status = "Pending: use Tesis QA > Defect Experiment > Open participant game scene after saving/stopping Play.";
            else
            {
                OpenScene();
                status = "Opened " + ScenePath + ". Press Play in the Game tab to preview the menu.";
            }
            Directory.CreateDirectory("results");
            File.WriteAllText("results/participant_editor_preview.txt", status);
            Debug.Log(status);
        }

        [MenuItem("Tesis QA/Defect Experiment/Build participant game")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before building.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(File.Exists(ScenePath) ? ScenePath : DefectExperimentSetup.ScenePath);
            var session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
            if (session == null || session.protocol == null) throw new InvalidOperationException("Evaluation scene/protocol unresolved.");
            session.participantPresentation = true;
            session.googleFormUrl = "https://forms.gle/D6iTTSbCAvPstjng9";
            session.mode = DefectExperimentMode.Manual;
            session.participant = "anonymous";
            session.participantOrder = 0;
            session.autoStart = session.batchRun = false;
            session.smokeDurationOverride = 0f;
            session.automaticTimeScale = 1f;
            session.outputDirectory = "";
            ParticipantCanvasUI.Create(session).RebuildResults();
            EditorUtility.SetDirty(session);
            if (!EditorSceneManager.SaveScene(session.gameObject.scene, ScenePath))
                throw new InvalidOperationException("Participant scene could not be saved.");
            PlayerSettings.productName = "ColliderQuest";
            PlayerSettings.companyName = "TesisQA";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "ColliderQuest", "ColliderQuest.exe"));
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-defectBuildOutput") output = Path.GetFullPath(args[i + 1]);
            if (File.Exists(output)) throw new InvalidOperationException("Build already exists. Use a new output path.");
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath },
                locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            if (result.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Participant build failed: " + result.summary.result);
            string folder = Path.GetDirectoryName(output);
            File.WriteAllText(Path.Combine(folder, "ParticipantSettings.json"),
                "{\n  \"googleFormUrl\": \"https://forms.gle/D6iTTSbCAvPstjng9\",\n  \"scenarioOrder\": 0\n}\n");
            File.WriteAllText(Path.Combine(folder, "Mulai_Game.cmd"),
                "@echo off\r\nstart \"\" \"%~dp0ColliderQuest.exe\" -force-d3d11 -screen-fullscreen 0\r\n");
            Debug.Log("PARTICIPANT GAME BUILT: " + output);
        }
    }
}
