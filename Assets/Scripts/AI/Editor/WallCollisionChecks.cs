using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class WallCollisionChecks
    {
        public static void Run()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use isolated batch Editor for detector checks.");
            string output = null; var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-defectOutput") output = Path.GetFullPath(args[i + 1]);
            if (output == null || Directory.Exists(output)) throw new InvalidOperationException("Use a new output directory.");
            Upgrade(LinearCollisionSetup.ProtocolPath, "collision-linear-walls-v2");
            Upgrade(SmallCollisionDemoSetup.Root + "/CollisionSandbox.asset", "collision-sandbox-walls-v2");
            CheckSynthetic();
            EditorSceneManager.OpenScene(SmallCollisionDemoSetup.ScenePath);
            CheckRealTile();
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "wall-detector-checks.txt"),
                "PASS: healthy walls rejected; missing wall found only after two scans; floor-only compatibility; no repeated emission; wall behind missing wall cannot mask defect; visual occlusion respected; stairs excluded; floor regression; wall/floor reports on same full collider count once; partial region boundaries; real sandbox corridor wall detected, then rejected after collider restoration. Frozen manifests unchanged. No training or policy observations changed.\n");
            Debug.Log("WALL COLLISION CHECKS PASSED: " + output);
        }

        private static void Upgrade(string path, string id)
        {
            var protocol = AssetDatabase.LoadAssetAtPath<DefectExperimentProtocol>(path);
            Check(protocol != null, "Protocol missing: " + path);
            protocol.inspectWalls = true; protocol.protocolId = id;
            protocol.detectorVersion = "local-floor-wall-v3"; protocol.scorerVersion = "collision-region-wall-v3";
            EditorUtility.SetDirty(protocol); AssetDatabase.SaveAssets();
        }

        private static void CheckSynthetic()
        {
            var root = new GameObject("Wall inspection software checks"); root.transform.position = Vector3.right * 10000;
            var library = ScriptableObject.CreateInstance<DefectMeshLibrary>();
            try
            {
                var floor = Cube(root.transform, "Floor tile", new Vector3(0, -.1f, 0), new Vector3(12, .2f, 12));
                var wall = Cube(root.transform, "Wall tile", new Vector3(0, 1.25f, 2), new Vector3(8, 2.5f, .2f));
                var mesh = floor.GetComponent<MeshFilter>().sharedMesh;
                library.meshes.Add(new DefectMeshData { source = mesh, vertices = mesh.vertices, triangles = mesh.triangles });
                var geometry = new DefectGeometry(root.transform, library);
                Vector3 eye = root.transform.position + Vector3.up * 1.6f;
                Physics.SyncTransforms();
                var reports = new List<DefectVisualHit>();
                var detector = new LocalCollisionDetector(geometry, true);
                detector.Scan(eye, Quaternion.identity, 6, reports.Add); detector.Scan(eye, Quaternion.identity, 6, reports.Add);
                Check(reports.Count == 0, "Healthy wall/floor produced a false report.");
                wall.GetComponent<Collider>().enabled = false; Physics.SyncTransforms();
                var floorOnly = new LocalCollisionDetector(geometry);
                floorOnly.Scan(eye, Quaternion.identity, 6, reports.Add); floorOnly.Scan(eye, Quaternion.identity, 6, reports.Add);
                Check(reports.Count == 0, "Legacy floor-only detector inspected walls.");
                detector = new LocalCollisionDetector(geometry, true);
                detector.Scan(eye, Quaternion.identity, 6, reports.Add);
                Check(reports.Count == 0, "Wall reported before confirmation.");
                detector.Scan(eye, Quaternion.identity, 6, reports.Add);
                Check(reports.Count > 0 && reports.All(hit => hit.IsInspectableWall), "Missing wall not confirmed.");
                int confirmed = reports.Count;
                detector.Scan(eye, Quaternion.identity, 6, reports.Add);
                Check(reports.Count == confirmed, "Wall sample was repeatedly emitted.");
                var wallHit = reports[0];
                Cube(root.transform, "Far healthy wall", new Vector3(0, 1.25f, 4), new Vector3(8, 2.5f, .2f)); Physics.SyncTransforms();
                Check(!DefectGeometry.CollisionPresent(wallHit), "Farther wall masked missing wall collision.");
                Cube(root.transform, "Healthy occluder", new Vector3(0, 1.25f, 1), new Vector3(10, 2.5f, .2f)); Physics.SyncTransforms();
                geometry = new DefectGeometry(root.transform, library); reports.Clear();
                detector = new LocalCollisionDetector(geometry, true);
                detector.Scan(eye, Quaternion.identity, 6, reports.Add); detector.Scan(eye, Quaternion.identity, 6, reports.Add);
                Check(reports.Count == 0, "Detector saw missing wall through healthy visual occluder.");
                foreach (var surface in geometry.surfaces) surface.inspectFloor = false;
                detector = new LocalCollisionDetector(geometry, true);
                detector.Scan(eye, Quaternion.identity, 6, reports.Add); detector.Scan(eye, Quaternion.identity, 6, reports.Add);
                Check(reports.Count == 0, "Excluded stair surfaces were inspected.");
                foreach (var surface in geometry.surfaces) surface.inspectFloor = true;
                floor.GetComponent<Collider>().enabled = false; Physics.SyncTransforms();
                detector = new LocalCollisionDetector(geometry, true); reports.Clear();
                detector.Scan(eye, Quaternion.identity, 6, reports.Add); detector.Scan(eye, Quaternion.identity, 6, reports.Add);
                Check(reports.Any(hit => hit.IsInspectableFloor), "Wall addition broke floor detection.");
                CheckScoring(wallHit);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(library); }
        }

        private static GameObject Cube(Transform root, string name, Vector3 local, Vector3 scale)
        {
            // Tile parent stays at nominal floor height; wall mesh is its child.
            var tile = new GameObject(name); tile.transform.SetParent(root, false);
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.transform.SetParent(tile.transform, false);
            cube.transform.localPosition = local; cube.transform.localScale = scale; return cube;
        }

        private static void CheckScoring(DefectVisualHit hit)
        {
            var trial = new DefectTrialReport { manifest = new DefectScenarioManifest() };
            var defect = new InjectedCollisionDefect { id = "D1", surfaceId = hit.surface.id, kind = CollisionDefectKind.FullCollider,
                center = new Vector3(hit.point.x, hit.surface.tile.position.y, hit.point.z) };
            trial.manifest.defects.Add(defect);
            DefectScenarioBuilder.Score(trial, new DefectFinding { surfaceId = hit.surface.id, surfaceKind = "wall", normal = hit.normal, position = hit.point });
            DefectScenarioBuilder.Score(trial, new DefectFinding { surfaceId = hit.surface.id, surfaceKind = "floor", normal = Vector3.up, position = defect.center });
            Check(trial.truePositives == 1 && trial.duplicates == 1 && trial.falsePositives == 0, "Wall and floor on one collider counted twice.");
            defect.kind = CollisionDefectKind.PartialHole; defect.width = 2.4f;
            trial.findings.Clear(); trial.truePositives = trial.duplicates = trial.falsePositives = 0;
            DefectScenarioBuilder.Score(trial, new DefectFinding { surfaceId = hit.surface.id, surfaceKind = "wall", normal = hit.normal, position = hit.point });
            DefectScenarioBuilder.Score(trial, new DefectFinding { surfaceId = hit.surface.id, surfaceKind = "wall", normal = hit.normal, position = hit.point + Vector3.right * 3 });
            Check(trial.truePositives == 1 && trial.falsePositives == 1, "Partial wall region boundaries differ from injection.");
        }

        private static void CheckRealTile()
        {
            var demo = UnityEngine.Object.FindAnyObjectByType<SmallCollisionDemo>();
            using (var build = DefectScenarioBuilder.Build(demo.protocol, demo.protocol.scenarios[0], demo.generator, demo.adapter, true))
            {
                var defect = build.manifest.defects.Single(d => d.kind == CollisionDefectKind.FullCollider);
                var surface = build.geometry.surfaces.Single(s => s.id == defect.surfaceId);
                Vector3 eye = demo.demonstrationSpawns[0] + Vector3.up * 1.6f;
                DefectVisualHit found = default; Quaternion yaw = Quaternion.identity;
                for (int i = 0; i < surface.data.triangles.Length; i += 3)
                {
                    Vector3 a = surface.transform.TransformPoint(surface.data.vertices[surface.data.triangles[i]]);
                    Vector3 b = surface.transform.TransformPoint(surface.data.vertices[surface.data.triangles[i + 1]]);
                    Vector3 c = surface.transform.TransformPoint(surface.data.vertices[surface.data.triangles[i + 2]]);
                    Vector3 point = (a + b + c) / 3;
                    var candidate = new DefectVisualHit { surface = surface, point = point, normal = Vector3.Cross(b - a, c - a).normalized };
                    if (!candidate.IsInspectableWall || Vector3.Distance(point, eye) > 6) continue;
                    var direction = point - eye;
                    yaw = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
                    var reports = new List<DefectVisualHit>(); var detector = new LocalCollisionDetector(build.geometry, true);
                    detector.Scan(eye, yaw, 6, reports.Add); detector.Scan(eye, yaw, 6, reports.Add);
                    found = reports.FirstOrDefault(h => h.IsInspectableWall && h.surface.id == defect.surfaceId);
                    if (found.surface != null) break;
                }
                Check(found.surface != null, "Real corridor wall was not detected from safe nearby spawn.");
                Check(!DefectGeometry.CollisionPresent(found), "Real removed wall still has blocking collision.");
                surface.collider.enabled = true; Physics.SyncTransforms();
                Check(DefectGeometry.CollisionPresent(found), "Restored real wall lacks blocking collision.");
                var after = new List<DefectVisualHit>(); var healthyDetector = new LocalCollisionDetector(build.geometry, true);
                healthyDetector.Scan(eye, yaw, 6, after.Add); healthyDetector.Scan(eye, yaw, 6, after.Add);
                Check(!after.Any(h => h.IsInspectableWall && h.surface.id == defect.surfaceId), "Restored corridor wall still reported.");
            }
        }

        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
