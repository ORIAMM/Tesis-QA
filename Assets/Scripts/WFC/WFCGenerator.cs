using System;
using System.Collections.Generic;
using UnityEngine;

namespace TesisQA.WFC
{
    public sealed class WFCGenerator : MonoBehaviour
    {
        private const string GeneratedRootName = "WFC Generated";

        [Header("Wide Corridor Tile Set")]
        [SerializeField] private WFCTileSet tileSet;

        [Header("Grid")]
        [SerializeField] private Vector3Int gridSize = new Vector3Int(12, 1, 12);
        [SerializeField] private Vector3 cellSize = new Vector3(8f, 4f, 8f);

        [Header("Generation")]
        [SerializeField] private int seed = 12345;
        [SerializeField] private bool randomizeSeed = true;
        [SerializeField, Min(1)] private int maxRestarts = 80;
        [SerializeField] private bool generateOnStart;

        [Tooltip("X = minimum, Y = maksimum. Nilai maksimum 0 berarti tanpa batas.")]
        [SerializeField] private Vector2Int boundaryEndCountRange = new Vector2Int(2, 8);

        [Tooltip("Jumlah minimum modul tangga pada hasil akhir. Gunakan 0 untuk map satu lantai.")]
        [SerializeField, Min(0)] private int minimumStairCount;

        [Header("Preview")]
        [SerializeField] private bool drawGridGizmos = true;
        [SerializeField] private Color gridGizmoColor = new Color(0.1f, 0.8f, 1f, 0.25f);

        [SerializeField, HideInInspector] private Transform generatedRoot;
        [SerializeField, HideInInspector] private int lastUsedSeed;

        public Transform GeneratedRoot => generatedRoot != null
            ? generatedRoot
            : transform.Find(GeneratedRootName);
        public Vector3 CellSize => cellSize;
        public Vector3Int GridSize => gridSize;
        public int LastUsedSeed => lastUsedSeed;

        // Evaluation scenarios can vary horizontal size without changing training assets.
        public void ConfigureRuntimeGrid(Vector3Int size)
        {
            if (size.x < 3 || size.y < 1 || size.z < 3)
                throw new ArgumentOutOfRangeException(nameof(size), "WFC grid must be at least 3 x 1 x 3.");
            gridSize = size;
        }

        public bool ConfigureRuntimeFloors(int floorCount, int requiredStairCount)
        {
            int clampedFloorCount = Mathf.Max(1, floorCount);
            int clampedStairCount = clampedFloorCount <= 1
                ? 0
                : Mathf.Max(0, requiredStairCount);
            bool changed = gridSize.y != clampedFloorCount
                || minimumStairCount != clampedStairCount;
            gridSize = new Vector3Int(gridSize.x, clampedFloorCount, gridSize.z);
            minimumStairCount = clampedStairCount;
            return changed;
        }

        // Runtime topology uses the same sockets as WFC validation, not visual proximity.
        private readonly Dictionary<Vector3Int, List<Vector3Int>> walkwayConnections =
            new Dictionary<Vector3Int, List<Vector3Int>>();
        private readonly HashSet<Vector3Int> stairCells = new HashSet<Vector3Int>();
        public IReadOnlyDictionary<Vector3Int, List<Vector3Int>> WalkwayConnections
            => walkwayConnections;
        public IEnumerable<Vector3Int> StairCells => stairCells;
        public bool IsStairCell(Vector3Int cell) => stairCells.Contains(cell);

        public Vector3 WalkwayCellToWorld(Vector3Int cell)
        {
            Vector3 local = Vector3.Scale((Vector3)cell, cellSize);
            return GeneratedRoot != null ? GeneratedRoot.TransformPoint(local) : transform.TransformPoint(local);
        }

        public Vector3Int WorldToWalkwayCell(Vector3 position)
        {
            Vector3 local = GeneratedRoot != null
                ? GeneratedRoot.InverseTransformPoint(position)
                : transform.InverseTransformPoint(position);
            return new Vector3Int(
                Mathf.RoundToInt(local.x / cellSize.x),
                Mathf.RoundToInt(local.y / cellSize.y),
                Mathf.RoundToInt(local.z / cellSize.z));
        }

        private sealed class TileState
        {
            public int id;
            public WFCTileDefinition definition;
            public int rotation;
            public Vector3Int localCell;
            public Vector3Int rootLocalCell;
            public Vector3Int rotatedSize;
            public float weight;
            public readonly string[] sockets = new string[WFCDirectionUtility.DirectionCount];
        }

        private void Start()
        {
            if (generateOnStart)
            {
                Generate();
            }
        }

        [ContextMenu("Generate WFC")]
        public bool Generate()
        {
            if (!ValidateSettings(out string validationError))
            {
                Debug.LogError($"WFC: {validationError}", this);
                return false;
            }

            List<TileState> states = BuildStates();
            TileState fillerState = FindFillerState(states);
            if (fillerState == null)
            {
                Debug.LogError("WFC: Tile Set harus mempunyai satu tile kategori Filler.", this);
                return false;
            }

            int baseSeed = randomizeSeed
                ? unchecked(Environment.TickCount * 397 ^ DateTime.Now.Millisecond)
                : seed;
            int collapseFailures = 0;
            int emptyNetworkFailures = 0;
            int boundaryEndFailures = 0;
            int stairCountFailures = 0;

            for (int attempt = 0; attempt < maxRestarts; attempt++)
            {
                int attemptSeed = unchecked(baseSeed + attempt * 7919);
                var random = new System.Random(attemptSeed);
                List<HashSet<int>> domains = CreateInitialDomains(states);
                if (domains == null)
                {
                    collapseFailures++;
                    continue;
                }

                var initialQueue = new Queue<int>();
                for (int index = 0; index < domains.Count; index++)
                {
                    initialQueue.Enqueue(index);
                }

                if (!Propagate(domains, states, initialQueue)
                    || !Collapse(domains, states, random))
                {
                    collapseFailures++;
                    continue;
                }

                if (!KeepLargestConnectedNetwork(domains, states, fillerState.id))
                {
                    emptyNetworkFailures++;
                    continue;
                }

                int boundaryEndCount = CountBoundaryEndCaps(domains, states);
                if (!IsBoundaryEndCountValid(boundaryEndCount))
                {
                    boundaryEndFailures++;
                    continue;
                }

                int stairCount = CountRootTilesByCategory(
                    domains,
                    states,
                    WFCTileCategory.Stairs);
                if (stairCount < minimumStairCount)
                {
                    stairCountFailures++;
                    continue;
                }

                if (!ValidateCollapsedAdjacency(domains, states))
                {
                    collapseFailures++;
                    continue;
                }

                lastUsedSeed = attemptSeed;
                BuildResult(domains, states);
                Debug.Log(
                    $"WFC selesai. Seed: {attemptSeed}, percobaan: {attempt + 1}, " +
                    $"boundary end: {boundaryEndCount}, tangga: {stairCount}.",
                    this);
                return true;
            }

            Debug.LogError(
                $"WFC gagal setelah {maxRestarts} percobaan. " +
                $"Rincian: collapse={collapseFailures}, jaringan kosong={emptyNetworkFailures}, " +
                $"jumlah boundary end={boundaryEndFailures}, jumlah tangga={stairCountFailures}.",
                this);
            return false;
        }

        public bool GenerateWithSeed(int fixedSeed)
        {
            bool previousRandomizeSeed = randomizeSeed;
            int previousSeed = seed;
            randomizeSeed = false;
            seed = fixedSeed;

            try
            {
                return Generate();
            }
            finally
            {
                seed = previousSeed;
                randomizeSeed = previousRandomizeSeed;
            }
        }

        [ContextMenu("Clear Generated")]
        public void ClearGenerated()
        {
            walkwayConnections.Clear();
            stairCells.Clear();
            Transform root = generatedRoot != null
                ? generatedRoot
                : transform.Find(GeneratedRootName);
            if (root == null)
            {
                return;
            }

            generatedRoot = null;
            if (Application.isPlaying)
            {
                // Destroy ditunda sampai akhir frame. Nonaktifkan lebih dulu agar
                // trigger map lama tidak ikut dihitung sebagai coverage episode baru.
                root.gameObject.SetActive(false);
                Destroy(root.gameObject);
            }
            else
            {
                DestroyImmediate(root.gameObject);
            }
        }

        private bool ValidateSettings(out string error)
        {
            if (tileSet == null || tileSet.tiles == null || tileSet.tiles.Count == 0)
            {
                error = "Wide Corridor Tile Set belum diisi atau kosong.";
                return false;
            }

            if (gridSize.x < 3 || gridSize.y < 1 || gridSize.z < 3)
            {
                error = "Grid Size minimal (3,1,3).";
                return false;
            }

            if (cellSize.x <= 0f || cellSize.y <= 0f || cellSize.z <= 0f)
            {
                error = "Cell Size harus lebih besar dari nol.";
                return false;
            }

            bool hasFiller = false;
            bool hasBoundaryEnd = false;
            foreach (WFCTileDefinition definition in tileSet.tiles)
            {
                if (definition == null)
                {
                    continue;
                }

                hasFiller |= definition.EffectiveCategory == WFCTileCategory.Filler;
                hasBoundaryEnd |= definition.isBoundaryEndCap;
            }

            if (!hasFiller)
            {
                error = "Tile Set memerlukan satu tile kategori Filler tanpa prefab.";
                return false;
            }

            if (!hasBoundaryEnd)
            {
                error = "Tandai Corridor Wide End sebagai Is Boundary End Cap pada Tile Definition.";
                return false;
            }

            error = null;
            return true;
        }

        private List<TileState> BuildStates()
        {
            var states = new List<TileState>();

            for (int tileIndex = 0; tileIndex < tileSet.tiles.Count; tileIndex++)
            {
                WFCTileDefinition definition = tileSet.tiles[tileIndex];
                if (definition == null)
                {
                    continue;
                }

                int rotationCount = definition.allowYRotations ? 4 : 1;
                for (int rotation = 0; rotation < rotationCount; rotation++)
                {
                    Vector3Int rotatedSize = WFCDirectionUtility.RotatedSize(definition.size, rotation);
                    Vector3Int rootLocalCell = WFCDirectionUtility.RotateCellY(
                        Vector3Int.zero,
                        definition.size,
                        rotation);

                    for (int y = 0; y < definition.size.y; y++)
                    {
                        for (int z = 0; z < definition.size.z; z++)
                        {
                            for (int x = 0; x < definition.size.x; x++)
                            {
                                var originalCell = new Vector3Int(x, y, z);
                                Vector3Int rotatedCell = WFCDirectionUtility.RotateCellY(
                                    originalCell,
                                    definition.size,
                                    rotation);
                                var state = new TileState
                                {
                                    id = states.Count,
                                    definition = definition,
                                    rotation = rotation,
                                    localCell = rotatedCell,
                                    rootLocalCell = rootLocalCell,
                                    rotatedSize = rotatedSize,
                                    weight = Mathf.Max(0.0001f, definition.weight)
                                };

                                for (int directionIndex = 0;
                                     directionIndex < WFCDirectionUtility.DirectionCount;
                                     directionIndex++)
                                {
                                    var rotatedDirection = (WFCDirection)directionIndex;
                                    Vector3Int neighbour = rotatedCell + rotatedDirection.ToOffset();
                                    if (IsInside(neighbour, rotatedSize))
                                    {
                                        state.sockets[directionIndex] = BuildInternalSocket(
                                            tileIndex,
                                            rotation,
                                            rotatedCell,
                                            neighbour);
                                    }
                                    else
                                    {
                                        WFCDirection originalDirection = rotatedDirection.RotateY(-rotation);
                                        state.sockets[directionIndex] = definition.GetOuterSocket(
                                            originalCell,
                                            originalDirection);
                                    }
                                }

                                states.Add(state);
                            }
                        }
                    }
                }
            }

            return states;
        }

        private List<HashSet<int>> CreateInitialDomains(List<TileState> states)
        {
            int cellCount = gridSize.x * gridSize.y * gridSize.z;
            var domains = new List<HashSet<int>>(cellCount);
            string closedSocket = WFCTileDefinition.NormalizeSocket(tileSet.boundarySocket);

            for (int index = 0; index < cellCount; index++)
            {
                Vector3Int cell = FromIndex(index);
                bool horizontalBoundary = IsHorizontalBoundaryCell(cell);
                var domain = new HashSet<int>();

                foreach (TileState state in states)
                {
                    if (horizontalBoundary)
                    {
                        if (!IsFiller(state)
                            && !IsValidBoundaryEndState(state, cell, closedSocket))
                        {
                            continue;
                        }
                    }
                    else if (state.definition.isBoundaryEndCap)
                    {
                        // End hanya dipakai pada perimeter. End di interior mudah
                        // membentuk banyak pulau kecil yang kemudian menjadi lubang.
                        continue;
                    }

                    bool allowed = true;
                    for (int directionIndex = 0;
                         directionIndex < WFCDirectionUtility.DirectionCount;
                         directionIndex++)
                    {
                        var direction = (WFCDirection)directionIndex;
                        if (!IsInside(cell + direction.ToOffset(), gridSize)
                            && !tileSet.SocketsMatch(
                                state.sockets[directionIndex],
                                closedSocket))
                        {
                            allowed = false;
                            break;
                        }
                    }

                    if (allowed)
                    {
                        domain.Add(state.id);
                    }
                }

                if (domain.Count == 0)
                {
                    return null;
                }

                domains.Add(domain);
            }

            return domains;
        }

        private bool IsValidBoundaryEndState(
            TileState state,
            Vector3Int cell,
            string closedSocket)
        {
            if (!state.definition.isBoundaryEndCap
                || state.rotatedSize != Vector3Int.one)
            {
                return false;
            }

            int openingCount = 0;
            bool pointsTowardInterior = false;
            for (int directionIndex = 0; directionIndex < 4; directionIndex++)
            {
                if (tileSet.SocketsMatch(state.sockets[directionIndex], closedSocket))
                {
                    continue;
                }

                openingCount++;
                pointsTowardInterior |= OpeningPointsTowardInterior(
                    cell,
                    (WFCDirection)directionIndex);
            }

            return openingCount == 1 && pointsTowardInterior;
        }

        private bool OpeningPointsTowardInterior(Vector3Int cell, WFCDirection direction)
        {
            return cell.x == 0 && direction == WFCDirection.East
                || cell.x == gridSize.x - 1 && direction == WFCDirection.West
                || cell.z == 0 && direction == WFCDirection.North
                || cell.z == gridSize.z - 1 && direction == WFCDirection.South;
        }

        private bool Collapse(
            List<HashSet<int>> domains,
            List<TileState> states,
            System.Random random)
        {
            while (true)
            {
                int selectedCell = SelectLowestEntropyCell(domains, states, random);
                if (selectedCell < 0)
                {
                    return true;
                }

                int chosenState = ChooseWeightedState(domains[selectedCell], states, random);
                domains[selectedCell].Clear();
                domains[selectedCell].Add(chosenState);

                var queue = new Queue<int>();
                queue.Enqueue(selectedCell);
                if (!Propagate(domains, states, queue))
                {
                    return false;
                }
            }
        }

        private int SelectLowestEntropyCell(
            List<HashSet<int>> domains,
            List<TileState> states,
            System.Random random)
        {
            int selected = -1;
            double bestEntropy = double.PositiveInfinity;

            for (int index = 0; index < domains.Count; index++)
            {
                HashSet<int> domain = domains[index];
                if (domain.Count <= 1)
                {
                    continue;
                }

                double sumWeight = 0d;
                double sumWeightLogWeight = 0d;
                foreach (int stateId in domain)
                {
                    double weight = states[stateId].weight;
                    sumWeight += weight;
                    sumWeightLogWeight += weight * Math.Log(weight);
                }

                double entropy = Math.Log(sumWeight) - sumWeightLogWeight / sumWeight;
                entropy += random.NextDouble() * 0.000001d;
                if (entropy < bestEntropy)
                {
                    bestEntropy = entropy;
                    selected = index;
                }
            }

            return selected;
        }

        private static int ChooseWeightedState(
            HashSet<int> domain,
            List<TileState> states,
            System.Random random)
        {
            double total = 0d;
            foreach (int stateId in domain)
            {
                total += states[stateId].weight;
            }

            double pick = random.NextDouble() * total;
            int fallback = -1;
            foreach (int stateId in domain)
            {
                fallback = stateId;
                pick -= states[stateId].weight;
                if (pick <= 0d)
                {
                    return stateId;
                }
            }

            return fallback;
        }

        private bool Propagate(
            List<HashSet<int>> domains,
            List<TileState> states,
            Queue<int> queue)
        {
            while (queue.Count > 0)
            {
                int currentIndex = queue.Dequeue();
                Vector3Int currentCell = FromIndex(currentIndex);
                HashSet<int> currentDomain = domains[currentIndex];
                if (currentDomain.Count == 0)
                {
                    return false;
                }

                for (int directionIndex = 0;
                     directionIndex < WFCDirectionUtility.DirectionCount;
                     directionIndex++)
                {
                    var direction = (WFCDirection)directionIndex;
                    Vector3Int neighbourCell = currentCell + direction.ToOffset();
                    if (!IsInside(neighbourCell, gridSize))
                    {
                        continue;
                    }

                    int neighbourIndex = ToIndex(neighbourCell);
                    HashSet<int> neighbourDomain = domains[neighbourIndex];
                    var removed = new List<int>();
                    foreach (int neighbourStateId in neighbourDomain)
                    {
                        bool hasCompatibleNeighbour = false;
                        foreach (int currentStateId in currentDomain)
                        {
                            if (StatesCanTouch(
                                    states[currentStateId],
                                    states[neighbourStateId],
                                    direction))
                            {
                                hasCompatibleNeighbour = true;
                                break;
                            }
                        }

                        if (!hasCompatibleNeighbour)
                        {
                            removed.Add(neighbourStateId);
                        }
                    }

                    if (removed.Count == 0)
                    {
                        continue;
                    }

                    foreach (int stateId in removed)
                    {
                        neighbourDomain.Remove(stateId);
                    }

                    if (neighbourDomain.Count == 0)
                    {
                        return false;
                    }

                    queue.Enqueue(neighbourIndex);
                }
            }

            return true;
        }

        private bool KeepLargestConnectedNetwork(
            List<HashSet<int>> domains,
            List<TileState> states,
            int fillerStateId)
        {
            List<List<int>> components = FindNetworkComponents(domains, states);
            if (components.Count == 0)
            {
                return false;
            }

            int largestIndex = 0;
            for (int index = 1; index < components.Count; index++)
            {
                if (components[index].Count > components[largestIndex].Count)
                {
                    largestIndex = index;
                }
            }

            for (int componentIndex = 0; componentIndex < components.Count; componentIndex++)
            {
                if (componentIndex == largestIndex)
                {
                    continue;
                }

                foreach (int cellIndex in components[componentIndex])
                {
                    domains[cellIndex].Clear();
                    domains[cellIndex].Add(fillerStateId);
                }
            }

            return true;
        }

        private List<List<int>> FindNetworkComponents(
            List<HashSet<int>> domains,
            List<TileState> states)
        {
            var components = new List<List<int>>();
            var visited = new HashSet<int>();
            string closedSocket = WFCTileDefinition.NormalizeSocket(tileSet.boundarySocket);

            for (int startIndex = 0; startIndex < domains.Count; startIndex++)
            {
                TileState startState = states[GetOnlyState(domains[startIndex])];
                if (IsFiller(startState) || !visited.Add(startIndex))
                {
                    continue;
                }

                var component = new List<int>();
                var queue = new Queue<int>();
                queue.Enqueue(startIndex);

                while (queue.Count > 0)
                {
                    int currentIndex = queue.Dequeue();
                    component.Add(currentIndex);
                    Vector3Int currentCell = FromIndex(currentIndex);
                    TileState currentState = states[GetOnlyState(domains[currentIndex])];

                    for (int directionIndex = 0;
                         directionIndex < WFCDirectionUtility.DirectionCount;
                         directionIndex++)
                    {
                        string socket = currentState.sockets[directionIndex];
                        if (tileSet.SocketsMatch(socket, closedSocket))
                        {
                            continue;
                        }

                        var direction = (WFCDirection)directionIndex;
                        Vector3Int neighbourCell = currentCell + direction.ToOffset();
                        if (!IsInside(neighbourCell, gridSize))
                        {
                            continue;
                        }

                        int neighbourIndex = ToIndex(neighbourCell);
                        TileState neighbourState = states[GetOnlyState(domains[neighbourIndex])];
                        if (IsFiller(neighbourState)
                            || !tileSet.SocketsMatch(
                                socket,
                                neighbourState.sockets[(int)direction.Opposite()])
                            || !visited.Add(neighbourIndex))
                        {
                            continue;
                        }

                        queue.Enqueue(neighbourIndex);
                    }
                }

                components.Add(component);
            }

            return components;
        }

        private int CountBoundaryEndCaps(
            List<HashSet<int>> domains,
            List<TileState> states)
        {
            int count = 0;
            for (int index = 0; index < domains.Count; index++)
            {
                Vector3Int cell = FromIndex(index);
                if (!IsHorizontalBoundaryCell(cell))
                {
                    continue;
                }

                TileState state = states[GetOnlyState(domains[index])];
                if (state.definition.isBoundaryEndCap
                    && state.localCell == state.rootLocalCell)
                {
                    count++;
                }
            }

            return count;
        }

        private static int CountRootTilesByCategory(
            List<HashSet<int>> domains,
            List<TileState> states,
            WFCTileCategory category)
        {
            int count = 0;
            for (int index = 0; index < domains.Count; index++)
            {
                TileState state = states[GetOnlyState(domains[index])];
                if (state.definition.EffectiveCategory == category
                    && state.localCell == state.rootLocalCell)
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsBoundaryEndCountValid(int count)
        {
            return count >= boundaryEndCountRange.x
                && (boundaryEndCountRange.y <= 0 || count <= boundaryEndCountRange.y);
        }

        private bool ValidateCollapsedAdjacency(
            List<HashSet<int>> domains,
            List<TileState> states)
        {
            for (int index = 0; index < domains.Count; index++)
            {
                Vector3Int cell = FromIndex(index);
                TileState state = states[GetOnlyState(domains[index])];
                for (int directionIndex = 0;
                     directionIndex < WFCDirectionUtility.DirectionCount;
                     directionIndex++)
                {
                    var direction = (WFCDirection)directionIndex;
                    Vector3Int neighbourCell = cell + direction.ToOffset();
                    if (!IsInside(neighbourCell, gridSize))
                    {
                        continue;
                    }

                    TileState neighbour = states[GetOnlyState(domains[ToIndex(neighbourCell)])];
                    if (!StatesCanTouch(state, neighbour, direction))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private bool StatesCanTouch(
            TileState first,
            TileState second,
            WFCDirection direction)
        {
            string firstSocket = first.sockets[(int)direction];
            string secondSocket = second.sockets[(int)direction.Opposite()];
            return tileSet.SocketsMatch(firstSocket, secondSocket);
        }

        private void BuildResult(List<HashSet<int>> domains, List<TileState> states)
        {
            ClearGenerated();
            var rootObject = new GameObject(GeneratedRootName);
            generatedRoot = rootObject.transform;
            generatedRoot.SetParent(transform, false);

            for (int index = 0; index < domains.Count; index++)
            {
                TileState state = states[GetOnlyState(domains[index])];
                if (state.localCell != state.rootLocalCell || state.definition.prefab == null)
                {
                    continue;
                }

                Vector3Int rootCell = FromIndex(index);
                Vector3Int moduleMinCell = rootCell - state.rootLocalCell;
                Vector3 footprintCenter = new Vector3(
                    moduleMinCell.x * cellSize.x + (state.rotatedSize.x - 1) * cellSize.x * 0.5f,
                    moduleMinCell.y * cellSize.y,
                    moduleMinCell.z * cellSize.z + (state.rotatedSize.z - 1) * cellSize.z * 0.5f);
                Quaternion wfcRotation = Quaternion.Euler(0f, state.rotation * 90f, 0f);

                GameObject instance = Instantiate(state.definition.prefab, generatedRoot);
                instance.name = $"{state.definition.TileName} [{moduleMinCell.x},{moduleMinCell.y},{moduleMinCell.z}]";
                instance.transform.localPosition = footprintCenter
                                                   + wfcRotation * state.definition.prefabPositionOffset;
                instance.transform.localRotation = wfcRotation
                                                   * Quaternion.Euler(state.definition.prefabEulerOffset);
            }

            BuildWalkwayConnections(domains, states);
        }

        private void BuildWalkwayConnections(List<HashSet<int>> domains, List<TileState> states)
        {
            string closedSocket = WFCTileDefinition.NormalizeSocket(tileSet.boundarySocket);
            for (int index = 0; index < domains.Count; index++)
            {
                TileState state = states[GetOnlyState(domains[index])];
                if (IsFiller(state)) continue;
                Vector3Int cell = FromIndex(index);
                var neighbours = new List<Vector3Int>();
                walkwayConnections.Add(cell, neighbours);
                if (state.definition.EffectiveCategory == WFCTileCategory.Stairs)
                {
                    stairCells.Add(cell);
                }

                for (int directionIndex = 0; directionIndex < WFCDirectionUtility.DirectionCount; directionIndex++)
                {
                    string socket = state.sockets[directionIndex];
                    if (tileSet.SocketsMatch(socket, closedSocket)) continue;
                    var direction = (WFCDirection)directionIndex;
                    Vector3Int neighbourCell = cell + direction.ToOffset();
                    if (!IsInside(neighbourCell, gridSize)) continue;
                    TileState neighbour = states[GetOnlyState(domains[ToIndex(neighbourCell)])];
                    if (!IsFiller(neighbour)
                        && tileSet.SocketsMatch(socket, neighbour.sockets[(int)direction.Opposite()]))
                    {
                        neighbours.Add(neighbourCell);
                    }
                }
            }
        }

        private TileState FindFillerState(List<TileState> states)
        {
            string closedSocket = WFCTileDefinition.NormalizeSocket(tileSet.boundarySocket);
            foreach (TileState state in states)
            {
                if (!IsFiller(state) || state.rotatedSize != Vector3Int.one)
                {
                    continue;
                }

                bool allClosed = true;
                for (int directionIndex = 0;
                     directionIndex < WFCDirectionUtility.DirectionCount;
                     directionIndex++)
                {
                    allClosed &= tileSet.SocketsMatch(
                        state.sockets[directionIndex],
                        closedSocket);
                }

                if (allClosed)
                {
                    return state;
                }
            }

            return null;
        }

        private static bool IsFiller(TileState state)
        {
            return state.definition.EffectiveCategory == WFCTileCategory.Filler;
        }

        private static int GetOnlyState(HashSet<int> domain)
        {
            foreach (int state in domain)
            {
                return state;
            }

            return -1;
        }

        private static bool IsInside(Vector3Int cell, Vector3Int size)
        {
            return cell.x >= 0 && cell.x < size.x
                && cell.y >= 0 && cell.y < size.y
                && cell.z >= 0 && cell.z < size.z;
        }

        private bool IsHorizontalBoundaryCell(Vector3Int cell)
        {
            return cell.x == 0 || cell.x == gridSize.x - 1
                || cell.z == 0 || cell.z == gridSize.z - 1;
        }

        private static string BuildInternalSocket(
            int tileIndex,
            int rotation,
            Vector3Int first,
            Vector3Int second)
        {
            Vector3Int minimum = IsLexicographicallyBefore(first, second) ? first : second;
            Vector3Int maximum = minimum == first ? second : first;
            return $"$Internal:{tileIndex}:{rotation}:{minimum.x},{minimum.y},{minimum.z}-{maximum.x},{maximum.y},{maximum.z}";
        }

        private static bool IsLexicographicallyBefore(Vector3Int first, Vector3Int second)
        {
            if (first.x != second.x) return first.x < second.x;
            if (first.y != second.y) return first.y < second.y;
            return first.z < second.z;
        }

        private int ToIndex(Vector3Int cell)
        {
            return cell.x + gridSize.x * (cell.z + gridSize.z * cell.y);
        }

        private Vector3Int FromIndex(int index)
        {
            int layerSize = gridSize.x * gridSize.z;
            int y = index / layerSize;
            int remainder = index - y * layerSize;
            int z = remainder / gridSize.x;
            int x = remainder - z * gridSize.x;
            return new Vector3Int(x, y, z);
        }

        private void OnValidate()
        {
            gridSize = new Vector3Int(
                Mathf.Max(3, gridSize.x),
                Mathf.Max(1, gridSize.y),
                Mathf.Max(3, gridSize.z));
            cellSize = new Vector3(
                Mathf.Max(0.01f, cellSize.x),
                Mathf.Max(0.01f, cellSize.y),
                Mathf.Max(0.01f, cellSize.z));
            maxRestarts = Mathf.Max(1, maxRestarts);
            minimumStairCount = Mathf.Max(0, minimumStairCount);
            boundaryEndCountRange.x = Mathf.Max(0, boundaryEndCountRange.x);
            boundaryEndCountRange.y = Mathf.Max(0, boundaryEndCountRange.y);
            if (boundaryEndCountRange.y > 0)
            {
                boundaryEndCountRange.y = Mathf.Max(
                    boundaryEndCountRange.x,
                    boundaryEndCountRange.y);
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawGridGizmos)
            {
                return;
            }

            Gizmos.color = gridGizmoColor;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;

            for (int y = 0; y < gridSize.y; y++)
            {
                for (int z = 0; z < gridSize.z; z++)
                {
                    for (int x = 0; x < gridSize.x; x++)
                    {
                        Vector3 center = new Vector3(x * cellSize.x, y * cellSize.y, z * cellSize.z);
                        Gizmos.DrawWireCube(center, cellSize * 0.95f);
                    }
                }
            }

            Gizmos.matrix = previousMatrix;
        }
    }
}
