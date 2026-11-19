using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TesisQA.WFC;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

namespace TesisQA.AI.Editor
{
    [InitializeOnLoad]
    public static class SmallCollisionDemoSetup
    {
        public const string ScenePath = "Assets/Scenes/AI/QA_CollisionSandbox.unity";
        public const string Root = "Assets/Experiments/CollisionSandbox";
        private const string Active = "CollisionSandbox.Check.Active", Output = "CollisionSandbox.Check.Output";
        private static int stage;
        private static double stageStarted;
        private static Keyboard keyboard;
        private static int fallsBefore;
        private static Vector3 expectedReturn;

        static SmallCollisionDemoSetup()
        { EditorApplication.update += CheckPlay; EditorApplication.playModeStateChanged += Changed; }

        [MenuItem("Tesis QA/Defect Experiment/Open small collision sandbox")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            if (File.Exists(ScenePath)) { EditorSceneManager.OpenScene(ScenePath); return; }
            Directory.CreateDirectory(Root); AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(DefectExperimentSetup.ScenePath);
            var source = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
            var oldScene = source.gameObject.scene;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var map = new GameObject("Small WFC map 6 x 2 x 6");
            var generator = map.AddComponent<WFCGenerator>(); var adapter = map.AddComponent<AIWFCTrainingAdapter>();
            EditorUtility.CopySerialized(source.generator, generator); EditorUtility.CopySerialized(source.adapter, adapter);
            var settings = new SerializedObject(generator);
            settings.FindProperty("gridSize").vector3IntValue = new Vector3Int(6, 2, 6);
            settings.FindProperty("generatedRoot").objectReferenceValue = null;
            settings.FindProperty("generateOnStart").boolValue = false;
            settings.FindProperty("drawGridGizmos").boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            settings = new SerializedObject(adapter); settings.FindProperty("generator").objectReferenceValue = generator;
            settings.FindProperty("addExplorationAreaTriggers").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var player = new GameObject("Demo Player (WASD and mouse)", typeof(CharacterController), typeof(AICharacterMotor));
            var motor = player.GetComponent<AICharacterMotor>();
            EditorUtility.CopySerialized(source.agent.GetComponent<CharacterController>(), player.GetComponent<CharacterController>());
            EditorUtility.CopySerialized(source.agent.Motor, motor);
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)).GetComponent<Camera>();
            camera.tag = "MainCamera"; EditorUtility.CopySerialized(source.viewCamera, camera); camera.enabled = true;
            var sun = new GameObject("Directional Light", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional; sun.intensity = 1.3f; sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.55f, .60f, .68f); RenderSettings.fog = false;
            var protocol = UnityEngine.Object.Instantiate(source.protocol);
            protocol.name = "CollisionSandbox"; protocol.protocolId = "collision-sandbox-v1";
            protocol.protocolId = "collision-sandbox-walls-v2"; protocol.inspectWalls = true;
            protocol.detectorVersion = "local-floor-wall-v3"; protocol.scorerVersion = "collision-region-wall-v3";
            protocol.scenarios.Clear(); protocol.fixedScenarioOrder = true;
            var demo = new GameObject("Collider sandbox controller").AddComponent<SmallCollisionDemo>();
            demo.protocol = protocol; demo.generator = generator; demo.adapter = adapter; demo.motor = motor; demo.eye = camera;
            EditorSceneManager.CloseScene(oldScene, true);
            DefectScenarioBuild built = null;
            Exception last = null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                var scenario = new DefectScenario { id = "SANDBOX", mapSeed = 170023451 + attempt * 7919,
                    defectSeed = 271828 + attempt, spawnSeed = 28320, fullDefects = 1, partialDefects = 1 };
                try
                {
                    built = DefectScenarioBuilder.Build(protocol, scenario, generator, adapter, false);
                    demo.demonstrationSpawns = built.manifest.defects.Select(defect =>
                        SmallCollisionDemo.FindApproach(built, defect, player.GetComponent<CharacterController>().radius)).ToArray();
                    scenario.frozenManifest = JsonUtility.ToJson(built.manifest); protocol.scenarios.Add(scenario); break;
                }
                catch (Exception exception) { last = exception; built?.Dispose(); built = null; generator.ClearGenerated(); }
            }
            if (built == null) throw new InvalidOperationException("Small map could not place two approachable defects.", last);
            AssetDatabase.CreateAsset(protocol, Root + "/CollisionSandbox.asset");
            File.WriteAllText(Root + "/Manifest.json", JsonUtility.ToJson(built.manifest, true));
            // Persist preview collision meshes, so the compact map is visible and
            // inspectable in Edit Mode as well as regenerated on entering Play.
            for (int i = 0; i < built.runtimeMeshes.Count; i++)
            {
                if (i == 0) AssetDatabase.CreateAsset(built.runtimeMeshes[i], Root + "/PreviewCollisionMeshes.asset");
                else AssetDatabase.AddObjectToAsset(built.runtimeMeshes[i], Root + "/PreviewCollisionMeshes.asset");
            }
            built.runtimeMeshes.Clear(); built.Dispose();
            motor.ResetMotor(demo.demonstrationSpawns[1], Quaternion.identity);
            Vector3 target = JsonUtility.FromJson<DefectScenarioManifest>(protocol.scenarios[0].frozenManifest).defects[1].center;
            camera.transform.SetPositionAndRotation(motor.transform.position + Vector3.up * 1.6f,
                Quaternion.LookRotation(target - (motor.transform.position + Vector3.up * 1.6f)));
            demo.CreateCanvas();
            AssetDatabase.SaveAssets(); EditorSceneManager.SaveScene(scene, ScenePath); AssetDatabase.Refresh();
            Debug.Log("COLLISION SANDBOX READY: compact 6x2x6 WFC, full/partial defect, nearby spawns and healthy comparison.");
        }

        public static void RunChecks()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval")) throw new InvalidOperationException("Use isolated batch Editor.");
            string output = null; var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-defectOutput") output = Path.GetFullPath(args[i + 1]);
            if (output == null || Directory.Exists(output)) throw new InvalidOperationException("Use a new software-check output.");
            Prepare(); Directory.CreateDirectory(output);
            SessionState.SetString(Output, output); SessionState.SetBool(Active, true);
            EditorApplication.EnterPlaymode();
        }

        private static void CheckPlay()
        {
            if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
            var demo = UnityEngine.Object.FindAnyObjectByType<SmallCollisionDemo>();
            if (demo == null) return;
            try
            {
                Check(demo.Error == null, demo.Error);
                if (!demo.Ready) return;
                if (stage == 0)
                {
                    Check(demo.Defects.Count == 2 && demo.generator.WalkwayConnections.Count <= 72, "Sandbox is not compact/two-defect.");
                    InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                    InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                    keyboard = InputSystem.AddDevice<Keyboard>();
                    demo.SelectDefect(0);
                    Check(!DefectGeometry.Supported(demo.Target) && demo.MarkAimedFloor() && demo.MarkCount == 1, "Full defect cannot be marked from nearby safe spawn.");
                    demo.SetHealthyControl(true);
                    Check(DefectGeometry.Supported(demo.Target) && !demo.MarkAimedFloor() && demo.MarkCount == 0, "Healthy comparison was marked or not restored.");
                    demo.SelectDefect(0); BeginWalk(demo); stage = 1;
                }
                else if (stage == 1 || stage == 3)
                {
                    if (demo.Falls <= fallsBefore)
                    { Check(EditorApplication.timeSinceStartup - stageStarted < 7, "Walking into defect did not make the player fall."); return; }
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    Check(Vector3.Distance(demo.motor.transform.position, demo.LastFallReturn) < .4f,
                        "Fallen player did not return to the last healthy tile.");
                    Check(Vector3.Distance(demo.LastFallReturn, demo.RecoveryAnchor) < .01f,
                        "Recovery ignored the remembered healthy tile.");
                    if (stage == 1)
                    {
                        demo.SelectDefect(1);
                        Check(!DefectGeometry.Supported(demo.Target) && DefectGeometry.Supported(demo.Target + Vector3.right * 2), "Partial hole or healthy rim missing.");
                        Check(demo.MarkAimedFloor(), "Partial hole cannot be marked from safe spawn.");
                        BeginWalk(demo); stage = 3;
                    }
                    else
                    {
                        demo.SelectDefect(1); demo.SetHealthyControl(true); BeginWalk(demo); stage = 4;
                    }
                }
                else if (stage == 4 && EditorApplication.timeSinceStartup - stageStarted >= 1.2)
                {
                    InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                    Check(demo.Falls == fallsBefore && Mathf.Abs(demo.motor.transform.position.y - demo.SafeSpawn.y) < .3f,
                        "Healthy comparison did not hold the walking player.");
                    demo.SelectDefect(0);
                    var map = (DefectScenarioBuild)typeof(SmallCollisionDemo).GetField("build", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(demo);
                    float radius = demo.motor.GetComponent<CharacterController>().radius + .45f;
                    bool remembered = false;
                    foreach (var point in map.coverageCells.Values.Where(p => p.y > demo.generator.CellSize.y * .5f))
                    {
                        if (!CollisionRecoveryPoint.TryGet(map, demo.generator, point + Vector3.up * .08f, radius, out var safe)) continue;
                        demo.motor.ResetMotor(safe, Quaternion.identity);
                        typeof(SmallCollisionDemo).GetMethod("RememberHealthyPosition", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(demo, null);
                        expectedReturn = demo.RecoveryAnchor;
                        remembered = true; break;
                    }
                    Check(remembered && Vector3.Distance(expectedReturn, demo.SafeSpawn) > 2,
                        "Checkpoint test did not move away from the initial demo position.");
                    fallsBefore = demo.Falls; stageStarted = EditorApplication.timeSinceStartup;
                    demo.motor.ResetMotor(expectedReturn + Vector3.down * 1000, Quaternion.identity);
                    stage = 5;
                }
                else if (stage == 5)
                {
                    if (demo.Falls == fallsBefore)
                    { Check(EditorApplication.timeSinceStartup - stageStarted < 3, "Checkpoint void fall did not recover."); return; }
                    Check(Vector3.Distance(demo.LastFallReturn, expectedReturn) < .02f
                        && Vector3.Distance(demo.motor.transform.position, expectedReturn) < .4f,
                        "Respawn returned to the demo start instead of the last visited healthy tile.");
                    Check(demo.LastFallReturn.y > demo.generator.CellSize.y * .5f, "Respawn lost the previously visited upper floor.");
                    string output = SessionState.GetString(Output, "");
                    File.WriteAllText(Path.Combine(output, "sandbox-checks.txt"),
                        "PASS: full/partial defect walking and fall; healthy comparison holds player; automatic recovery uses last healthy tile; independent checkpoint moved to upper floor away from demo start, forced void fall returned to that exact checkpoint. Software check only; no participant data.\n");
                    InputSystem.RemoveDevice(keyboard); keyboard = null; EditorApplication.isPlaying = false;
                }
            }
            catch (Exception exception)
            { Debug.LogException(exception); if (keyboard != null) InputSystem.RemoveDevice(keyboard); keyboard = null; EditorApplication.isPlaying = false; }
        }

        private static void BeginWalk(SmallCollisionDemo demo)
        {
            fallsBefore = demo.Falls; stageStarted = EditorApplication.timeSinceStartup;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
        }
        private static void Changed(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Active, false)) return;
            SessionState.SetBool(Active, false);
            bool passed = File.Exists(Path.Combine(SessionState.GetString(Output, ""), "sandbox-checks.txt"));
            EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
