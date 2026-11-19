using System;
using System.Collections.Generic;
using TesisQA.WFC;
using Unity.MLAgents;
using UnityEngine;

namespace TesisQA.AI
{
    [DisallowMultipleComponent]
    public sealed class AIWFCTrainingAdapter : MonoBehaviour
    {
        [SerializeField] private WFCGenerator generator;
        [SerializeField, Min(1)] private int regenerateEveryEpisodes = 10;
        [SerializeField] private int baseMapSeed = 73001;
        [SerializeField, Min(0f)] private float spawnHeight = 0.08f;
        [SerializeField] private bool excludeStairsAsSpawnPoints = true;

        [Header("Floor Curriculum")]
        [SerializeField] private bool useFloorCountCurriculum;
        [SerializeField, Range(0, 5)] private int defaultFloorLesson;
        [Tooltip("Phase 2: transfer lantai sebelum coverage seimbang. Phase 3 tetap dua lesson.")]
        [SerializeField] private bool useFloorTransferLesson;
        [Tooltip("Tanpa trainer: transfer pada stage 1-3; coverage 10%/20% pada stage 4/5.")]
        [SerializeField] private bool defaultGraduatedTransferLessons;
        [SerializeField, Min(1)] private int minimumTwoFloorStairCount = 1;
        [Header("Phase2T Preview (without trainer)")]
        [Tooltip("Untuk menonton model Phase2T. Trainer mengatur opsi ini melalui terrain_curriculum.")]
        [SerializeField] private bool defaultTerrainCurriculum;
        [SerializeField, Range(0, AITerrainCurriculum.LastLevel)] private int defaultTerrainLevel;
        public bool UsesFloorCountCurriculum => useFloorCountCurriculum;
        public bool IsTwoFloorLesson => useFloorCountCurriculum && CurrentTrainingStage >= 1;
        public bool IsFloorTransferLesson => useFloorCountCurriculum
            && useFloorTransferLesson
            && (CurrentTrainingStage == 1
                || (graduatedTransferLessons
                    && (CurrentTrainingStage == 2 || CurrentTrainingStage == 3)));
        public bool UsesGraduatedTransferLessons => graduatedTransferLessons;
        public bool IsGraduatedCoverageLesson => graduatedTransferLessons
            && IsTwoFloorLesson && !IsFloorTransferLesson;
        public float GraduatedFloorCoverageTarget => TerrainCurriculumEnabled
            ? AITerrainCurriculum.Coverage(TerrainEpisodeLevel)
            : CurrentTrainingStage == 4 ? 0.10f : 0.20f;
        public bool TerrainCurriculumEnabled { get; private set; }
        public int TerrainLevel { get; private set; }
        public int TerrainEpisodeLevel { get; private set; }
        public bool TerrainRehearsal { get; private set; }

        [Header("Multi Floor Spawn")]
        [SerializeField, Range(0f, 1f)] private float crossFloorSpawnChance = 0.75f;
        [SerializeField, Min(0.1f)] private float minimumFloorHeightDifference = 3f;

        [Header("Phase 3B Stair Curriculum")]
        [Tooltip("Gunakan rute socket WFC untuk memperbesar jarak spawn dari tangga secara bertahap.")]
        [SerializeField] private bool useStairSpawnCurriculum;
        [Tooltip("Tanpa trainer, stage 6 memakai spawn seluruh map dan target coverage 18%.")]
        [SerializeField, Range(0, 6)] private int defaultTrainingStage = 6;
        [Tooltip("Jumlah seed WFC yang dicoba ketika map baru tidak mempunyai rute tangga valid.")]
        [SerializeField, Min(1)] private int maximumTrainingMapAttempts = 6;
        public bool UsesStairSpawnCurriculum => useStairSpawnCurriculum;
        public bool UsesSingleFloorExplorationLesson =>
            useStairSpawnCurriculum && CurrentTrainingStage == 0;
        public bool RequiresEveryFloorCoverage => !UsesSingleFloorExplorationLesson;
        public bool RequiresStairTraversalForCoverage =>
            !UsesSingleFloorExplorationLesson;
        public int CurrentTrainingStage { get; private set; }
        public int MainTrainingStage { get; private set; }
        public bool StairPracticeEnabled { get; private set; }
        public int StairPracticeLevel { get; private set; }
        public bool IsStairRehearsal { get; private set; }
        public int EpisodePracticeLevel => IsStairRehearsal ? 0 : StairPracticeLevel;
        public bool NearSearchEnabled { get; private set; }
        public int NearSearchLevel { get; private set; }
        public bool MixedSearchEnabled { get; private set; }
        public int MixedSearchLevel { get; private set; }
        public int LastSpawnStairSteps { get; private set; }
        // Read-only diagnostics, never included in the policy observations.
        public Vector3 DiagnosticStairMouth { get; private set; }
        public int DiagnosticRouteSteps(Vector3 position)
        {
            return generator != null && searchDistances.TryGetValue(
                generator.WorldToWalkwayCell(position), out int steps) ? steps : -1;
        }
        public int GetFloorIndexAt(Vector3 worldPosition)
        {
            Transform root = generator != null ? generator.GeneratedRoot : null;
            if (root == null)
            {
                return -1;
            }

            float floorHeight = Mathf.Max(0.01f, generator.CellSize.y);
            return Mathf.Max(0,
                Mathf.RoundToInt(root.InverseTransformPoint(worldPosition).y / floorHeight));
        }

        public bool StairGuideEnabled => stairGuideEnabled;
        // Physical occupancy diagnostic; do not round a halfway-up ramp into a floor.
        public int DiagnosticFlatFloorAt(Vector3 feet)
        {
            Transform root = generator != null ? generator.GeneratedRoot : null;
            if (root == null) return -1;
            Vector3 local = root.InverseTransformPoint(feet);
            float height = Mathf.Max(.01f, generator.CellSize.y);
            int floor = Mathf.RoundToInt(local.y / height);
            return floor >= 0 && floor <= 1 && Mathf.Abs(local.y - floor * height) <= .35f
                ? floor : -1;
        }
        public float StairTaskDirectionObservation { get; private set; }
        public float CurriculumCoverageTarget => CurrentTrainingStage == 0 ? 0.10f
            : CurrentTrainingStage == 1 ? 0.02f
            : CurrentTrainingStage == 2 ? 0.05f
            : CurrentTrainingStage == 3 ? 0.08f
            : CurrentTrainingStage == 4 ? 0.10f
            : CurrentTrainingStage == 5 ? 0.14f : 0.18f;

        [Header("Training Colliders")]
        [SerializeField] private bool addMeshColliders = true;
        [SerializeField] private bool addSmoothStairRamp = true;
        [SerializeField, Min(0.5f)] private float stairRampWidth = 4f;
        [SerializeField] private bool addExplorationAreaTriggers = true;

        private readonly List<Vector3> walkablePositions = new List<Vector3>();
        private int generationIndex;
        private int lastGeneratedSeed;
        public int LastGeneratedSeed => lastGeneratedSeed;
        // Evaluation-only entry point: fixed map, no curriculum/regeneration RNG.
        public bool PrepareDefectExperiment(int requestedSeed, DefectMeshLibrary library, List<Mesh> ownedMeshes)
        {
            TerrainCurriculumEnabled = true;
            TerrainLevel = TerrainEpisodeLevel = 9;
            TerrainRehearsal = false;
            graduatedTransferLessons = true;
            CurrentTrainingStage = MainTrainingStage = 5;
            generator.ConfigureRuntimeFloors(2, minimumTwoFloorStairCount);
            if (!generator.GenerateWithSeed(requestedSeed)) return false;
            lastGeneratedSeed = generator.LastUsedSeed;
            // Player builds cannot cook inaccessible imported meshes at runtime.
            // Use the same baked vertex/index data, shared per source mesh. Rendering
            // retains its original imported mesh and training uses its original path.
            var readable = new Dictionary<Mesh, Mesh>();
            foreach (Transform tile in generator.GeneratedRoot)
            {
                if (tile.name.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                foreach (var filter in tile.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh source = filter.sharedMesh;
                    if (source == null) continue;
                    var existing = filter.GetComponent<Collider>();
                    if (existing != null && !(existing is MeshCollider)) continue;
                    if (!readable.TryGetValue(source, out Mesh mesh))
                    {
                        var data = library.Find(source);
                        if (data == null) throw new InvalidOperationException("Missing collision geometry: " + source.name);
                        mesh = new Mesh { name = "Evaluation collision " + source.name,
                            indexFormat = data.vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                        mesh.vertices = data.vertices; mesh.triangles = data.triangles; mesh.RecalculateBounds();
                        readable[source] = mesh; ownedMeshes.Add(mesh);
                    }
                    var collider = existing as MeshCollider;
                    if (collider == null)
                    {
                        // Avoid implicit MeshCollider initialization from an unreadable
                        // co-located MeshFilter before sharedMesh is assigned explicitly.
                        filter.sharedMesh = mesh;
                        try { collider = filter.gameObject.AddComponent<MeshCollider>(); }
                        finally { filter.sharedMesh = source; }
                        collider.convex = false;
                    }
                    collider.sharedMesh = mesh;
                }
            }
            EnsureTrainingColliders();
            return true;
        }
        private int currentEpisodeIndex;
        private int lastPreparedStage = -1;
        private bool graduatedTransferLessons;
        private Vector3 curriculumSpawnForward;
        private readonly HashSet<Vector3Int> invalidStairCells = new HashSet<Vector3Int>();
        private readonly Dictionary<Vector3Int, Vector3Int> stairGuideNextCell =
            new Dictionary<Vector3Int, Vector3Int>();
        private Vector3Int stairGuideMouth;
        private Vector3Int stairGuideEntry;
        private Vector3Int stairGuideExit;
        private bool stairGuideEnabled;
        private readonly Dictionary<Vector3Int, int> searchDistances = new Dictionary<Vector3Int, int>();
        private Vector3Int searchMouth;
        private Vector3Int practiceEntry;
        private Vector3Int practiceDestination;
        private bool hasPracticeRoute;
        private float practiceApproachDistance;

        private struct SpawnCandidate
        {
            public Vector3Int cell;
            public int steps;
        }

        public bool PrepareEpisode(int episodeIndex)
        {
            currentEpisodeIndex = episodeIndex;
            TerrainCurriculumEnabled = Academy.IsInitialized
                ? Academy.Instance.EnvironmentParameters.GetWithDefault("terrain_curriculum",
                    defaultTerrainCurriculum ? 1f : 0f) >= 0.5f : defaultTerrainCurriculum;
            if (TerrainCurriculumEnabled && (!useFloorCountCurriculum || !useFloorTransferLesson
                || Academy.IsInitialized && Academy.Instance.IsCommunicatorOn
                    && Academy.Instance.EnvironmentParameters.GetWithDefault("terrain_driver", 0f) < 0.5f))
            {
                Debug.LogError("Phase2T: gunakan train_phase2_terrain.py dan scene AI_Phase2_SmallWFC dengan floor/transfer curriculum aktif.", this);
                return false;
            }
            graduatedTransferLessons = useFloorCountCurriculum
                && useFloorTransferLesson && defaultGraduatedTransferLessons;
            if (useFloorCountCurriculum && useFloorTransferLesson
                && Academy.IsInitialized)
            {
                graduatedTransferLessons = Academy.Instance.EnvironmentParameters
                    .GetWithDefault("graduated_transfer",
                        graduatedTransferLessons ? 1f : 0f) >= 0.5f;
            }
            CurrentTrainingStage = useFloorCountCurriculum
                ? defaultFloorLesson : defaultTrainingStage;
            if (useFloorCountCurriculum && Academy.IsInitialized)
            {
                CurrentTrainingStage = Mathf.Clamp(Mathf.RoundToInt(
                    Academy.Instance.EnvironmentParameters.GetWithDefault(
                        "floor_lesson", defaultFloorLesson)), 0,
                    useFloorTransferLesson
                        ? graduatedTransferLessons ? 5 : 2
                        : 1);
            }
            else if (useStairSpawnCurriculum && Academy.IsInitialized)
            {
                CurrentTrainingStage = Mathf.Clamp(Mathf.RoundToInt(
                    Academy.Instance.EnvironmentParameters.GetWithDefault(
                        "wfc_training_stage", defaultTrainingStage)), 0, 6);
            }
            // This independent curriculum key leaves floor_lesson unused until
            // the gated coverage resume begins, so its saved index starts at 0.
            if (graduatedTransferLessons && Academy.IsInitialized)
            {
                if (Academy.Instance.EnvironmentParameters.GetWithDefault("mastery_protocol", 0f) >= 1f
                    && Academy.Instance.EnvironmentParameters.GetWithDefault("mastery_driver", 0f) < 1f)
                {
                    Debug.LogError("Phase 2 mastery: gunakan uv run python "
                        + "Assets/Scripts/AI/Training/train_phase2.py <config> --run-id=Phase2M4. "
                        + "Command mlagents-learn biasa tidak menjalankan pemeriksaan mastery.", this);
                    return false;
                }
                int searchLesson = Mathf.RoundToInt(Academy.Instance.EnvironmentParameters
                    .GetWithDefault("stair_search_lesson", -1f));
                if (searchLesson >= 0) CurrentTrainingStage = Mathf.Clamp(searchLesson, 0, 3);
                bool requireGate = Academy.Instance.EnvironmentParameters
                    .GetWithDefault("require_stair_eval_gate", 0f) >= 0.5f;
                bool passedGate = Academy.Instance.EnvironmentParameters
                    .GetWithDefault("stair_eval_gate_passed", 0f) >= 0.5f;
                if (requireGate && !passedGate && CurrentTrainingStage >= 4)
                    CurrentTrainingStage = 3;
            }
            MainTrainingStage = CurrentTrainingStage;
            MixedSearchEnabled = graduatedTransferLessons && Academy.IsInitialized
                && Academy.Instance.EnvironmentParameters.GetWithDefault("mixed_search_curriculum", 0f) >= 0.5f;
            MixedSearchLevel = Academy.IsInitialized ? Mathf.Clamp(Mathf.RoundToInt(
                Academy.Instance.EnvironmentParameters.GetWithDefault("mixed_search_level", 0f)),
                0, AIMixedSearchSchedule.LastLevel) : 0;
            if (MixedSearchEnabled && Academy.Instance.EnvironmentParameters.GetWithDefault("mixed_search_driver", 0f) < 0.5f)
            {
                Debug.LogError("Mixed search: gunakan train_phase2.py dengan konfigurasi Phase2M5.", this);
                return false;
            }
            if (MixedSearchEnabled && generationIndex == 0)
                baseMapSeed = Mathf.RoundToInt(Academy.Instance.EnvironmentParameters
                    .GetWithDefault("mixed_search_map_seed", 173003f));
            NearSearchEnabled = graduatedTransferLessons && Academy.IsInitialized
                && Academy.Instance.EnvironmentParameters.GetWithDefault("near_search_curriculum", 0f) >= 0.5f;
            NearSearchLevel = Academy.IsInitialized ? Mathf.Clamp(Mathf.RoundToInt(
                Academy.Instance.EnvironmentParameters.GetWithDefault("near_search_level", 0f)), 0, AINearSearchSchedule.LastLevel) : 0;
            if (NearSearchEnabled && Academy.Instance.EnvironmentParameters.GetWithDefault("near_search_driver", 0f) < 0.5f)
            {
                Debug.LogError("Near-search curriculum: hentikan trainer lama lalu resume menggunakan train_phase2.py terbaru.", this);
                return false;
            }
            StairPracticeEnabled = graduatedTransferLessons && Academy.IsInitialized
                && Academy.Instance.EnvironmentParameters.GetWithDefault("stair_practice", 0f) >= 0.5f;
            StairPracticeLevel = Academy.IsInitialized ? Mathf.Clamp(Mathf.RoundToInt(
                Academy.Instance.EnvironmentParameters.GetWithDefault("stair_practice_level", 0f)), 0, 2) : 0;
            // Blocked rehearsal batches reduce floor/map rebuilding compared to
            // alternating the number of floors every other episode.
            IsStairRehearsal = StairPracticeEnabled
                && (MainTrainingStage == 0 && episodeIndex % 20 >= 15
                    || (MainTrainingStage == 2 || MainTrainingStage == 3) && episodeIndex % 20 >= 16);
            if (IsStairRehearsal) CurrentTrainingStage = 1;
            if (TerrainCurriculumEnabled)
            {
                // Separate schema/curriculum branch: old runs keep their exact sampling.
                graduatedTransferLessons = true;
                TerrainLevel = Academy.IsInitialized ? Mathf.Clamp(Mathf.RoundToInt(Academy.Instance.EnvironmentParameters
                    .GetWithDefault("terrain_level", defaultTerrainLevel)), 0, AITerrainCurriculum.LastLevel) : defaultTerrainLevel;
                bool training = Academy.IsInitialized
                    && Academy.Instance.EnvironmentParameters.GetWithDefault("terrain_driver", 0f) >= 0.5f;
                bool phase3 = Academy.IsInitialized
                    && Academy.Instance.EnvironmentParameters.GetWithDefault("phase3_curriculum", 0f) >= 0.5f;
                // Phase 3 starts at balanced coverage, never rehearses guided transfer
                // level 7. At 20%, retain 20% rehearsal of balanced coverage 10%.
                TerrainEpisodeLevel = training && !(phase3 && TerrainLevel == 8)
                    ? AITerrainCurriculum.EpisodeLevel(TerrainLevel, episodeIndex) : TerrainLevel;
                TerrainRehearsal = TerrainEpisodeLevel != TerrainLevel;
                MainTrainingStage = AITerrainCurriculum.Stage(TerrainLevel);
                CurrentTrainingStage = AITerrainCurriculum.Stage(TerrainEpisodeLevel);
                StairPracticeEnabled = NearSearchEnabled = MixedSearchEnabled = IsStairRehearsal = false;
                if (generationIndex == 0 && Academy.IsInitialized) baseMapSeed = Mathf.RoundToInt(Academy.Instance.EnvironmentParameters
                    .GetWithDefault("terrain_map_seed", 283003f));
            }
            hasPracticeRoute = false;
            LastSpawnStairSteps = -1;
            StairTaskDirectionObservation = 0f;
            curriculumSpawnForward = Vector3.zero;
            stairGuideEnabled = false;
            stairGuideNextCell.Clear();
            searchDistances.Clear();
            if (generator == null)
            {
                Debug.LogError("AI WFC Adapter: WFCGenerator belum diisi.", this);
                return false;
            }

            bool singleFloorLesson = useFloorCountCurriculum
                ? !IsTwoFloorLesson : UsesSingleFloorExplorationLesson;
            bool layoutChanged = (useStairSpawnCurriculum || useFloorCountCurriculum)
                && generator.ConfigureRuntimeFloors(
                    singleFloorLesson ? 1 : 2,
                    singleFloorLesson ? 0 : useFloorCountCurriculum
                        ? minimumTwoFloorStairCount : 1);
            bool stageChanged = CurrentTrainingStage != lastPreparedStage;
            lastPreparedStage = CurrentTrainingStage;
            bool mustGenerate = layoutChanged
                || (stageChanged && !TerrainCurriculumEnabled)
                || walkablePositions.Count < 2
                || episodeIndex % regenerateEveryEpisodes == 0;
            if (mustGenerate && (useStairSpawnCurriculum || useFloorCountCurriculum))
            {
                if (singleFloorLesson)
                {
                    if (!GenerateNextMap())
                    {
                        return false;
                    }

                    EnsureTrainingColliders();
                    RefreshWalkablePositions();
                    return walkablePositions.Count >= 2;
                }

                return TryGenerateUsableStairMap();
            }

            if (mustGenerate)
            {
                if (!GenerateNextMap())
                {
                    return false;
                }
            }

            EnsureTrainingColliders();
            RefreshWalkablePositions();
            return walkablePositions.Count >= 2;
        }

        private bool GenerateNextMap()
        {
            lastGeneratedSeed = unchecked(baseMapSeed + generationIndex * 7919);
            generationIndex++;
            while (MixedSearchEnabled && AIMixedSearchSchedule.IsHeldOutSeed(lastGeneratedSeed))
            {
                lastGeneratedSeed = unchecked(baseMapSeed + generationIndex * 7919);
                generationIndex++;
            }
            return generator.GenerateWithSeed(lastGeneratedSeed);
        }

        private bool TryGenerateUsableStairMap()
        {
            int lastWalkableCount = 0;
            int lastStairCellCount = 0;
            int lastConnectedStairPairCount = 0;
            int lastMouthPairCount = 0;
            for (int attempt = 1; attempt <= maximumTrainingMapAttempts; attempt++)
            {
                if (!GenerateNextMap())
                {
                    Debug.LogWarning(
                        $"AI WFC: seed {lastGeneratedSeed} gagal digenerate; "
                        + $"mencoba seed berikutnya ({attempt}/{maximumTrainingMapAttempts}).",
                        this);
                    continue;
                }

                EnsureTrainingColliders();
                RefreshWalkablePositions();
                ValidateStairGeometry(false);

                var lowerMouths = new List<Vector3Int>();
                var upperMouths = new List<Vector3Int>();
                bool hasStairRoute = TryCollectStairMouthPairs(
                    lowerMouths,
                    upperMouths,
                    out lastStairCellCount,
                    out lastConnectedStairPairCount);
                lastWalkableCount = walkablePositions.Count;
                lastMouthPairCount = lowerMouths.Count;
                if (lastWalkableCount >= 2 && hasStairRoute)
                {
                    if (attempt > 1)
                    {
                        Debug.Log(
                            $"AI WFC: map pengganti diterima dengan seed {lastGeneratedSeed} "
                            + $"pada percobaan {attempt}/{maximumTrainingMapAttempts}; "
                            + $"pasangan akses tangga={lastMouthPairCount}.",
                            this);
                    }
                    return true;
                }

                Debug.LogWarning(
                    $"AI WFC: menolak seed {lastGeneratedSeed} "
                    + $"({attempt}/{maximumTrainingMapAttempts}). "
                    + $"walkable={lastWalkableCount}, sel tangga={lastStairCellCount}, "
                    + $"pasangan tangga vertikal={lastConnectedStairPairCount}, "
                    + $"pasangan akses valid={lastMouthPairCount}, "
                    + $"collider tangga bermasalah={invalidStairCells.Count}.",
                    this);
            }

            Debug.LogError(
                $"AI WFC: tidak menemukan map dua lantai yang valid setelah "
                + $"{maximumTrainingMapAttempts} seed. Seed terakhir={lastGeneratedSeed}, "
                + $"walkable={lastWalkableCount}, sel tangga={lastStairCellCount}, "
                + $"pasangan tangga vertikal={lastConnectedStairPairCount}, "
                + $"pasangan akses valid={lastMouthPairCount}, "
                + $"collider tangga bermasalah={invalidStairCells.Count}.",
                this);
            return false;
        }

        public bool TryChooseSpawnPair(
            System.Random random,
            float minimumDistance,
            float maximumDistance,
            out Vector3 agentPosition,
            out Vector3 targetPosition)
        {
            agentPosition = default;
            targetPosition = default;
            if (walkablePositions.Count < 2)
            {
                return false;
            }

            if (useStairSpawnCurriculum)
            {
                if (UsesSingleFloorExplorationLesson)
                {
                    // The first lesson teaches coverage and anti-loop behaviour on a
                    // simpler one-floor WFC before stairs are introduced.
                    return TryChooseSpawnPairInternal(
                        random,
                        minimumDistance,
                        maximumDistance,
                        false,
                        512,
                        out agentPosition,
                        out targetPosition)
                        || TryChooseSpawnPairInternal(
                            random,
                            0f,
                            0f,
                            false,
                            256,
                            out agentPosition,
                            out targetPosition);
                }

                // Guided lessons intentionally use path steps instead of Euclidean spawn limits.
                return TryChooseStairSpawnPair(random, out agentPosition, out targetPosition);
            }

            if (IsFloorTransferLesson)
            {
                // Socket path steps define graduated transfer distance. Do not
                // let the Euclidean spawn limit remove near-stair examples.
                return TryChooseConnectedFloorTransferPair(random,
                    graduatedTransferLessons ? 0f : minimumDistance,
                    graduatedTransferLessons ? 0f : maximumDistance,
                    out agentPosition, out targetPosition);
            }

            bool requireCrossFloor = HasCrossFloorCandidates()
                && random.NextDouble() < crossFloorSpawnChance;
            if (TryChooseSpawnPairInternal(
                    random,
                    minimumDistance,
                    maximumDistance,
                    requireCrossFloor,
                    512,
                    out agentPosition,
                    out targetPosition))
            {
                return true;
            }

            // Curriculum jarak yang ketat dapat membuat pasangan lintas lantai
            // sementara tidak tersedia. Jangan batalkan episode: gunakan pasangan
            // valid mana pun sebagai fallback dan coba lintas lantai lagi berikutnya.
            return requireCrossFloor && TryChooseSpawnPairInternal(
                random,
                minimumDistance,
                maximumDistance,
                false,
                256,
                out agentPosition,
                out targetPosition);
        }

        private bool TryChooseConnectedFloorTransferPair(
            System.Random random,
            float minimumDistance,
            float maximumDistance,
            out Vector3 agentPosition,
            out Vector3 targetPosition)
        {
            agentPosition = default;
            targetPosition = default;
            var lowerMouths = new List<Vector3Int>();
            var upperMouths = new List<Vector3Int>();
            if (!TryCollectStairMouthPairs(
                    lowerMouths, upperMouths, out _, out _))
            {
                return false;
            }

            // Select both positions from the same validated stair component.
            // A random pair of walkable tiles can otherwise be cross-floor yet
            // impossible to connect on a WFC map with separate components.
            // The optional new curriculum teaches ascent first. The next
            // transfer stage reintroduces descent without changing the policy
            // observation or forcing its movement actions.
            bool ascend = TerrainCurriculumEnabled ? currentEpisodeIndex % 2 == 0 : graduatedTransferLessons
                ? CurrentTrainingStage == 1 || currentEpisodeIndex % 4 != 3
                : currentEpisodeIndex % 2 == 0;
            int firstRoute = random.Next(lowerMouths.Count);
            for (int offset = 0; offset < lowerMouths.Count; offset++)
            {
                int route = (firstRoute + offset) % lowerMouths.Count;
                Vector3Int startMouth = ascend
                    ? lowerMouths[route] : upperMouths[route];
                Vector3Int destinationMouth = ascend
                    ? upperMouths[route] : lowerMouths[route];
                int maximumStairSteps = graduatedTransferLessons
                    ? CurrentTrainingStage == 1 ? 1 : CurrentTrainingStage == 2 ? 2 : 4
                    : int.MaxValue;
                if (TerrainCurriculumEnabled) maximumStairSteps = AITerrainCurriculum.MaxTiles(TerrainEpisodeLevel);
                if (StairPracticeEnabled && CurrentTrainingStage == 1 && EpisodePracticeLevel < 2)
                    maximumStairSteps = 0;
                if (NearSearchEnabled && CurrentTrainingStage == 2)
                    maximumStairSteps = AINearSearchSchedule.MaxTiles(NearSearchLevel);
                if (MixedSearchEnabled && CurrentTrainingStage == 3)
                    maximumStairSteps = AIMixedSearchSchedule.MaxTiles(MixedSearchLevel);
                List<SpawnCandidate> starts = FindFloorCandidates(
                    startMouth, maximumStairSteps, !graduatedTransferLessons);
                if (starts.Count == 0) continue;
                if (graduatedTransferLessons && (CurrentTrainingStage == 3
                    || TerrainCurriculumEnabled && TerrainEpisodeLevel >= 6))
                {
                    // A 50/50 near/far mixture keeps easy examples even when
                    // the branching outer ring contains most candidate cells.
                    float farProbability = TerrainCurriculumEnabled
                        ? AITerrainCurriculum.FarProbability(TerrainEpisodeLevel) : MixedSearchEnabled
                        ? AIMixedSearchSchedule.FarProbability(MixedSearchLevel) : 0.5f;
                    // Preserve the exact historical RNG mapping when the opt-in
                    // bridge is off, including paired held-out evaluations.
                    bool sampleNear = MixedSearchEnabled || TerrainCurriculumEnabled
                        ? random.NextDouble() >= farProbability : random.NextDouble() < 0.5;
                    List<SpawnCandidate> band = starts.FindAll(candidate =>
                        sampleNear ? candidate.steps <= 2 : candidate.steps > 2);
                    if (band.Count > 0) starts = band;
                }
                Vector3 destination = generator.WalkwayCellToWorld(destinationMouth)
                    + Vector3.up * spawnHeight;
                int firstStart = random.Next(starts.Count);
                for (int candidate = 0; candidate < starts.Count; candidate++)
                {
                    SpawnCandidate start = starts[(firstStart + candidate) % starts.Count];
                    Vector3 origin = generator.WalkwayCellToWorld(start.cell)
                        + Vector3.up * spawnHeight;
                    float distance = Vector3.Distance(origin, destination);
                    if (distance < minimumDistance
                        || (maximumDistance > 0f && distance > maximumDistance))
                    {
                        continue;
                    }

                    agentPosition = origin;
                    targetPosition = destination;
                    LastSpawnStairSteps = start.steps;
                    DiagnosticStairMouth = generator.WalkwayCellToWorld(startMouth);
                    if (graduatedTransferLessons) BuildSearchDistances(startMouth);
                    if (StairPracticeEnabled && TryFindStairEntry(startMouth, destinationMouth, out Vector3Int selectedEntry))
                    {
                        hasPracticeRoute = true;
                        practiceEntry = selectedEntry;
                        practiceDestination = destinationMouth;
                        if (CurrentTrainingStage == 1)
                        {
                            stairGuideMouth = startMouth;
                            stairGuideEntry = selectedEntry;
                            stairGuideExit = destinationMouth;
                            BuildStairGuideNextCells(startMouth);
                            stairGuideEnabled = EpisodePracticeLevel == 0
                                || EpisodePracticeLevel == 1 && random.NextDouble() < 0.5;
                            // Stay inside the existing mouth tile, approaching the
                            // foot of the ramp without changing any prefab/collider.
                            Vector3 approach = generator.WalkwayCellToWorld(selectedEntry) - origin;
                            approach.y = 0f;
                            if (approach.sqrMagnitude > 0.01f && EpisodePracticeLevel < 2)
                                agentPosition += approach.normalized * generator.CellSize.x
                                    * (EpisodePracticeLevel == 0 ? 0.3f : 0.15f);
                        }
                    }
                    if (graduatedTransferLessons
                        && (CurrentTrainingStage == 1 || NearSearchEnabled && CurrentTrainingStage == 2
                            || TerrainCurriculumEnabled)
                        && TryFindStairEntry(
                            startMouth, destinationMouth, out Vector3Int entry))
                    {
                        Vector3Int headingCell = entry;
                        if ((NearSearchEnabled && CurrentTrainingStage == 2 || TerrainCurriculumEnabled) && start.steps > 0)
                        {
                            // Face the first legal route segment, not a diagonal
                            // line through a corner wall toward the distant stair.
                            foreach (Vector3Int next in generator.WalkwayConnections[start.cell])
                                if (searchDistances.TryGetValue(next, out int remaining) && remaining == start.steps - 1)
                                {
                                    headingCell = next;
                                    break;
                                }
                        }
                        curriculumSpawnForward = generator.WalkwayCellToWorld(headingCell)
                            - agentPosition;
                        curriculumSpawnForward.y = 0f;
                        curriculumSpawnForward.Normalize();
                    }
                    if (hasPracticeRoute)
                    {
                        Vector3 approachOffset = generator.WalkwayCellToWorld(practiceEntry) - agentPosition;
                        approachOffset.y = 0f;
                        practiceApproachDistance = Mathf.Max(0.1f, approachOffset.magnitude);
                    }
                    return true;
                }
            }

            if (minimumDistance > 0f || maximumDistance > 0f)
            {
                // Keep the connected staircase route even if a tight spawn
                // distance cannot be met on a small generated component.
                return TryChooseConnectedFloorTransferPair(
                    random, 0f, 0f, out agentPosition, out targetPosition);
            }

            Debug.LogError("AI WFC: tidak menemukan spawn lintas lantai yang "
                + "terhubung ke tangga untuk lesson transfer.", this);
            return false;
        }

        private void BuildSearchDistances(Vector3Int mouth)
        {
            searchMouth = mouth;
            searchDistances.Clear();
            var queue = new Queue<Vector3Int>();
            searchDistances[mouth] = 0;
            queue.Enqueue(mouth);
            while (queue.Count > 0)
            {
                Vector3Int cell = queue.Dequeue();
                foreach (Vector3Int next in generator.WalkwayConnections[cell])
                {
                    if (next.y != mouth.y || generator.IsStairCell(next) || searchDistances.ContainsKey(next)) continue;
                    searchDistances[next] = searchDistances[cell] + 1;
                    queue.Enqueue(next);
                }
            }
        }

        public bool TryGetStairSearchDistance(Vector3 position, out float distance)
        {
            distance = 0f;
            if (!IsFloorTransferLesson || !graduatedTransferLessons || generator == null) return false;
            Vector3Int cell = generator.WorldToWalkwayCell(position);
            if (StairPracticeEnabled && hasPracticeRoute
                && (cell == searchMouth || cell == practiceEntry
                    || cell == practiceEntry + Vector3Int.up || cell == practiceEntry + Vector3Int.down
                    || cell == practiceDestination))
            {
                distance = Vector3.Distance(position, generator.WalkwayCellToWorld(practiceDestination))
                    / Mathf.Max(0.01f, generator.CellSize.x);
                return true;
            }
            if (!searchDistances.TryGetValue(cell, out int steps)) return false;
            float traversalDistance = StairPracticeEnabled && hasPracticeRoute
                ? Vector3.Distance(generator.WalkwayCellToWorld(searchMouth),
                    generator.WalkwayCellToWorld(practiceDestination)) / Mathf.Max(0.01f, generator.CellSize.x)
                : 0f;
            Vector3 center = generator.WalkwayCellToWorld(cell);
            position.y = center.y;
            if (steps == 0)
            {
                distance = traversalDistance + Vector3.Distance(position, generator.WalkwayCellToWorld(searchMouth))
                    / Mathf.Max(0.01f, generator.CellSize.x);
                return true;
            }
            float best = float.PositiveInfinity;
            foreach (Vector3Int next in generator.WalkwayConnections[cell])
            {
                if (!searchDistances.TryGetValue(next, out int remaining) || remaining != steps - 1) continue;
                Vector3 nextCenter = generator.WalkwayCellToWorld(next);
                float edgeLength = Mathf.Max(0.01f, Vector3.Distance(center, nextCenter));
                best = Mathf.Min(best, remaining + Vector3.Distance(position, nextCenter) / edgeLength);
            }
            if (float.IsInfinity(best)) return false;
            distance = best + traversalDistance;
            return true;
        }

        public bool TryGetStairPracticeFraction(Vector3 position, out float fraction)
        {
            fraction = 0f;
            if (!StairPracticeEnabled || !hasPracticeRoute || !IsFloorTransferLesson) return false;
            Vector3Int cell = generator.WorldToWalkwayCell(position);
            if (cell != searchMouth && cell != practiceDestination
                && cell != practiceEntry && cell != practiceEntry + Vector3Int.up
                && cell != practiceEntry + Vector3Int.down) return false;
            fraction = AIStairPracticeProgress.Fraction(position,
                generator.WalkwayCellToWorld(searchMouth), generator.WalkwayCellToWorld(practiceDestination));
            return true;
        }

        public bool TryGetStairApproachFraction(Vector3 position, out float fraction)
        {
            fraction = 0f;
            if (!StairPracticeEnabled || !hasPracticeRoute || CurrentTrainingStage != 1) return false;
            Vector3Int cell = generator.WorldToWalkwayCell(position);
            if (cell != searchMouth && cell != practiceEntry && cell != practiceEntry + Vector3Int.up) return false;
            Vector3 offset = position - generator.WalkwayCellToWorld(practiceEntry);
            offset.y = 0f;
            fraction = Mathf.Clamp01(1f - offset.magnitude / practiceApproachDistance);
            return true;
        }

        private bool TryChooseSpawnPairInternal(
            System.Random random,
            float minimumDistance,
            float maximumDistance,
            bool requireCrossFloor,
            int maximumAttempts,
            out Vector3 agentPosition,
            out Vector3 targetPosition)
        {
            agentPosition = default;
            targetPosition = default;
            for (int attempt = 0; attempt < maximumAttempts; attempt++)
            {
                Vector3 first = walkablePositions[random.Next(walkablePositions.Count)];
                Vector3 second = walkablePositions[random.Next(walkablePositions.Count)];
                float distance = Vector3.Distance(first, second);
                if (distance < minimumDistance
                    || (maximumDistance > 0f && distance > maximumDistance)
                    || (requireCrossFloor
                        && Mathf.Abs(first.y - second.y) < minimumFloorHeightDifference))
                {
                    continue;
                }

                agentPosition = first;
                targetPosition = second;
                return true;
            }

            return false;
        }

        private bool TryChooseStairSpawnPair(System.Random random,
            out Vector3 agentPosition, out Vector3 targetPosition)
        {
            agentPosition = default;
            targetPosition = default;
            var lowerMouths = new List<Vector3Int>();
            var upperMouths = new List<Vector3Int>();
            if (!TryCollectStairMouthPairs(
                    lowerMouths,
                    upperMouths,
                    out int stairCellCount,
                    out int connectedStairPairCount))
            {
                Debug.LogError(
                    $"AI WFC: map seed {lastGeneratedSeed} kehilangan rute tangga valid "
                    + $"setelah lolos validasi. Sel tangga={stairCellCount}, "
                    + $"pasangan tangga vertikal={connectedStairPairCount}, "
                    + $"collider tangga bermasalah={invalidStairCells.Count}.",
                    this);
                return false;
            }

            int route = random.Next(lowerMouths.Count);
            int radius = CurrentTrainingStage <= 1 ? 0
                : CurrentTrainingStage == 2 ? 1
                : CurrentTrainingStage == 3 ? 2
                : CurrentTrainingStage == 4 ? 3
                : CurrentTrainingStage == 5 ? 5 : int.MaxValue;
            // Lessons 1-2 build on Phase 3A by teaching a complete sequence:
            // explore the lower floor, find the stair, ascend, then explore upstairs.
            // Descent is introduced gradually so it is retained without dominating
            // the early stair-search lessons.
            bool descending = IsDescendingCurriculumEpisode();
            Vector3Int startMouth = descending ? upperMouths[route] : lowerMouths[route];
            Vector3Int targetMouth = descending ? lowerMouths[route] : upperMouths[route];
            if (!TryFindStairEntry(startMouth, targetMouth, out Vector3Int entry))
            {
                Debug.LogError("AI WFC: pasangan akses tangga tidak memiliki sambungan masuk yang cocok.", this);
                return false;
            }
            List<SpawnCandidate> starts = FindFloorCandidates(startMouth, radius);
            SpawnCandidate start = starts[random.Next(starts.Count)];
            Vector3Int destination = targetMouth;
            if (CurrentTrainingStage >= 3)
            {
                List<SpawnCandidate> targets = FindFloorCandidates(targetMouth, radius);
                destination = targets[random.Next(targets.Count)].cell;
            }

            LastSpawnStairSteps = start.steps;
            agentPosition = generator.WalkwayCellToWorld(start.cell) + Vector3.up * spawnHeight;
            targetPosition = generator.WalkwayCellToWorld(destination) + Vector3.up * spawnHeight;
            StairTaskDirectionObservation = descending ? -1f : 1f;

            stairGuideMouth = startMouth;
            stairGuideEntry = entry;
            stairGuideExit = targetMouth;
            BuildStairGuideNextCells(startMouth);
            float guideChance = CurrentTrainingStage == 1 ? 1f
                : CurrentTrainingStage == 2 ? 0.75f
                : CurrentTrainingStage == 3 ? 0.5f
                : CurrentTrainingStage == 4 ? 0.25f : 0f;
            stairGuideEnabled = random.NextDouble() < guideChance;

            Vector3Int firstGuideCell = start.steps > 0
                && stairGuideNextCell.TryGetValue(start.cell, out Vector3Int nextCell)
                    ? nextCell : stairGuideEntry;
            Vector3 guidePosition = generator.WalkwayCellToWorld(firstGuideCell);
            curriculumSpawnForward = guidePosition - agentPosition;
            curriculumSpawnForward.y = 0f;
            if (curriculumSpawnForward.sqrMagnitude < 0.001f)
            {
                curriculumSpawnForward = targetPosition - agentPosition;
                curriculumSpawnForward.y = 0f;
            }
            curriculumSpawnForward.Normalize();
            return true;
        }

        public bool TryGetStairGuideWaypoint(Vector3 agentPosition,
            bool enteredStair, out Vector3 waypoint)
        {
            waypoint = default;
            if (!stairGuideEnabled || generator == null)
            {
                return false;
            }

            Vector3Int current = generator.WorldToWalkwayCell(agentPosition);
            Vector3Int next = enteredStair || generator.IsStairCell(current)
                ? stairGuideExit
                : current == stairGuideMouth
                    ? stairGuideEntry
                    : stairGuideNextCell.TryGetValue(current, out Vector3Int neighbour)
                        ? neighbour : stairGuideMouth;
            waypoint = generator.WalkwayCellToWorld(next);
            return true;
        }

        private bool TryFindStairEntry(Vector3Int startMouth,
            Vector3Int targetMouth, out Vector3Int entry)
        {
            entry = default;
            if (!generator.WalkwayConnections.TryGetValue(startMouth,
                    out List<Vector3Int> links))
            {
                return false;
            }

            foreach (Vector3Int candidate in links)
            {
                if (candidate.y != startMouth.y || !generator.IsStairCell(candidate)
                    || !generator.WalkwayConnections.TryGetValue(candidate,
                        out List<Vector3Int> stairLinks)) continue;

                foreach (Vector3Int opposite in stairLinks)
                {
                    if (opposite.y != targetMouth.y
                        || !generator.IsStairCell(opposite)
                        || !generator.WalkwayConnections.TryGetValue(opposite,
                            out List<Vector3Int> oppositeLinks)
                        || !oppositeLinks.Contains(targetMouth)) continue;
                    entry = candidate;
                    return true;
                }
            }

            return false;
        }

        private void BuildStairGuideNextCells(Vector3Int mouth)
        {
            stairGuideNextCell.Clear();
            var visited = new HashSet<Vector3Int> { mouth };
            var queue = new Queue<Vector3Int>();
            queue.Enqueue(mouth);
            while (queue.Count > 0)
            {
                Vector3Int current = queue.Dequeue();
                foreach (Vector3Int neighbour in generator.WalkwayConnections[current])
                {
                    if (neighbour.y != mouth.y || generator.IsStairCell(neighbour)
                        || !visited.Add(neighbour)) continue;
                    stairGuideNextCell.Add(neighbour, current);
                    queue.Enqueue(neighbour);
                }
            }
        }

        public bool TryGetCurriculumSpawnRotation(
            System.Random random,
            out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (TerrainCurriculumEnabled && IsFloorTransferLesson
                && curriculumSpawnForward.sqrMagnitude >= 0.5f)
            {
                float yaw = ((float)random.NextDouble() * 2f - 1f)
                    * AITerrainCurriculum.YawRange(TerrainEpisodeLevel);
                rotation = Quaternion.LookRotation(curriculumSpawnForward, Vector3.up)
                    * Quaternion.Euler(0f, yaw, 0f);
                return true;
            }
            if (NearSearchEnabled && CurrentTrainingStage == 2
                && curriculumSpawnForward.sqrMagnitude >= 0.5f)
            {
                float yaw = ((float)random.NextDouble() * 2f - 1f)
                    * AINearSearchSchedule.YawRange(NearSearchLevel);
                rotation = Quaternion.LookRotation(curriculumSpawnForward, Vector3.up)
                    * Quaternion.Euler(0f, yaw, 0f);
                return true;
            }
            if (graduatedTransferLessons && IsFloorTransferLesson
                && CurrentTrainingStage == 1
                && curriculumSpawnForward.sqrMagnitude >= 0.5f)
            {
                float yawRange = StairPracticeEnabled
                    ? EpisodePracticeLevel == 0 ? 0f : EpisodePracticeLevel == 1 ? 15f : 30f
                    : 30f;
                float guidedYawOffset = ((float)random.NextDouble() * 2f - 1f) * yawRange;
                rotation = Quaternion.LookRotation(curriculumSpawnForward, Vector3.up)
                    * Quaternion.Euler(0f, guidedYawOffset, 0f);
                return true;
            }
            if (!useStairSpawnCurriculum
                || UsesSingleFloorExplorationLesson
                || curriculumSpawnForward.sqrMagnitude < 0.5f)
            {
                return false;
            }

            float maximumYawOffset = CurrentTrainingStage <= 1 ? 0f
                : CurrentTrainingStage == 2 ? 45f
                : CurrentTrainingStage == 3 ? 90f
                : 180f;
            float yawOffset = maximumYawOffset <= 0f
                ? 0f
                : ((float)random.NextDouble() * 2f - 1f) * maximumYawOffset;
            rotation = Quaternion.LookRotation(curriculumSpawnForward, Vector3.up)
                * Quaternion.Euler(0f, yawOffset, 0f);
            return true;
        }

        private bool IsDescendingCurriculumEpisode()
        {
            if (CurrentTrainingStage <= 2)
            {
                return false;
            }

            if (CurrentTrainingStage == 3)
            {
                // One descent for every three ascent episodes.
                return currentEpisodeIndex % 4 == 3;
            }

            // Later lessons are balanced in both directions.
            return (currentEpisodeIndex & 1) == 1;
        }

        private bool TryCollectStairMouthPairs(
            List<Vector3Int> lowerMouths,
            List<Vector3Int> upperMouths,
            out int stairCellCount,
            out int connectedStairPairCount)
        {
            lowerMouths.Clear();
            upperMouths.Clear();
            stairCellCount = 0;
            connectedStairPairCount = 0;
            foreach (Vector3Int lower in generator.StairCells)
            {
                stairCellCount++;
                Vector3Int upper = lower + Vector3Int.up;
                if (invalidStairCells.Contains(lower) || !generator.IsStairCell(upper)
                    || !generator.WalkwayConnections.TryGetValue(lower, out List<Vector3Int> lowerLinks)
                    || !lowerLinks.Contains(upper)
                    || !generator.WalkwayConnections.TryGetValue(upper, out List<Vector3Int> upperLinks))
                {
                    continue;
                }

                connectedStairPairCount++;

                foreach (Vector3Int lowerMouth in lowerLinks)
                {
                    if (lowerMouth.y != lower.y || generator.IsStairCell(lowerMouth)) continue;
                    foreach (Vector3Int upperMouth in upperLinks)
                    {
                        if (upperMouth.y != upper.y || generator.IsStairCell(upperMouth)) continue;
                        lowerMouths.Add(lowerMouth);
                        upperMouths.Add(upperMouth);
                    }
                }
            }

            return lowerMouths.Count > 0;
        }

        private List<SpawnCandidate> FindFloorCandidates(
            Vector3Int mouth, int maximumSteps, bool favorOuterRing = true)
        {
            var candidates = new List<SpawnCandidate>();
            var queue = new Queue<SpawnCandidate>();
            var visited = new HashSet<Vector3Int> { mouth };
            queue.Enqueue(new SpawnCandidate { cell = mouth, steps = 0 });
            int furthest = 0;
            while (queue.Count > 0)
            {
                SpawnCandidate candidate = queue.Dequeue();
                candidates.Add(candidate);
                furthest = Mathf.Max(furthest, candidate.steps);
                if (candidate.steps >= maximumSteps) continue;
                foreach (Vector3Int neighbour in generator.WalkwayConnections[candidate.cell])
                {
                    if (neighbour.y != mouth.y || generator.IsStairCell(neighbour)
                        || !visited.Add(neighbour)) continue;
                    queue.Enqueue(new SpawnCandidate { cell = neighbour, steps = candidate.steps + 1 });
                }
            }

            // Preserve a few easy examples, but don't let every later lesson spawn next to stairs.
            if (favorOuterRing && maximumSteps != int.MaxValue)
            {
                int minimumSteps = Mathf.Max(0, furthest - 1);
                candidates.RemoveAll(candidate => candidate.steps < minimumSteps);
            }
            return candidates;
        }

        [ContextMenu("Validate Stair Connections")]
        private void ValidateStairConnections()
        {
            EnsureTrainingColliders();
            ValidateStairGeometry(true);
        }

        private void ValidateStairGeometry(bool logSummary)
        {
            invalidStairCells.Clear();
            Transform root = generator != null ? generator.GeneratedRoot : null;
            if (root == null) return;
            Physics.SyncTransforms();
            int checkedCount = 0;
            // Known Kenney ramp/landing centreline; samples also cover both corridor seams.
            float[] sampleZ = { -4.05f, -3.8f, 0f, 1.7f, 3f, 4.05f };
            foreach (Transform stair in root)
            {
                if (stair.name.IndexOf("stair", StringComparison.OrdinalIgnoreCase) < 0) continue;
                checkedCount++;
                foreach (float z in sampleZ)
                {
                    float height = z < -4f ? 0f : z >= 1.86f ? 4.04f
                        : 1.97f + Mathf.Tan(33.9f * Mathf.Deg2Rad) * (z + 1.07f)
                            + 0.1f / Mathf.Cos(33.9f * Mathf.Deg2Rad);
                    Vector3 origin = stair.TransformPoint(new Vector3(0f, height + 1f, z));
                    bool supported = false;
                    foreach (RaycastHit hit in Physics.RaycastAll(origin, -stair.up, 1.3f,
                        ~0, QueryTriggerInteraction.Ignore))
                    {
                        if (hit.transform.IsChildOf(root)
                            && Mathf.Abs(stair.InverseTransformPoint(hit.point).y - height) <= 0.2f)
                        {
                            supported = true;
                            break;
                        }
                    }
                    if (supported) continue;
                    invalidStairCells.Add(generator.WorldToWalkwayCell(stair.position));
                    Debug.LogWarning($"AI WFC: permukaan collider tangga tidak menyambung di {stair.name}, z={z:F2}. Tangga ini tidak dipakai untuk spawn kurikulum.", stair);
                    break;
                }
            }

            if (logSummary)
            {
                Debug.Log($"AI WFC: cek sambungan collider {checkedCount} tangga; bermasalah={invalidStairCells.Count}.", this);
            }
        }

        private bool HasCrossFloorCandidates()
        {
            for (int first = 0; first < walkablePositions.Count; first++)
            {
                for (int second = first + 1; second < walkablePositions.Count; second++)
                {
                    if (Mathf.Abs(
                            walkablePositions[first].y
                            - walkablePositions[second].y)
                        >= minimumFloorHeightDifference)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        [ContextMenu("Refresh Walkable Positions")]
        public void RefreshWalkablePositions()
        {
            walkablePositions.Clear();
            if (generator == null)
            {
                return;
            }

            Transform generatedRoot = generator.GeneratedRoot;
            if (generatedRoot == null)
            {
                return;
            }

            for (int index = 0; index < generatedRoot.childCount; index++)
            {
                Transform tile = generatedRoot.GetChild(index);
                if (excludeStairsAsSpawnPoints
                    && tile.name.IndexOf("stair", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                walkablePositions.Add(tile.position + Vector3.up * spawnHeight);
            }
        }

        [ContextMenu("Ensure Training Colliders")]
        public void EnsureTrainingColliders()
        {
            if (generator == null)
            {
                return;
            }

            Transform generatedRoot = generator.GeneratedRoot;
            if (generatedRoot == null)
            {
                return;
            }

            for (int tileIndex = 0; tileIndex < generatedRoot.childCount; tileIndex++)
            {
                Transform tile = generatedRoot.GetChild(tileIndex);
                bool isStair = tile.name.IndexOf(
                    "stair",
                    StringComparison.OrdinalIgnoreCase) >= 0;

                if (addExplorationAreaTriggers && !isStair)
                {
                    AddExplorationArea(tile);
                }

                if (isStair)
                {
                    AddStairTraversalArea(tile);
                }

                if (isStair && addSmoothStairRamp)
                {
                    AddSmoothRamp(tile);
                    TagSolidStairColliders(tile);
                    continue;
                }

                if (!addMeshColliders)
                {
                    continue;
                }

                MeshFilter[] meshFilters = tile.GetComponentsInChildren<MeshFilter>(true);
                foreach (MeshFilter meshFilter in meshFilters)
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
        }

        private void AddSmoothRamp(Transform stairTile)
        {
            const string rampName = "AI Smooth Stair Ramp";
            Transform existingRamp = stairTile.Find(rampName);
            if (existingRamp != null)
            {
                BoxCollider existingCollider = existingRamp.GetComponent<BoxCollider>();
                if (existingCollider != null)
                {
                    // The prefab is the source of truth for the ramp geometry.
                    // Do not shrink a manually widened collider on every map generation.
                    existingCollider.isTrigger = false;
                    existingCollider.enabled = true;
                    existingRamp.gameObject.tag = "Stair";
                    return;
                }
            }

            GameObject rampObject;
            if (existingRamp != null)
            {
                rampObject = existingRamp.gameObject;
                Debug.LogWarning(
                    $"AI WFC: '{rampName}' pada {stairTile.name} tidak memiliki BoxCollider. "
                    + "Collider fallback dibuat dengan ukuran default.", stairTile);
            }
            else
            {
                rampObject = new GameObject(rampName);
                rampObject.transform.SetParent(stairTile, false);
                Debug.LogWarning(
                    $"AI WFC: prefab {stairTile.name} tidak memiliki '{rampName}'. "
                    + "Collider fallback dibuat saat runtime; perbarui prefab agar konsisten.",
                    stairTile);
            }

            // Model stairs-wide mencapai lantai atas sekitar z=2, sedangkan
            // sel WFC berakhir di z=4. Ramp hanya mengikuti bagian miring;
            // top landing tipis mengisi sisa z=2..4 tanpa menjadi plafon.
            rampObject.transform.localPosition = new Vector3(0f, 1.97f, -1.07f);
            rampObject.transform.localRotation = Quaternion.Euler(-33.9f, 0f, 0f);
            rampObject.transform.localScale = Vector3.one;

            BoxCollider rampCollider = rampObject.AddComponent<BoxCollider>();

            rampCollider.center = Vector3.zero;
            rampCollider.size = new Vector3(stairRampWidth, 0.2f, 7.06f);
            rampCollider.isTrigger = false;
            rampCollider.enabled = true;
            rampObject.tag = "Stair";
        }

        private static void TagSolidStairColliders(Transform stairTile)
        {
            foreach (Collider collider in stairTile.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.isTrigger)
                {
                    collider.gameObject.tag = "Stair";
                }
            }
        }

        private void AddExplorationArea(Transform tile)
        {
            const string areaName = "AI Exploration Area";
            float floorHeight = Mathf.Max(0.01f, generator.CellSize.y);
            int floorIndex = Mathf.Max(
                0,
                Mathf.RoundToInt(tile.localPosition.y / floorHeight));
            Transform existingArea = tile.Find(areaName);
            if (existingArea != null)
            {
                ExplorationArea existingComponent =
                    existingArea.GetComponent<ExplorationArea>();
                existingComponent?.ConfigureFloor(floorIndex);
                return;
            }

            var areaObject = new GameObject(areaName);
            areaObject.layer = 2; // Ignore Raycast
            areaObject.transform.SetParent(tile, false);
            areaObject.transform.localPosition = new Vector3(0f, 1f, 0f);

            BoxCollider trigger = areaObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(6f, 2f, 6f);
            ExplorationArea area = areaObject.AddComponent<ExplorationArea>();
            area.ConfigureFloor(floorIndex);
        }

        private static void AddStairTraversalArea(Transform stairTile)
        {
            const string triggerName = "AI Stair Traversal Area";
            if (stairTile.Find(triggerName) != null)
            {
                return;
            }

            var triggerObject = new GameObject(triggerName);
            triggerObject.layer = 2; // Ignore Raycast
            triggerObject.transform.SetParent(stairTile, false);
            triggerObject.transform.localPosition = new Vector3(0f, 2f, 0f);

            BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(4f, 4f, 6f);
            triggerObject.AddComponent<StairTraversalArea>();
        }

        private void OnValidate()
        {
            regenerateEveryEpisodes = Mathf.Max(1, regenerateEveryEpisodes);
            maximumTrainingMapAttempts = Mathf.Max(1, maximumTrainingMapAttempts);
            minimumTwoFloorStairCount = Mathf.Max(1, minimumTwoFloorStairCount);
            spawnHeight = Mathf.Max(0f, spawnHeight);
            stairRampWidth = Mathf.Max(0.5f, stairRampWidth);
            minimumFloorHeightDifference = Mathf.Max(0.1f, minimumFloorHeightDifference);
        }
    }
}
