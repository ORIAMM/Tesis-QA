using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class ParticipantHighlightChecks
    {
        public const string ResourceFolder = "Assets/Experiments/CollisionSelected/Resources";
        private const string MaterialPath = ResourceFolder + "/ParticipantAnomalyHighlight.mat";

        public static void Run()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use the isolated batch project.");
            string output = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-defectOutput") output = Path.GetFullPath(args[i + 1]);
            if (output == null) throw new InvalidOperationException("Check output required.");
            PrepareMaterial();
            ValidateAndCapture(output + "-preview");
            ParticipantGameChecks.Run();
        }

        public static void PrepareMaterial()
        {
            if (AssetDatabase.LoadAssetAtPath<Material>(MaterialPath) != null) return;
            Directory.CreateDirectory(ResourceFolder); AssetDatabase.Refresh();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader is unavailable.");
            var material = new Material(shader) { name = ParticipantAnomalyHighlights.MaterialResource };
            material.SetColor("_BaseColor", new Color(.35f, .94f, .76f));
            material.SetFloat("_Cull", 0);
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        }

        private static void ValidateAndCapture(string output)
        {
            Directory.CreateDirectory(output);
            using (var highlights = new ParticipantAnomalyHighlights())
            {
                highlights.Sync(new List<DefectFinding>(), true);
                Check(highlights.Count == 0 && highlights.Root == null, "Unfound anomalies were highlighted.");
                var findings = new List<DefectFinding> {
                    new DefectFinding { position = new Vector3(-1.1f, 0, 0), normal = Vector3.up, surfaceKind = "floor" },
                    new DefectFinding { position = new Vector3(1.1f, 1.3f, 2), normal = Vector3.back, surfaceKind = "wall" }
                };
                Func<DefectFinding, Vector3[]> outline = finding => finding.surfaceKind == "floor"
                    ? new[] { new Vector3(-2.3f, .045f, -1.2f), new Vector3(.1f, .045f, -1.2f),
                        new Vector3(.1f, .045f, 1.2f), new Vector3(-2.3f, .045f, 1.2f) }
                    : new[] { new Vector3(-.5f, .2f, 1.955f), new Vector3(2.7f, .2f, 1.955f),
                        new Vector3(2.7f, 2.6f, 1.955f), new Vector3(-.5f, 2.6f, 1.955f) };
                highlights.Sync(findings, true, outline); highlights.Sync(findings, true, outline);
                Check(highlights.Count == 2, "Repeated refresh duplicated world markers.");
                foreach (var line in highlights.Root.GetComponentsInChildren<LineRenderer>())
                    Check(line.loop && line.positionCount == 4, "Highlight is not an outside rectangular outline.");
                Check(Mathf.Abs(Vector3.Dot(highlights.Root.GetChild(0).forward, Vector3.up)) > .999f
                    && Mathf.Abs(Vector3.Dot(highlights.Root.GetChild(1).forward, Vector3.back)) > .999f,
                    "Highlight planes do not follow the accepted floor/wall normals.");
                Check(highlights.Root.GetComponentsInChildren<Collider>().Length == 0, "Highlights changed collision.");
                highlights.Sync(findings, false);
                Check(!highlights.Root.gameObject.activeSelf, "Hidden HUD left world highlights visible.");
                highlights.Sync(findings, true);
                Capture(highlights.Root, output);
                highlights.Sync(new List<DefectFinding>(), true);
                Check(highlights.Count == 0 && highlights.Root == null, "New trial kept old highlights.");
                File.WriteAllText(Path.Combine(output, "world-highlight-checks.txt"),
                    "PASS: four-corner outside outlines on floor/wall; no highlights before findings; duplicate-free refresh; visibility toggles; reset on new trial; no added colliders. Resource material and shader referenced for standalone builds.\n");
            }
        }

        private static void Capture(Transform highlights, string output)
        {
            var fixture = new GameObject("Highlight render fixture");
            var floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            floorMaterial.SetColor("_BaseColor", new Color(.20f, .25f, .30f));
            RenderTexture target = null; Texture2D texture = null;
            try
            {
                highlights.SetParent(fixture.transform, false);
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.SetParent(fixture.transform); floor.transform.position = new Vector3(0, -.5f, 0);
                floor.transform.localScale = new Vector3(8, 1, 8); floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.SetParent(fixture.transform); wall.transform.position = new Vector3(0, 1.5f, 2.1f);
                wall.transform.localScale = new Vector3(8, 3, .2f); wall.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                foreach (var child in fixture.GetComponentsInChildren<Transform>()) child.gameObject.layer = 30;
                var cameraObject = new GameObject("Highlight preview camera", typeof(Camera));
                cameraObject.transform.SetParent(fixture.transform);
                var camera = cameraObject.GetComponent<Camera>();
                camera.transform.position = new Vector3(3.4f, 3.2f, -5.8f);
                camera.transform.LookAt(new Vector3(0, .7f, .5f));
                camera.cullingMask = 1 << 30; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.04f, .065f, .08f); camera.fieldOfView = 46;
                target = new RenderTexture(1280, 720, 24); camera.targetTexture = target;
                camera.Render();
                var previous = RenderTexture.active; RenderTexture.active = target;
                texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(output, "floor-wall-highlights.png"), texture.EncodeToPNG());
                RenderTexture.active = previous; camera.targetTexture = null;
                highlights.SetParent(null, true);
            }
            finally
            {
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                UnityEngine.Object.DestroyImmediate(fixture); UnityEngine.Object.DestroyImmediate(floorMaterial);
            }
        }

        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
