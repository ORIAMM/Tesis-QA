using System;
using System.Collections.Generic;
using Unity.InferenceEngine;
using UnityEngine;

namespace TesisQA.AI
{
    public enum DefectExperimentMode { Manual, AI, Random }
    public enum CollisionDefectKind { FullCollider, PartialHole }

    [Serializable]
    public sealed class DefectScenario
    {
        public string id;
        public int mapSeed, defectSeed, spawnSeed;
        public int fullDefects, partialDefects;
        [Tooltip("Per-trial WFC dimensions. Zero preserves the scene's legacy grid size.")]
        public Vector3Int gridSize;
        [TextArea] public string frozenManifest;
    }

    [CreateAssetMenu(menuName = "Tesis QA/Defect experiment protocol")]
    public sealed class DefectExperimentProtocol : ScriptableObject
    {
        public string protocolId = "collision-pilot-v1";
        public string detectorVersion = "local-flat-floor-v2";
        public string scorerVersion = "collision-region-v2";
        [Tooltip("External wall collision inspection; does not add policy observations.")]
        public bool inspectWalls;
        [Tooltip("Use the model's deterministic action outputs for reproducible evaluation.")]
        public bool deterministicInference;
        [Tooltip("Align automatic episode transitions and initial decisions to physics steps.")]
        public bool fixedStepTransitions;
        public ModelAsset model;
        public string modelSource, modelSha256;
        public DefectMeshLibrary meshLibrary;
        public float durationSeconds = 150f;
        public int policyStepHorizon = 12000;
        public float fixedDeltaTime = .02f;
        public float scanInterval = .2f;
        public float scanRange = 6f;
        public float partialHoleWidth = 2.4f;
        public int automaticRepetitions = 3;
        [Tooltip("Keep progressive trials in their original order, regardless of participant counterbalancing settings.")]
        public bool fixedScenarioOrder;
        public List<DefectScenario> scenarios = new List<DefectScenario>();

        // Ten orders: all cyclic shifts, followed by their reverses. Five scenarios
        // occur equally often in every position when complete blocks are recruited.
        public int ScenarioIndex(int ordinal, int participantOrder)
        {
            int count = scenarios.Count;
            if (fixedScenarioOrder) return ((ordinal % count) + count) % count;
            int order = ((participantOrder % (2 * count)) + 2 * count) % (2 * count);
            return order < count ? (ordinal + order) % count
                : (count - 1 - ordinal + order - count) % count;
        }
    }

    [Serializable]
    public sealed class DefectMeshData
    {
        public Mesh source;
        public Vector3[] vertices;
        public int[] triangles;
        public string sha256;
        public string sourceAssetPath, sourceDependencyHash;
    }

    [Serializable]
    public sealed class DefectLayoutModule
    {
        public string name;
        public Vector3 position, euler, scale;
    }

    [Serializable]
    public sealed class InjectedCollisionDefect
    {
        public string id, surfaceId;
        public CollisionDefectKind kind;
        public Vector3 center;
        public int floor;
        public float width;
    }

    [Serializable]
    public sealed class DefectScenarioManifest
    {
        public string scenarioId, geometrySha256, configurationSha256;
        public int requestedMapSeed, actualMapSeed, defectSeed, spawnSeed;
        public Vector3 spawn;
        public float spawnYaw;
        public List<DefectLayoutModule> layout = new List<DefectLayoutModule>();
        public List<InjectedCollisionDefect> defects = new List<InjectedCollisionDefect>();
    }

    [Serializable]
    public sealed class DefectFinding
    {
        public string surfaceId, source, matchedDefectId;
        public string surfaceKind;
        public Vector3 normal;
        public Vector3 position;
        public float simulationSeconds, wallSeconds;
        public bool truePositive, duplicate;
    }

    [Serializable]
    public sealed class DefectMarkAttempt
    {
        public string surfaceId, reason;
        public Vector3 position;
        public float simulationSeconds, wallSeconds;
        public bool accepted;
    }

    [Serializable]
    public sealed class DefectTrace
    {
        public float seconds;
        public Vector3 position;
        public float yaw;
    }

    [Serializable]
    public sealed class DefectTrialReport
    {
        public string trialId, participant, mode, modelSha256, protocolSha256, status, finishReason;
        public int order, repetition, inferenceSeed, randomActionSeed, falls, truePositives, falsePositives, duplicates;
        public float simulationSeconds, wallSeconds, pathLength, coverage, floorZeroCoverage, floorOneCoverage;
        public float pausedWallSeconds;
        public int pauseCount;
        // -1 denotes no detection / undefined precision or recall; CSV uses empty cells.
        public float firstDetectionSeconds = -1f, firstTrueDetectionSeconds = -1f;
        public float precision = -1f, recall = -1f;
        public DefectScenarioManifest manifest;
        public List<DefectFinding> findings = new List<DefectFinding>();
        public List<DefectMarkAttempt> markAttempts = new List<DefectMarkAttempt>();
        public List<DefectTrace> trace = new List<DefectTrace>();
    }
}
