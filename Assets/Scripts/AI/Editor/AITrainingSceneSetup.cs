using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TesisQA.WFC;
using TesisQA.WFC.Editor;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace TesisQA.AI.Editor
{
    // Rebuildable editor utility; runtime training code does not depend on this assembly.
    public static class AITrainingSceneSetup
    {
        private const string SceneRoot = "Assets/Scenes/AI";
        private const string Phase1Path = SceneRoot + "/AI_Phase1_Movement.unity";
        private const string Phase2Path = SceneRoot + "/AI_Phase2_SmallWFC.unity";
        private const string Phase3StairPath = SceneRoot + "/AI_Phase3A_StairNavigation.unity";
        private const string Phase3SmallTwoFloorPath =
            SceneRoot + "/AI_Phase3B_SmallTwoFloorWFC.unity";
        private const string Phase3Path = SceneRoot + "/AI_Phase3_ComplexRouting.unity";
        private const string SceneVersionMarker = "AI Scene Setup Version 16";
        private const string KenneyModelRoot =
            "Assets/Modular Tile Sets/kenney_modular-dungeon-kit_1.0/Models/FBX format";
        private const string WideCorridorModelPath =
            KenneyModelRoot + "/corridor-wide.fbx";
        private const string WideCorridorEndModelPath =
            KenneyModelRoot + "/corridor-wide-end.fbx";
        private const string WideTileSetPath =
            "Assets/WFC/Kenney Wide Corridors/TS_Kenney_Wide_Corridors.asset";
        private const string WideStairLandingPrefabPath =
            "Assets/WFC/Kenney Wide Corridors/Prefabs/PF_Wide_Stairs_With_Landing.prefab";
        private static readonly string[] TrainingScenePaths =
        {
            Phase1Path,
            Phase2Path,
            Phase3Path
        };

        private readonly struct OpenTrainingSceneState
        {
            public readonly string path;
            public readonly bool wasActive;

            public OpenTrainingSceneState(string path, bool wasActive)
            {
                this.path = path;
                this.wasActive = wasActive;
            }
        }

        [InitializeOnLoadMethod]
        private static void ScheduleInitialCreation()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode
                    || AreGeneratedScenesCurrent())
                {
                    return;
                }

                if (HasDirtyTrainingScene())
                {
                    Debug.LogWarning(
                        "Scene AI yang terbuka mempunyai perubahan belum disimpan. "
                        + "Simpan scene dulu, lalu jalankan Tools > AI > Create or Rebuild Training Scenes.");
                    return;
                }

                try
                {
                    CreateAllScenes(true);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
        }

        [MenuItem("Tools/AI/Create or Rebuild Training Scenes")]
        public static void RebuildAllScenes()
        {
            if (!EditorUtility.DisplayDialog(
                    "Rebuild AI Training Scenes",
                    "Tiga scene AI utama akan dibuat ulang. Scene training yang sedang " +
                    "terbuka akan ditutup sementara lalu dibuka kembali. Lanjutkan?",
                    "Rebuild",
                    "Batal"))
            {
                return;
            }

            CreateAllScenes(true);
        }

        // Dipakai oleh Unity batch mode untuk validasi otomatis tanpa dialog Editor.
        public static void RebuildAllScenesFromCommandLine()
        {
            CreateAllScenes(true);
        }

        private static void CreateAllScenes(bool overwrite)
        {
            if (HasDirtyTrainingScene())
            {
                throw new InvalidOperationException(
                    "Simpan perubahan pada scene AI yang terbuka sebelum rebuild agar tidak hilang.");
            }

            EnsureSceneFolder();
            List<OpenTrainingSceneState> openTrainingScenes =
                CaptureOpenTrainingScenes();
            Scene rebuildHostScene = PrepareOpenScenesForRebuild(openTrainingScenes);

            try
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(WideStairLandingPrefabPath) == null)
                {
                    KenneyWFCSetup.CreateOrUpdateTileSet();
                }

                CreatePhaseOneScene(overwrite, rebuildHostScene);
                CreateWfcScene(
                    Phase2Path,
                    AITrainingPhase.MultiroomNavigation,
                    new Vector3Int(10, 1, 10),
                    22002,
                    40,
                    overwrite,
                    rebuildHostScene);
                CreateWfcScene(
                    Phase3Path,
                    AITrainingPhase.ComplexRouting,
                    new Vector3Int(24, 1, 24),
                    33003,
                    20,
                    overwrite,
                    rebuildHostScene);

                AddScenesToBuildSettings();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"AI training scenes siap di {SceneRoot}.");
            }
            finally
            {
                RestoreOpenTrainingScenes(openTrainingScenes, rebuildHostScene);
            }
        }

        private static bool IsTrainingScenePath(string path)
        {
            return !string.IsNullOrEmpty(path)
                && TrainingScenePaths.Any(item =>
                    string.Equals(item, path, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasDirtyTrainingScene()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (IsTrainingScenePath(scene.path) && scene.isDirty)
                {
                    return true;
                }
            }

            return false;
        }

        private static List<OpenTrainingSceneState> CaptureOpenTrainingScenes()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            var result = new List<OpenTrainingSceneState>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (IsTrainingScenePath(scene.path))
                {
                    result.Add(new OpenTrainingSceneState(
                        scene.path,
                        scene == activeScene));
                }
            }

            return result;
        }

        private static Scene PrepareOpenScenesForRebuild(
            List<OpenTrainingSceneState> openTrainingScenes)
        {
            Scene activeScene = SceneManager.GetActiveScene();
            Scene rebuildHostScene = activeScene;
            if (!activeScene.IsValid() || IsTrainingScenePath(activeScene.path))
            {
                rebuildHostScene = EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    NewSceneMode.Additive);
                SceneManager.SetActiveScene(rebuildHostScene);
            }

            foreach (OpenTrainingSceneState state in openTrainingScenes)
            {
                Scene scene = SceneManager.GetSceneByPath(state.path);
                if (scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            return rebuildHostScene;
        }

        private static void RestoreOpenTrainingScenes(
            List<OpenTrainingSceneState> openTrainingScenes,
            Scene rebuildHostScene)
        {
            Scene sceneToActivate = default;
            foreach (OpenTrainingSceneState state in openTrainingScenes)
            {
                if (!File.Exists(state.path))
                {
                    continue;
                }

                Scene restored = EditorSceneManager.OpenScene(
                    state.path,
                    OpenSceneMode.Additive);
                if (state.wasActive)
                {
                    sceneToActivate = restored;
                }
            }

            if (sceneToActivate.IsValid() && sceneToActivate.isLoaded)
            {
                SceneManager.SetActiveScene(sceneToActivate);
            }

            // Host tanpa path hanya dibuat ketika scene aktif sebelumnya adalah
            // salah satu scene training. Setelah scene tersebut dibuka kembali,
            // host sementara aman untuk ditutup.
            if (rebuildHostScene.IsValid()
                && rebuildHostScene.isLoaded
                && string.IsNullOrEmpty(rebuildHostScene.path)
                && sceneToActivate.IsValid()
                && sceneToActivate.isLoaded)
            {
                EditorSceneManager.CloseScene(rebuildHostScene, true);
            }
        }

        private static void CreatePhaseThreeStairScene(
            bool overwrite,
            Scene previousActiveScene)
        {
            if (!overwrite && File.Exists(Phase3StairPath))
            {
                return;
            }

            GameObject stairPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                WideStairLandingPrefabPath);
            if (stairPrefab == null)
            {
                throw new InvalidOperationException(
                    $"Prefab tangga dengan landing tidak ditemukan: {WideStairLandingPrefabPath}");
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            try
            {
                var environmentObject = new GameObject(
                    "AI Training Environment - Phase 3A Stair Navigation");
                CreateVersionMarker(environmentObject.transform);
                AIEpisodeMetrics metrics = environmentObject.AddComponent<AIEpisodeMetrics>();
                AITrainingEnvironment environment =
                    environmentObject.AddComponent<AITrainingEnvironment>();

                GameObject stair = (GameObject)PrefabUtility.InstantiatePrefab(stairPrefab);
                stair.name = "Wide Stairs With Landing";
                stair.transform.SetParent(environmentObject.transform, false);

                GameObject corridorModel = LoadRequiredModel(WideCorridorModelPath);
                GameObject corridorEndModel = LoadRequiredModel(WideCorridorEndModelPath);
                Transform modularPath = new GameObject("Modular Corridor Path").transform;
                modularPath.SetParent(environmentObject.transform, false);

                // Urutan socket: End(N) -> Straight -> Stair(S bawah/N atas)
                // -> Straight -> End(S). End hanya boleh berada di dua ujung luar.
                CreateCorridorModule(corridorEndModel, "Lower Corridor Wide End",
                    modularPath, new Vector3(0f, 0f, -16f), 90f);
                CreateCorridorModule(corridorModel, "Lower Corridor Wide",
                    modularPath, new Vector3(0f, 0f, -8f), 90f);
                CreateCorridorModule(corridorModel, "Upper Corridor Wide",
                    modularPath, new Vector3(0f, 4f, 8f), 90f);
                CreateCorridorModule(corridorEndModel, "Upper Corridor Wide End",
                    modularPath, new Vector3(0f, 4f, 16f), 270f);

                ValidatePrefabStairRamp(stair.transform);

                CreateSpawnPoint(environmentObject.transform, "Lower Floor Spawn",
                    new Vector3(0f, 0.05f, -16f), AISpawnPointUsage.Both);
                CreateSpawnPoint(environmentObject.transform, "Upper Floor Spawn",
                    new Vector3(0f, 4.05f, 16f), AISpawnPointUsage.Both);

                CreateBoxExplorationArea(environmentObject.transform, "Lower Floor Area",
                    new Vector3(0f, 1f, -12f), new Vector3(6f, 2f, 15.5f), 0);
                CreateBoxExplorationArea(environmentObject.transform, "Upper Floor Area",
                    new Vector3(0f, 5f, 12f), new Vector3(6f, 2f, 15.5f), 1);

                GameObject stairTrigger = new GameObject("AI Stair Traversal Area");
                stairTrigger.layer = 2;
                stairTrigger.transform.SetParent(stair.transform, false);
                stairTrigger.transform.localPosition = new Vector3(0f, 2f, 0f);
                BoxCollider stairTriggerCollider = stairTrigger.AddComponent<BoxCollider>();
                stairTriggerCollider.isTrigger = true;
                stairTriggerCollider.size = new Vector3(4f, 4f, 6f);
                stairTrigger.AddComponent<StairTraversalArea>();

                ExplorationTarget target = CreateTarget(
                    environmentObject.transform,
                    new Vector3(0f, 4.55f, 16f));
                ExplorationAgent agent = CreateAgent(
                    environmentObject.transform,
                    new Vector3(0f, 0.05f, -16f),
                    2500,
                    32f,
                    ExplorationAgent.BaseVectorObservationSize);
                // Phase 3 rays report geometry only (hit/miss and distance).
                // Keep this identical across 3A, 3B, and the full WFC scene.
                agent.GetComponent<RayPerceptionSensorComponent3D>().DetectableTags =
                    new List<string>();
                agent.GetComponent<RayPerceptionSensorComponent3D>().MaxRayDegrees = 180f;
                SetFloat(agent.GetComponent<AICharacterMotor>(), "moveSpeed", 6f);
                SetFloat(agent.GetComponent<AICharacterMotor>(), "rotationSpeed", 180f);
                SetFloat(agent, "stepPenalty", -0.0001f);

                ConfigureEnvironment(
                    environment,
                    metrics,
                    target,
                    agent,
                    AITrainingPhase.ComplexRouting,
                    null,
                    33101,
                    8f,
                    40f);
                ConfigureCoverageEnvironment(environment, 1f);
                SetFloat(environment, "minimumTravelOnOtherFloorToComplete", 3f);
                SetBoolean(environment, "alternateFixedSpawnDirection", true);

                CreateLightingAndCamera(
                    new Vector3(20f, 18f, -28f),
                    new Vector3(0f, 2f, 0f));
                SaveAndClose(scene, Phase3StairPath, previousActiveScene);
            }
            catch
            {
                CloseWithoutSaving(scene, previousActiveScene);
                throw;
            }
        }

        private static void CreatePhaseOneScene(bool overwrite, Scene previousActiveScene)
        {
            if (!overwrite && File.Exists(Phase1Path))
            {
                return;
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            try
            {
                var environmentObject = new GameObject("AI Training Environment - Phase 1");
                CreateVersionMarker(environmentObject.transform);
                AIEpisodeMetrics metrics = environmentObject.AddComponent<AIEpisodeMetrics>();
                AITrainingEnvironment environment =
                    environmentObject.AddComponent<AITrainingEnvironment>();

                GameObject corridorModel = LoadRequiredModel(WideCorridorModelPath);
                GameObject corridorEndModel = LoadRequiredModel(WideCorridorEndModelPath);
                GameObject stairPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    WideStairLandingPrefabPath);
                if (stairPrefab == null)
                {
                    throw new InvalidOperationException(
                        $"Prefab tangga tidak ditemukan: {WideStairLandingPrefabPath}");
                }

                Transform flatRoot = new GameObject("Lesson 0 - Straight Corridor").transform;
                flatRoot.SetParent(environmentObject.transform, false);
                Transform modularPath = new GameObject("Modular Corridor Path").transform;
                modularPath.SetParent(flatRoot, false);
                for (int z = -24; z <= 24; z += 8)
                {
                    bool isLowerEnd = z == -24;
                    bool isUpperEnd = z == 24;
                    GameObject model = isLowerEnd || isUpperEnd
                        ? corridorEndModel
                        : corridorModel;
                    float yaw = isUpperEnd ? 270f : 90f;
                    string moduleName = isLowerEnd
                        ? "Start Corridor Wide End"
                        : isUpperEnd
                            ? "Finish Corridor Wide End"
                            : $"Corridor Wide {z:0}";
                    CreateCorridorModule(
                        model,
                        moduleName,
                        modularPath,
                        new Vector3(0f, 0f, z),
                        yaw);
                }

                for (float z = -20f; z <= 20f; z += 4f)
                {
                    CreateSpawnPoint(
                        flatRoot,
                        $"Spawn {z:0}",
                        new Vector3(0f, 0.05f, z),
                        AISpawnPointUsage.Both);
                }

                Transform stairRoot = new GameObject("Lesson 1 - Corridor With Stairs").transform;
                stairRoot.SetParent(environmentObject.transform, false);
                GameObject stair = (GameObject)PrefabUtility.InstantiatePrefab(stairPrefab);
                stair.name = "Wide Stairs With Landing";
                stair.transform.SetParent(stairRoot, false);
                stair.transform.localPosition = new Vector3(0f, 0f, 2f);
                ValidatePrefabStairRamp(stair.transform);

                CreateCorridorModule(corridorEndModel, "Lower Corridor Wide End",
                    stairRoot, new Vector3(0f, 0f, -16f), 90f);
                CreateCorridorModule(corridorModel, "Lower Corridor Wide",
                    stairRoot, new Vector3(0f, 0f, -8f), 90f);
                CreateCorridorModule(corridorModel, "Upper Corridor Wide",
                    stairRoot, new Vector3(0f, 4f, 8f), 90f);
                CreateCorridorModule(corridorEndModel, "Upper Corridor Wide End",
                    stairRoot, new Vector3(0f, 4f, 16f), 270f);
                CreateSpawnPoint(stairRoot, "Lower Floor Spawn",
                    new Vector3(0f, 0.05f, -16f), AISpawnPointUsage.Both);
                CreateSpawnPoint(stairRoot, "Upper Floor Spawn",
                    new Vector3(0f, 4.05f, 16f), AISpawnPointUsage.Both);

                GameObject stairTrigger = new GameObject("AI Stair Traversal Area");
                stairTrigger.layer = 2;
                stairTrigger.transform.SetParent(stair.transform, false);
                stairTrigger.transform.localPosition = new Vector3(0f, 2f, 0f);
                BoxCollider stairTriggerCollider = stairTrigger.AddComponent<BoxCollider>();
                stairTriggerCollider.isTrigger = true;
                stairTriggerCollider.size = new Vector3(4f, 4f, 6f);
                stairTrigger.AddComponent<StairTraversalArea>();
                stairRoot.gameObject.SetActive(false);
                SetObjectReference(environment, "phaseOneFlatRoot", flatRoot.gameObject);
                SetObjectReference(environment, "phaseOneStairRoot", stairRoot.gameObject);

                ExplorationTarget target = CreateTarget(
                    environmentObject.transform,
                    new Vector3(0f, 0.55f, 20f));
                ExplorationAgent agent = CreateAgent(
                    environmentObject.transform,
                    new Vector3(0f, 0.05f, -20f),
                    1500,
                    32f,
                    ExplorationAgent.BaseVectorObservationSize);

                ConfigureEnvironment(
                    environment,
                    metrics,
                    target,
                    agent,
                    AITrainingPhase.MovementControl,
                    null,
                    11001,
                    6f,
                    0f);
                SetBoolean(environment, "alternateFixedSpawnDirection", true);
                SetFloat(environment, "newAreaReward", 0f);
                SetFloat(environment, "firstStairTraversalReward", 0.08f);

                CreateLightingAndCamera(
                    new Vector3(12f, 14f, -28f),
                    new Vector3(0f, 0f, 0f));
                SaveAndClose(scene, Phase1Path, previousActiveScene);
            }
            catch
            {
                CloseWithoutSaving(scene, previousActiveScene);
                throw;
            }
        }

        private static void CreateWfcScene(
            string scenePath,
            AITrainingPhase phase,
            Vector3Int gridSize,
            int seed,
            int regenerateEveryEpisodes,
            bool overwrite,
            Scene previousActiveScene)
        {
            if (!overwrite && File.Exists(scenePath))
            {
                return;
            }

            WFCTileSet tileSet = AssetDatabase.LoadAssetAtPath<WFCTileSet>(WideTileSetPath);
            if (tileSet == null)
            {
                throw new InvalidOperationException(
                    $"Wide Corridor Tile Set tidak ditemukan: {WideTileSetPath}");
            }

            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);

            try
            {
                string phaseNumber = phase == AITrainingPhase.MultiroomNavigation ? "2" : "3";
                var environmentObject = new GameObject(
                    $"AI Training Environment - Phase {phaseNumber}");
                CreateVersionMarker(environmentObject.transform);
                AIEpisodeMetrics metrics = environmentObject.AddComponent<AIEpisodeMetrics>();
                AITrainingEnvironment environment =
                    environmentObject.AddComponent<AITrainingEnvironment>();

                var generatorObject = new GameObject("WFC Generator");
                generatorObject.transform.SetParent(environmentObject.transform, false);
                WFCGenerator generator = generatorObject.AddComponent<WFCGenerator>();
                ConfigureWfcGenerator(generator, tileSet, gridSize, seed);

                AIWFCTrainingAdapter adapter =
                    generatorObject.AddComponent<AIWFCTrainingAdapter>();
                SetObjectReference(adapter, "generator", generator);
                SetInteger(adapter, "regenerateEveryEpisodes", regenerateEveryEpisodes);
                SetInteger(adapter, "baseMapSeed", seed);
                SetBoolean(adapter, "useStairSpawnCurriculum", false);
                SetBoolean(adapter, "useFloorCountCurriculum", true);
                SetBoolean(adapter, "useFloorTransferLesson", true);
                bool phase3Coverage = phase == AITrainingPhase.ComplexRouting;
                SetBoolean(adapter, "defaultGraduatedTransferLessons", phase3Coverage);
                SetInteger(adapter, "defaultFloorLesson", phase3Coverage ? 4 : 0);
                SetBoolean(adapter, "defaultTerrainCurriculum", phase3Coverage);
                SetInteger(adapter, "defaultTerrainLevel", phase3Coverage ? 8 : 0);
                SetInteger(adapter, "minimumTwoFloorStairCount",
                    phase == AITrainingPhase.MultiroomNavigation ? 1 : 2);
                SetInteger(adapter, "maximumTrainingMapAttempts", 12);
                SetFloat(
                    adapter,
                    "crossFloorSpawnChance",
                    1f);

                ExplorationTarget target = CreateTarget(
                    environmentObject.transform,
                    new Vector3(0f, 0.5f, 0f));
                // Coverage is the goal in Phase 2/3. Keep the target transform
                // only as a spawn/metrics anchor, not a visible or touchable goal.
                target.GetComponent<Renderer>().enabled = false;
                target.GetComponent<Collider>().enabled = false;
                ExplorationAgent agent = CreateAgent(
                    environmentObject.transform,
                    new Vector3(0f, 5f, 0f),
                    phase == AITrainingPhase.MultiroomNavigation ? 4000 : 12000,
                    32f,
                    ExplorationAgent.BaseVectorObservationSize);
                if (phase == AITrainingPhase.ComplexRouting)
                {
                    RayPerceptionSensorComponent3D raySensor =
                        agent.GetComponent<RayPerceptionSensorComponent3D>();
                    raySensor.DetectableTags = new List<string>();
                    raySensor.MaxRayDegrees = 180f;
                }
                AICharacterMotor motor = agent.GetComponent<AICharacterMotor>();
                SetFloat(
                    motor,
                    "moveSpeed",
                    6f);
                // Keep regenerated coverage scenes consistent with Phase2B.
                SetBoolean(agent, "useCompletedBranchAssist", false);
                SetBoolean(agent, "useBranchMemorySensor", true);
                SetBoolean(agent, "useLongRangeTerrainProfile", false);
                SetBoolean(agent, "useDistantTerrainSensor", true);
                SetBoolean(agent, "useRampMemorySensor", true);
                SetFloat(
                    motor,
                    "rotationSpeed",
                    180f);
                SetFloat(
                    agent,
                    "stepPenalty",
                    phase == AITrainingPhase.MultiroomNavigation ? -0.0000625f : -0.0000333333f);

                ConfigureEnvironment(
                    environment,
                    metrics,
                    target,
                    agent,
                    phase,
                    adapter,
                    seed,
                    8f,
                    0f);
                ConfigureCoverageEnvironment(
                    environment,
                    phase == AITrainingPhase.MultiroomNavigation ? 0.20f : 0.25f,
                    10f);
                SetFloat(environment, "twoFloorNoNewAreaTimeout", 25f);
                SetFloat(environment, "twoFloorNoNewAreaPenalty", -0.35f);
                SetInteger(environment, "phase3RewardRevision", phase3Coverage ? 2 : 0);
                if (phase == AITrainingPhase.MultiroomNavigation)
                {
                    SetInteger(agent, "twoFloorMaxStep", 6000);
                }
                SetFloat(agent, "requiredDisplacement", 4f);
                SetFloat(agent, "noDisplacementTimeout",
                    12f);
                SetFloat(agent, "twoFloorNoDisplacementTimeout", 18f);
                SetFloat(agent, "noDisplacementPenalty", -0.25f);
                SetFloat(environment, "earlyGoalReward", 0f);
                SetFloat(environment, "graduatedTransferAscentBonus", 0.4f);

                bool previewGenerated = generator.GenerateWithSeed(seed);
                if (previewGenerated)
                {
                    adapter.EnsureTrainingColliders();
                    adapter.RefreshWalkablePositions();
                }

                Vector3 mapCenter = new Vector3(
                    (gridSize.x - 1) * 4f,
                    0f,
                    (gridSize.z - 1) * 4f);
                float cameraHeight = phase == AITrainingPhase.MultiroomNavigation ? 90f : 210f;
                CreateLightingAndCamera(
                    mapCenter + new Vector3(0f, cameraHeight, -cameraHeight * 0.35f),
                    mapCenter);
                SaveAndClose(scene, scenePath, previousActiveScene);
            }
            catch
            {
                CloseWithoutSaving(scene, previousActiveScene);
                throw;
            }
        }

        private static ExplorationAgent CreateAgent(
            Transform parent,
            Vector3 position,
            int maxStep,
            float rayLength,
            int vectorObservationSize)
        {
            var agentObject = new GameObject("Explorer Agent");
            agentObject.tag = "Player";
            agentObject.transform.SetParent(parent, false);
            agentObject.transform.position = position;

            CharacterController controller = agentObject.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.slopeLimit = 50f;
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.04f;
            controller.minMoveDistance = 0f;

            AICharacterMotor motor = agentObject.AddComponent<AICharacterMotor>();
            ExplorationAgent agent = agentObject.AddComponent<ExplorationAgent>();
            EpisodicVisitMemorySensorComponent visitMemorySensor =
                agentObject.AddComponent<EpisodicVisitMemorySensorComponent>();
            agent.MaxStep = maxStep;

            BehaviorParameters behavior = agentObject.GetComponent<BehaviorParameters>();
            if (behavior == null)
            {
                behavior = agentObject.AddComponent<BehaviorParameters>();
            }
            behavior.BehaviorName = "Explorer";
            behavior.BehaviorType = BehaviorType.Default;
            behavior.BrainParameters.VectorObservationSize = vectorObservationSize;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(2);

            DecisionRequester requester = agentObject.AddComponent<DecisionRequester>();
            requester.DecisionPeriod = 5;
            requester.TakeActionsBetweenDecisions = true;

            RayPerceptionSensorComponent3D raySensor =
                agentObject.AddComponent<RayPerceptionSensorComponent3D>();
            raySensor.SensorName = "Explorer Rays";
            raySensor.DetectableTags = new List<string>();
            raySensor.RaysPerDirection = 8;
            raySensor.MaxRayDegrees = 180f;
            raySensor.SphereCastRadius = 0.08f;
            raySensor.RayLength = rayLength;
            raySensor.RayLayerMask = ~(1 << 2);
            raySensor.ObservationStacks = 1;
            raySensor.AlternatingRayOrder = false;
            raySensor.UseBatchedRaycasts = true;
            raySensor.StartVerticalOffset = 1f;
            raySensor.EndVerticalOffset = 1f;

            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            visual.name = "Agent Visual";
            visual.transform.SetParent(agentObject.transform, false);
            visual.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            visual.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            Object.DestroyImmediate(visual.GetComponent<Collider>());

            SetObjectReference(agent, "motor", motor);
            SetObjectReference(agent, "visitMemorySensor", visitMemorySensor);
            SetBoolean(agent, "useTerrainProfileSensor", true);

            if (agentObject.GetComponents<BehaviorParameters>().Length != 1)
            {
                throw new InvalidOperationException(
                    "Explorer Agent harus memiliki tepat satu BehaviorParameters.");
            }

            return agent;
        }

        private static bool AreGeneratedScenesCurrent()
        {
            foreach (string path in TrainingScenePaths)
            {
                if (!File.Exists(path)
                    || !File.ReadAllText(path).Contains(SceneVersionMarker))
                {
                    return false;
                }
            }

            return true;
        }

        private static void CreateVersionMarker(Transform parent)
        {
            var marker = new GameObject(SceneVersionMarker);
            marker.transform.SetParent(parent, false);
        }

        private static ExplorationTarget CreateTarget(Transform parent, Vector3 position)
        {
            GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            targetObject.name = "Exploration Target";
            targetObject.tag = "Respawn";
            targetObject.transform.SetParent(parent, false);
            targetObject.transform.position = position;
            targetObject.transform.localScale = new Vector3(0.8f, 0.5f, 0.8f);

            Collider collider = targetObject.GetComponent<Collider>();
            collider.isTrigger = true;
            return targetObject.AddComponent<ExplorationTarget>();
        }

        private static void ConfigureEnvironment(
            AITrainingEnvironment environment,
            AIEpisodeMetrics metrics,
            ExplorationTarget target,
            ExplorationAgent agent,
            AITrainingPhase phase,
            AIWFCTrainingAdapter adapter,
            int seed,
            float minimumDistance,
            float maximumDistance)
        {
            SetEnum(environment, "phase", (int)phase - 1);
            SetObjectReference(environment, "metrics", metrics);
            SetObjectReference(environment, "target", target);
            SetObjectReference(environment, "wfcAdapter", adapter);
            SetInteger(environment, "randomSeed", seed);
            SetFloat(environment, "minimumSpawnDistance", minimumDistance);
            SetFloat(environment, "maximumSpawnDistance", maximumDistance);
            SetBoolean(
                environment,
                "phaseOneTargetAlwaysVisible",
                phase == AITrainingPhase.MovementControl);
            SetFloat(
                environment,
                "targetVisibilityDistance",
                phase == AITrainingPhase.MovementControl ? 60f : 24f);

            SetObjectReference(agent, "environment", environment);
        }

        private static void ConfigureWfcGenerator(
            WFCGenerator generator,
            WFCTileSet tileSet,
            Vector3Int gridSize,
            int seed)
        {
            SetObjectReference(generator, "tileSet", tileSet);
            SetVector3Int(generator, "gridSize", gridSize);
            SetVector3(generator, "cellSize", new Vector3(8f, 4f, 8f));
            SetInteger(generator, "seed", seed);
            SetBoolean(generator, "randomizeSeed", false);
            SetInteger(generator, "maxRestarts", 80);
            SetInteger(
                generator,
                "minimumStairCount",
                gridSize.y <= 1 ? 0 : gridSize.x <= 10 ? 1 : 2);
            SetBoolean(generator, "generateOnStart", false);
            SetVector2Int(generator, "boundaryEndCountRange", new Vector2Int(2, 8));
            SetBoolean(generator, "drawGridGizmos", false);
        }

        private static void CreateSpawnPoint(
            Transform parent,
            string pointName,
            Vector3 position,
            AISpawnPointUsage usage)
        {
            var pointObject = new GameObject(pointName);
            pointObject.transform.SetParent(parent, false);
            pointObject.transform.position = position;
            AITrainingSpawnPoint point = pointObject.AddComponent<AITrainingSpawnPoint>();
            SetEnum(point, "usage", (int)usage);
        }

        private static void CreateExplorationArea(Transform parent, float z)
        {
            GameObject area = GameObject.CreatePrimitive(PrimitiveType.Cube);
            area.name = $"Exploration Area {z:0}";
            area.layer = 2;
            area.transform.SetParent(parent, false);
            area.transform.position = new Vector3(0f, 1f, z);
            area.transform.localScale = new Vector3(7.5f, 2f, 4f);
            area.GetComponent<MeshRenderer>().enabled = false;
            BoxCollider collider = area.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            area.AddComponent<ExplorationArea>();
        }

        private static void CreateBoxExplorationArea(
            Transform parent,
            string areaName,
            Vector3 position,
            Vector3 size,
            int floorIndex)
        {
            GameObject area = GameObject.CreatePrimitive(PrimitiveType.Cube);
            area.name = areaName;
            area.layer = 2;
            area.transform.SetParent(parent, false);
            area.transform.position = position;
            area.transform.localScale = size;
            area.GetComponent<MeshRenderer>().enabled = false;
            BoxCollider collider = area.GetComponent<BoxCollider>();
            collider.isTrigger = true;
            ExplorationArea explorationArea = area.AddComponent<ExplorationArea>();
            explorationArea.ConfigureFloor(floorIndex);
        }

        private static void ConfigureCoverageEnvironment(
            AITrainingEnvironment environment,
            float targetCoverage,
            float noNewAreaTimeout = 20f)
        {
            SetFloat(environment, "newAreaReward", 0.02f);
            SetFloat(environment, "maximumAreaRewardPerEpisode", 1f);
            SetFloat(environment, "upperFloorNewAreaRewardBonus", 0.02f);
            SetFloat(environment, "maximumUpperFloorAreaBonusPerEpisode", 1f);
            SetBoolean(environment, "requireCoverageBeforeGoal", true);
            SetFloat(environment, "targetCoverage", targetCoverage);
            SetBoolean(environment, "requireCoverageOnEveryFloor", true);
            SetBoolean(environment, "requireStairTraversalForCoverage", true);
            SetFloat(environment, "maximumFloorCoverageDifference", 0.1f);
            SetBoolean(environment, "useProgressiveFloorBalance", true);
            SetFloat(environment, "coverageCompletionReward", 1f);
            SetFloat(environment, "earlyGoalReward", 0.1f);
            SetFloat(environment, "firstStairSeenReward", 0f);
            SetFloat(environment, "firstStairTraversalReward", 0.08f);
            SetFloat(environment, "firstOtherFloorReward", 0.2f);
            SetFloat(environment, "balancedCoverageRewardScale", 1.5f);
            SetFloat(environment, "maximumBalancedCoverageReward", 0.3f);
            SetFloat(environment, "shortLoopPenalty", -0.005f);
            SetFloat(environment, "noNewAreaTimeout", noNewAreaTimeout);
            SetFloat(environment, "noNewAreaPenalty", -0.25f);
        }

        private static GameObject CreateCube(
            string objectName,
            Transform parent,
            Vector3 position,
            Vector3 scale)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = objectName;
            cube.transform.SetParent(parent, false);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            return cube;
        }

        private static void ValidatePrefabStairRamp(Transform stair)
        {
            Transform ramp = stair.Find("AI Smooth Stair Ramp");
            BoxCollider collider = ramp != null ? ramp.GetComponent<BoxCollider>() : null;
            if (collider == null || !collider.enabled || collider.isTrigger)
            {
                throw new InvalidOperationException(
                    "Prefab tangga harus mempunyai child 'AI Smooth Stair Ramp' "
                    + "dengan BoxCollider aktif dan bukan trigger.");
            }
        }

        private static GameObject LoadRequiredModel(string assetPath)
        {
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (model == null)
            {
                throw new InvalidOperationException(
                    $"Model modular tidak ditemukan: {assetPath}");
            }

            return model;
        }

        private static GameObject CreateCorridorModule(
            GameObject model,
            string objectName,
            Transform parent,
            Vector3 position,
            float yaw)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            instance.name = objectName;
            instance.transform.SetParent(parent, false);
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            AddStaticMeshColliders(instance);
            return instance;
        }

        private static void AddStaticMeshColliders(GameObject root)
        {
            foreach (MeshFilter meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshFilter.sharedMesh == null
                    || meshFilter.GetComponent<Collider>() != null)
                {
                    continue;
                }

                MeshCollider collider = meshFilter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = meshFilter.sharedMesh;
                collider.convex = false;
            }
        }

        private static void CreateLightingAndCamera(Vector3 cameraPosition, Vector3 lookAt)
        {
            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.transform.position = cameraPosition;
            cameraObject.transform.LookAt(lookAt);
        }

        private static void SaveAndClose(Scene scene, string path, Scene previousActiveScene)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, path))
            {
                throw new InvalidOperationException($"Gagal menyimpan scene: {path}");
            }

            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }

            EditorSceneManager.CloseScene(scene, true);
        }

        private static void CloseWithoutSaving(Scene scene, Scene previousActiveScene)
        {
            if (previousActiveScene.IsValid() && previousActiveScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousActiveScene);
            }

            if (scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void EnsureSceneFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            if (!AssetDatabase.IsValidFolder(SceneRoot))
            {
                AssetDatabase.CreateFolder("Assets/Scenes", "AI");
            }
        }

        private static void AddScenesToBuildSettings()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(item => item.path != Phase3StairPath
                    && item.path != Phase3SmallTwoFloorPath)
                .ToList();
            var existingPaths = new HashSet<string>(scenes.Select(item => item.path));
            foreach (string path in TrainingScenePaths)
            {
                if (existingPaths.Add(path))
                {
                    scenes.Add(new EditorBuildSettingsScene(path, true));
                }
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static SerializedProperty FindProperty(Object target, string propertyName)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
            {
                throw new MissingFieldException(target.GetType().Name, propertyName);
            }

            return property;
        }

        private static void Apply(SerializedProperty property)
        {
            property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(property.serializedObject.targetObject);
        }

        private static void SetObjectReference(Object target, string name, Object value)
        {
            SerializedProperty property = FindProperty(target, name);
            property.objectReferenceValue = value;
            Apply(property);
        }

        private static void SetInteger(Object target, string name, int value)
        {
            SerializedProperty property = FindProperty(target, name);
            property.intValue = value;
            Apply(property);
        }

        private static void SetFloat(Object target, string name, float value)
        {
            SerializedProperty property = FindProperty(target, name);
            property.floatValue = value;
            Apply(property);
        }

        private static void SetBoolean(Object target, string name, bool value)
        {
            SerializedProperty property = FindProperty(target, name);
            property.boolValue = value;
            Apply(property);
        }

        private static void SetEnum(Object target, string name, int value)
        {
            SerializedProperty property = FindProperty(target, name);
            property.enumValueIndex = value;
            Apply(property);
        }

        private static void SetVector3(Object target, string name, Vector3 value)
        {
            SerializedProperty property = FindProperty(target, name);
            property.vector3Value = value;
            Apply(property);
        }

        private static void SetVector3Int(Object target, string name, Vector3Int value)
        {
            SerializedProperty property = FindProperty(target, name);
            property.vector3IntValue = value;
            Apply(property);
        }

        private static void SetVector2Int(Object target, string name, Vector2Int value)
        {
            SerializedProperty property = FindProperty(target, name);
            property.vector2IntValue = value;
            Apply(property);
        }
    }
}
