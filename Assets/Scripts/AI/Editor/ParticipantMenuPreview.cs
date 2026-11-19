using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace TesisQA.AI.Editor
{
    public static class ParticipantMenuPreview
    {
        [MenuItem("Tesis QA/Defect Experiment/Update main menu from reference")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ParticipantGameBuild.ScenePath);
            var ui = UnityEngine.Object.FindAnyObjectByType<ParticipantCanvasUI>();
            ui.RebuildMainMenu();
            ui.RebuildTrialPause();
            ui.EnsureDefectClue();
            EditorSceneManager.MarkSceneDirty(ui.gameObject.scene);
            EditorSceneManager.SaveScene(ui.gameObject.scene);
        }

        public static void PrepareAndCapture()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use the isolated batch project for preview capture.");
            Prepare();
            string output = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-defectOutput") output = Path.GetFullPath(args[i + 1]);
            if (output == null) throw new InvalidOperationException("Preview output required.");
            Capture(output);
        }

        public static void Capture(string output)
        {
            Capture(output, false);
        }

        public static void CaptureResults(string output)
        {
            Capture(output, true);
        }

        private static void Capture(string output, bool resultsPreview)
        {
            Directory.CreateDirectory(output);
            var ui = UnityEngine.Object.FindAnyObjectByType<ParticipantCanvasUI>();
            var canvas = ui.GetComponent<Canvas>();
            ui.GetComponent<CanvasScaler>().enabled = false;
            canvas.renderMode = RenderMode.WorldSpace;
            var root = (RectTransform)ui.transform;
            root.position = Vector3.zero; root.rotation = Quaternion.identity; root.localScale = Vector3.one;
            foreach (Transform page in root) page.gameObject.SetActive(page.name == (resultsPreview ? "Five trial results" : "Main menu"));
            foreach (var child in root.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 30;
            var cameraObject = new GameObject("Menu preview camera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true; camera.cullingMask = 1 << 30;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(13, 22, 27, 255);
            camera.nearClipPlane = .1f; camera.farClipPlane = 20;
            foreach (var size in new[] { new Vector2Int(1306, 718), new Vector2Int(720, 1280) })
            foreach (bool showGuide in resultsPreview ? new[] { false } : new[] { false, true })
            {
                float scale = Mathf.Sqrt(size.x / 1920f * size.y / 1080f);
                root.sizeDelta = new Vector2(size.x / scale, size.y / scale);
                ui.ShowGuide(showGuide);
                for (int step = 0; step < 4; step++)
                {
                    ui.RefreshLayout(); Canvas.ForceUpdateCanvases();
                    foreach (var group in root.GetComponentsInChildren<LayoutGroup>())
                        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)group.transform);
                }
                camera.orthographicSize = root.rect.height / 2;
                var target = new RenderTexture(size.x, size.y, 24);
                camera.targetTexture = target;
                camera.Render();
                var previous = RenderTexture.active; RenderTexture.active = target;
                var texture = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(output, (resultsPreview ? "results-" : showGuide ? "defect-guide-" : "main-menu-") + size.x + "x" + size.y + ".png"), texture.EncodeToPNG());
                RenderTexture.active = previous; camera.targetTexture = null;
                UnityEngine.Object.DestroyImmediate(texture); target.Release(); UnityEngine.Object.DestroyImmediate(target);
            }
            Debug.Log("MAIN MENU PREVIEW SAVED: " + output);
        }
    }
}
