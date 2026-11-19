using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using TesisQA.WFC;
using Unity.Collections;
using Unity.InferenceEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    [InitializeOnLoad]
    public static class DefectExperimentSetup
    {
        public const string Root = "Assets/Experiments/CollisionPilot";
        public const string ScenePath = "Assets/Scenes/AI/QA_CollisionExperiment.unity";
        public const string ProtocolPath = Root + "/CollisionPilot.asset";
        public const string ModelSource = "results/Phase3R/Explorer/Explorer-2000063.onnx";
        private const string ActiveKey = "DefectExperiment.Batch.Active";
        private const string OutputKey = "DefectExperiment.Batch.Output";
        private const string ManualKey = "DefectExperiment.Batch.ManualCheck";
        static DefectExperimentSetup()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update += CheckManualWorkflow;
        }

        [MenuItem("Tesis QA/Defect Experiment/Open evaluation scene")]
        public static void Open()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (!File.Exists(ScenePath)) BuildAndValidate();
            EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("Tesis QA/Defect Experiment/Prepare and validate pilot")]
        public static void BuildAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before preparing evaluation assets.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            CommunicatorFactory.Enabled = false;
            Directory.CreateDirectory(Root + "/Manifests");
            string modelPath = Root + "/Phase3R_Explorer_2000063.onnx";
            string source = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), ModelSource);
            if (!File.Exists(source)) throw new FileNotFoundException("Pinned Phase3R model is missing.", source);
            string sourceHash = FileHash(source);
            if (File.Exists(modelPath) && FileHash(modelPath) != sourceHash)
                throw new InvalidOperationException("Pinned evaluation model differs; refusing overwrite.");
            if (!File.Exists(modelPath)) File.Copy(source, modelPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var model = AssetDatabase.LoadAssetAtPath<ModelAsset>(modelPath);
            if (model == null) throw new InvalidOperationException("Phase3R ONNX import failed.");

            if (File.Exists(ProtocolPath))
            {
                if (!File.Exists(ScenePath)) throw new InvalidOperationException("Protocol exists without its scene; preserve it and create a versioned replacement.");
                var existing = AssetDatabase.LoadAssetAtPath<DefectExperimentProtocol>(ProtocolPath);
                if (existing == null) throw new InvalidOperationException("Protocol script reference is unresolved. Restore component .meta GUIDs; do not overwrite the frozen asset.");
                EditorUtility.SetDirty(existing); AssetDatabase.SaveAssets();
                ValidateFrozen(); return;
            }

            EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity");
            var generator = UnityEngine.Object.FindAnyObjectByType<WFCGenerator>();
            var adapter = UnityEngine.Object.FindAnyObjectByType<AIWFCTrainingAdapter>();
            var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
            var environment = UnityEngine.Object.FindAnyObjectByType<AITrainingEnvironment>();
            generator.ClearGenerated();
            Set(generator, "generateOnStart", false);
            var library = BakeLibrary(generator);
            AssetDatabase.CreateAsset(library, Root + "/VisualGeometry.asset");
            var protocol = ScriptableObject.CreateInstance<DefectExperimentProtocol>();
            protocol.model = model; protocol.modelSource = ModelSource; protocol.modelSha256 = sourceHash; protocol.meshLibrary = library;
            protocol.scenarios.AddRange(new[] {
                Scenario("S1", 170000001, 0, 0, 28320), Scenario("S2", 170004100, 2, 0, 28321),
                Scenario("S3", 170008199, 0, 2, 28322), Scenario("S4", 170012298, 2, 2, 28323),
                Scenario("S5", 170016397, 2, 2, 28324) });
            foreach (var scenario in protocol.scenarios)
            {
                if ((scenario.mapSeed - 33003) % 7919 == 0)
                    throw new InvalidOperationException("Evaluation seed overlaps the Phase3R training seed sequence.");
                using (var build = DefectScenarioBuilder.Build(protocol, scenario, generator, adapter, false))
                {
                    scenario.frozenManifest = JsonUtility.ToJson(build.manifest);
                    File.WriteAllText(Root + "/Manifests/" + scenario.id + ".json", JsonUtility.ToJson(build.manifest, true));
                }
                Debug.Log("DEFECT SCENARIO BAKED " + scenario.id);
            }
            generator.ClearGenerated();
            AssetDatabase.CreateAsset(protocol, ProtocolPath);
            Set(environment, "phase3RewardRevision", 1); // Match Phase3R, regardless of current Phase3S training scene.
            Set(environment, "defaultStairSearchShaping", false);
            Set(adapter, "useFloorCountCurriculum", true); Set(adapter, "useFloorTransferLesson", true);
            Set(agent, "useTerrainProfileSensor", true); Set(agent, "useBranchMemorySensor", true);
            Set(agent, "useDistantTerrainSensor", true); Set(agent, "useRampMemorySensor", true);
            Set(agent, "useLongRangeTerrainProfile", false); Set(agent, "useCompletedBranchAssist", false);
            var behavior = agent.GetComponent<BehaviorParameters>();
            behavior.Model = model; behavior.InferenceDevice = InferenceDevice.Burst;
            behavior.BehaviorType = BehaviorType.HeuristicOnly; behavior.DeterministicInference = false;
            agent.MaxStep = 0;
            var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            camera.orthographic = false; camera.fieldOfView = 75f; camera.nearClipPlane = .05f; camera.farClipPlane = 250f;
            var cameraGO = camera.gameObject;
            foreach (var listener in UnityEngine.Object.FindObjectsByType<AudioListener>())
                if (listener.gameObject != cameraGO) UnityEngine.Object.DestroyImmediate(listener);
            if (camera.GetComponent<AudioListener>() == null) camera.gameObject.AddComponent<AudioListener>();
            Transform visual = agent.transform.Find("Agent Visual");
            if (visual != null) foreach (var renderer in visual.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            if (environment.TargetTransform != null)
            {
                environment.TargetTransform.gameObject.layer = 2;
                foreach (var renderer in environment.TargetTransform.GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            }
            var session = new GameObject("Collision experiment controller").AddComponent<DefectExperimentSession>();
            session.protocol = protocol; session.agent = agent; session.environment = environment;
            session.adapter = adapter; session.generator = generator; session.viewCamera = camera;
            session.mode = DefectExperimentMode.Manual;
            EditorSceneManager.SaveScene(generator.gameObject.scene, ScenePath);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateFrozen();
        }

        private static DefectScenario Scenario(string id, int seed, int full, int partial, int spawnSeed) => new DefectScenario {
            id = id, mapSeed = seed, defectSeed = seed ^ 0x53172, spawnSeed = spawnSeed, fullDefects = full, partialDefects = partial };

        private static DefectMeshLibrary BakeLibrary(WFCGenerator generator)
        {
            var library = ScriptableObject.CreateInstance<DefectMeshLibrary>();
            var tileSet = new SerializedObject(generator).FindProperty("tileSet").objectReferenceValue as WFCTileSet;
            var meshes = new HashSet<Mesh>();
            foreach (var tile in tileSet.tiles)
                if (tile.prefab != null)
                    foreach (var filter in tile.prefab.GetComponentsInChildren<MeshFilter>(true))
                        if (filter.sharedMesh != null) meshes.Add(filter.sharedMesh);
            foreach (Mesh mesh in meshes.OrderBy(m => m.name))
            {
                using (var array = UnityEditor.MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    var data = array[0];
                    using (var vertices = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp))
                    {
                        data.GetVertices(vertices);
                        var triangles = new List<int>();
                        for (int sub = 0; sub < data.subMeshCount; sub++)
                        {
                            var descriptor = data.GetSubMesh(sub);
                            if (descriptor.topology != MeshTopology.Triangles) continue;
                            using (var indices = new NativeArray<int>(descriptor.indexCount, Allocator.Temp))
                            { data.GetIndices(indices, sub); triangles.AddRange(indices.ToArray()); }
                        }
                        Vector3[] v = vertices.ToArray(); int[] t = triangles.ToArray();
                        string digest;
                        using (var buffer = new MemoryStream())
                        using (var writer = new BinaryWriter(buffer))
                        {
                            foreach (var p in v) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                            foreach (int index in t) writer.Write(index);
                            using (var hash = SHA256.Create()) digest = BitConverter.ToString(hash.ComputeHash(buffer.ToArray())).Replace("-", "").ToLowerInvariant();
                        }
                        string assetPath = AssetDatabase.GetAssetPath(mesh);
                        library.meshes.Add(new DefectMeshData { source = mesh, vertices = v, triangles = t, sha256 = digest,
                            sourceAssetPath = assetPath, sourceDependencyHash = AssetDatabase.GetAssetDependencyHash(assetPath).ToString() });
                    }
                }
            }
            return library;
        }

        [MenuItem("Tesis QA/Defect Experiment/Validate frozen scenarios")]
        public static void ValidateFrozen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ScenePath);
            var session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
            var protocol = session.protocol;
            if (FileHash(Root + "/Phase3R_Explorer_2000063.onnx") != protocol.modelSha256)
                throw new InvalidOperationException("Frozen model hash changed.");
            foreach (var mesh in protocol.meshLibrary.meshes)
                Check(AssetDatabase.GetAssetDependencyHash(mesh.sourceAssetPath).ToString() == mesh.sourceDependencyHash,
                    "Source mesh dependency changed: " + mesh.sourceAssetPath);
            int validated = 0;
            foreach (var scenario in protocol.scenarios)
            {
                using (var build = DefectScenarioBuilder.Build(protocol, scenario, session.generator, session.adapter, true))
                {
                    Check(build.manifest.defects.Count == scenario.fullDefects + scenario.partialDefects, "Wrong defect count.");
                    foreach (var defect in build.manifest.defects)
                    {
                        Check(!DefectGeometry.Supported(defect.center), "Injected defect was healed.");
                        Check(build.geometry.Cast(new Ray(defect.center + Vector3.up, Vector3.down), 1.2f, out var hit)
                            && hit.surface.id == defect.surfaceId, "Visual surface disappeared.");
                    }
                    // Frozen clean centers must never report mismatches.
                    foreach (var center in build.coverageCells.Values)
                        if (!build.manifest.defects.Any(d => Vector3.Distance(d.center, center) < 1f))
                            Check(DefectGeometry.Supported(center), "Unexpected healthy-center mismatch.");
                    validated++;
                }
                session.generator.ClearGenerated();
            }
            ValidateSyntheticGeometry();
            for (int index = 0; index < 5; index++)
                Check(Enumerable.Range(0, 10).Count(order => protocol.ScenarioIndex(0, order) == index) == 2,
                    "Participant orders are not balanced.");
            string output = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "results", "DefectExperiments", "validation");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "validation.txt"), "PASS\nscenarios=" + validated
                + "\nmodel_sha256=" + protocol.modelSha256
                + "\nChecks: frozen replay, verified full/partial holes, preserved visuals, healthy centers, lower-floor rejection, clipping, detector confirmations, occlusion, scoring, counterbalanced orders.\n");
            Debug.Log("DEFECT EXPERIMENT VALIDATION PASSED: five frozen scenarios and geometry/scoring checks.");
            // The evaluation scene stays empty in Edit Mode and builds its frozen map on Play.
            EditorSceneManager.OpenScene(ScenePath);
        }

        private static void ValidateSyntheticGeometry()
        {
            var root = new GameObject("Synthetic defect checks"); root.transform.position = new Vector3(-10000f, 0f, -10000f);
            Mesh cut = null;
            try
            {
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name = "Floor";
                floor.transform.SetParent(root.transform, false); floor.transform.localScale = new Vector3(8f, .2f, 8f);
                UnityEngine.Object.DestroyImmediate(floor.GetComponent<BoxCollider>());
                var source = floor.GetComponent<MeshFilter>().sharedMesh;
                var collider = floor.AddComponent<MeshCollider>(); collider.sharedMesh = source;
                var library = ScriptableObject.CreateInstance<DefectMeshLibrary>();
                library.meshes.Add(new DefectMeshData { source = source, vertices = source.vertices, triangles = source.triangles });
                var geometry = new DefectGeometry(root.transform, library);
                Physics.SyncTransforms();
                Vector3 center = floor.transform.position + Vector3.up * .1f;
                Check(DefectGeometry.Supported(center), "Clean synthetic floor missing.");
                Check(geometry.Cast(new Ray(center + Vector3.up, Vector3.down), 2f, out var hit), "Visual ray missed floor.");
                var visual = hit.surface;
                cut = DefectGeometry.CutHole(visual, center, 2.4f); collider.sharedMesh = cut;
                Physics.SyncTransforms();
                Check(!DefectGeometry.Supported(center) && DefectGeometry.Supported(center + Vector3.right * 2f), "Partial clipping failed.");
                var detector = new LocalCollisionDetector(geometry); int found = 0;
                detector.Scan(center + Vector3.up * 1.6f, Quaternion.identity, 6f, _ => found++);
                Check(found == 0, "Detector reported on its first scan.");
                detector.Scan(center + Vector3.up * 1.6f, Quaternion.identity, 6f, _ => found++);
                Check(found > 0, "Detector failed to confirm missing collision.");
                visual.inspectFloor = false;
                detector = new LocalCollisionDetector(geometry); int excludedStairReports = 0;
                for (int scan = 0; scan < 2; scan++)
                    detector.Scan(center + Vector3.up * 1.6f, Quaternion.identity, 6f, _ => excludedStairReports++);
                Check(excludedStairReports == 0, "Out-of-scope stair treads were inspected.");
                visual.inspectFloor = true;
                var lower = GameObject.CreatePrimitive(PrimitiveType.Cube); lower.transform.SetParent(root.transform, false);
                lower.transform.localPosition = Vector3.down * 4f; lower.transform.localScale = new Vector3(8f, .2f, 8f);
                Physics.SyncTransforms(); Check(!DefectGeometry.Supported(center), "Lower floor masked missing upper floor.");
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube); wall.name = "Wall";
                wall.transform.SetParent(root.transform, false); wall.transform.localPosition = new Vector3(0f, 1f, -1f);
                wall.transform.localScale = new Vector3(8f, 3f, .2f);
                geometry = new DefectGeometry(root.transform, library);
                Check(geometry.Cast(new Ray(center + new Vector3(0f, 1f, -3f), Vector3.forward), 6f, out var wallHit)
                    && wallHit.surface.transform == wall.transform, "Visual wall did not occlude geometry behind it.");
                var trial = new DefectTrialReport { manifest = new DefectScenarioManifest() };
                trial.manifest.defects.Add(new InjectedCollisionDefect { id = "D1", surfaceId = visual.id,
                    kind = CollisionDefectKind.PartialHole, center = center, width = 2.4f });
                foreach (Vector3 position in new[] { center, center, center + Vector3.right * 3f })
                    DefectScenarioBuilder.Score(trial, new DefectFinding { surfaceId = visual.id, position = position });
                Check(trial.truePositives == 1 && trial.duplicates == 1 && trial.falsePositives == 1, "Scoring leaked/double-counted labels.");
                trial.findings.Clear(); trial.truePositives = trial.falsePositives = trial.duplicates = 0;
                foreach (float offset in new[] { 1.1f, 1.3f })
                    DefectScenarioBuilder.Score(trial, new DefectFinding { surfaceId = visual.id, position = center + Vector3.right * offset });
                Check(trial.truePositives == 1 && trial.falsePositives == 1 && trial.duplicates == 0,
                    "An unmatched report near a true positive was incorrectly discarded as a duplicate.");
                trial.findings.Clear(); trial.truePositives = trial.falsePositives = trial.duplicates = 0;
                trial.manifest.defects[0].kind = CollisionDefectKind.FullCollider;
                DefectScenarioBuilder.Score(trial, new DefectFinding { surfaceId = visual.id, position = center + Vector3.up * .2f });
                Check(trial.truePositives == 1 && trial.falsePositives == 0, "Scorer floor-height band differs from detector scope.");
                UnityEngine.Object.DestroyImmediate(library);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); if (cut != null) UnityEngine.Object.DestroyImmediate(cut); }
        }

        // Example: Unity -batchmode -projectPath <root> -executeMethod
        // TesisQA.AI.Editor.DefectExperimentSetup.RunAutomatic -defectMode AI
        // -defectOutput <NEW directory> [-defectSmokeSeconds 4] (without -quit).
        public static void RunAutomatic()
        {
            RunAutomaticScene(ScenePath);
        }

        public static void RunLinearAutomatic()
        {
            RunAutomaticScene(LinearCollisionSetup.EvaluationScene);
        }

        private static void RunAutomaticScene(string scenePath)
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use batch mode for automatic campaigns.");
            string output = null, mode = "AI"; float smoke = 0f;
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-defectOutput") output = Path.GetFullPath(args[i + 1]);
                if (args[i] == "-defectMode") mode = args[i + 1];
                if (args[i] == "-defectSmokeSeconds") smoke = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            }
            if (output == null || Directory.Exists(output)) throw new InvalidOperationException("Use a NEW output directory.");
            if (!Enum.TryParse(mode, out DefectExperimentMode selected) || selected == DefectExperimentMode.Manual)
                throw new InvalidOperationException("Automatic mode must be AI or Random.");
            CommunicatorFactory.Enabled = false;
            EditorSceneManager.OpenScene(scenePath);
            var session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
            session.mode = selected; session.participant = selected.ToString();
            session.autoStart = session.batchRun = true; session.outputDirectory = output;
            session.automaticTimeScale = 20f; session.smokeDurationOverride = smoke;
            SessionState.SetString(OutputKey, output); SessionState.SetBool(ActiveKey, true);
            EditorApplication.EnterPlaymode();
        }

        public static void RunManualSmoke()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use batch mode for software checks.");
            string output = null; string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-defectOutput") output = Path.GetFullPath(args[i + 1]);
            if (output == null || Directory.Exists(output)) throw new InvalidOperationException("Use a NEW software-check output directory.");
            CommunicatorFactory.Enabled = false;
            EditorSceneManager.OpenScene(ScenePath);
            var session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
            session.mode = DefectExperimentMode.Manual; session.participant = "SOFTWARE_CHECK_NOT_A_PARTICIPANT";
            session.participantOrder = 1; session.autoStart = session.batchRun = true;
            session.outputDirectory = output; session.smokeDurationOverride = 4f;
            SessionState.SetString(OutputKey, output); SessionState.SetBool(ActiveKey, true); SessionState.SetBool(ManualKey, true);
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Tesis QA/Defect Experiment/Build Windows testing player")]
        public static void BuildWindowsPlayer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play before building.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "CollisionPilot", "CollisionPilot.exe"));
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-defectBuildOutput") output = Path.GetFullPath(args[i + 1]);
            if (File.Exists(output)) throw new InvalidOperationException("Build already exists; use a new output path.");
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { ScenePath },
                locationPathName = output, target = BuildTarget.StandaloneWindows64, options = BuildOptions.None });
            if (build.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException("Windows testing player build failed: " + build.summary.result);
            Debug.Log("DEFECT WINDOWS PLAYER BUILT: " + output);
        }

        private static void CheckManualWorkflow()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(ManualKey, false)) return;
            var session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
            if (session == null || !session.Running) return;
            var field = typeof(DefectExperimentSession).GetField("current", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var trial = field.GetValue(session) as DefectTrialReport;
            if (trial == null) return;
            try
            {
                Vector3 point = trial.manifest.defects[0].center;
                session.viewCamera.transform.SetPositionAndRotation(point + Vector3.up, Quaternion.LookRotation(Vector3.down));
                session.ReportAim(); session.ReportAim();
                point = trial.manifest.spawn - Vector3.up * .08f;
                session.viewCamera.transform.SetPositionAndRotation(point + Vector3.up, Quaternion.LookRotation(Vector3.down));
                session.ReportAim();
                Check(trial.truePositives == 1 && trial.duplicates == 1 && trial.falsePositives == 1,
                    "Manual aim/report/scoring workflow failed.");
                Check(session.mode == DefectExperimentMode.Manual && Mathf.Approximately(Time.timeScale, 1f), "Manual timing differs.");
                File.WriteAllText(Path.Combine(SessionState.GetString(OutputKey, ""), "manual-checks.txt"),
                    "PASS: manual camera aim, report, duplicate, false report, shared scorer, time scale 1. Software check only; not human participant data.\n");
            }
            catch (Exception error)
            { Debug.LogException(error); }
            finally { EditorApplication.isPlaying = false; }
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(ActiveKey, false)) return;
            SessionState.SetBool(ActiveKey, false);
            if (SessionState.GetBool(ManualKey, false))
            {
                SessionState.SetBool(ManualKey, false);
                bool manualPassed = File.Exists(Path.Combine(SessionState.GetString(OutputKey, ""), "manual-checks.txt"));
                EditorApplication.delayCall += () => EditorApplication.Exit(manualPassed ? 0 : 1);
                return;
            }
            string done = Path.Combine(SessionState.GetString(OutputKey, ""), "session.done");
            bool passed = File.Exists(done) && File.ReadAllText(done) == "complete";
            EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
        }
        private static void Set(UnityEngine.Object target, string property, bool value)
        { var serialized = new SerializedObject(target); serialized.FindProperty(property).boolValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
        private static void Set(UnityEngine.Object target, string property, int value)
        { var serialized = new SerializedObject(target); serialized.FindProperty(property).intValue = value; serialized.ApplyModifiedPropertiesWithoutUndo(); }
        private static string FileHash(string path)
        { using (var hash = SHA256.Create()) using (var file = File.OpenRead(path)) return BitConverter.ToString(hash.ComputeHash(file)).Replace("-", "").ToLowerInvariant(); }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
