using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TesisQA.WFC.Editor
{
    /// <summary>Membuat atau memperbaiki seluruh asset untuk aturan Wide Corridor.</summary>
    public static class KenneyWFCSetup
    {
        private const string ModelRoot =
            "Assets/Modular Tile Sets/kenney_modular-dungeon-kit_1.0/Models/FBX format";
        private const string OutputRoot = "Assets/WFC/Kenney Wide Corridors";
        private const string DefinitionRoot = OutputRoot + "/Tile Definitions";
        private const string PrefabRoot = OutputRoot + "/Prefabs";
        private const string TileSetPath = OutputRoot + "/TS_Kenney_Wide_Corridors.asset";
        private const string StairLandingPrefabPath =
            PrefabRoot + "/PF_Wide_Stairs_With_Landing.prefab";

        [InitializeOnLoadMethod]
        private static void ScheduleMissingStairPrefabCreation()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode
                    || AssetDatabase.LoadAssetAtPath<GameObject>(StairLandingPrefabPath) != null)
                {
                    return;
                }

                CreateOrUpdateTileSet();
            };
        }

        [MenuItem("Tools/WFC/Create or Update Kenney Wide Corridor Tile Set")]
        public static void CreateOrUpdateTileSet()
        {
            EnsureFolder("Assets", "WFC");
            EnsureFolder("Assets/WFC", "Kenney Wide Corridors");
            EnsureFolder(OutputRoot, "Tile Definitions");
            EnsureFolder(OutputRoot, "Prefabs");

            GameObject stairWithLanding = CreateOrUpdateStairLandingPrefab();

            var definitions = new List<WFCTileDefinition>
            {
                CreateDefinition("Wide_Void", null, Vector3Int.one, 2f, false,
                    WFCTileCategory.Filler, 0f, false, Vector3.zero),
                CreateCorridor("Wide_Corridor_Straight", "corridor-wide", 5f, 90f,
                    Face(Vector3Int.zero, WFCDirection.North),
                    Face(Vector3Int.zero, WFCDirection.South)),
                CreateCorridor("Wide_Corridor_Corner", "corridor-wide-corner", 4f, 270f,
                    Face(Vector3Int.zero, WFCDirection.North),
                    Face(Vector3Int.zero, WFCDirection.East)),
                CreateDefinition("Wide_Corridor_End", LoadModel("corridor-wide-end"), Vector3Int.one, 0.12f, true,
                    WFCTileCategory.Corridor, 90f, true, Vector3.zero,
                    Face(Vector3Int.zero, WFCDirection.North)),
                CreateCorridor("Wide_Corridor_Junction", "corridor-wide-junction", 1.5f, 180f,
                    Face(Vector3Int.zero, WFCDirection.North),
                    Face(Vector3Int.zero, WFCDirection.East),
                    Face(Vector3Int.zero, WFCDirection.West)),
                CreateCorridor("Wide_Corridor_Intersection", "corridor-wide-intersection", 0.75f, 0f,
                    Face(Vector3Int.zero, WFCDirection.North),
                    Face(Vector3Int.zero, WFCDirection.East),
                    Face(Vector3Int.zero, WFCDirection.South),
                    Face(Vector3Int.zero, WFCDirection.West)),
                CreateDefinition("Wide_Stairs", stairWithLanding, new Vector3Int(1, 2, 1), 0.35f, true,
                    WFCTileCategory.Stairs, 0f, false, new Vector3(0f, 0f, 2f),
                    Face(new Vector3Int(0, 0, 0), WFCDirection.South),
                    Face(new Vector3Int(0, 1, 0), WFCDirection.North))
            };

            WFCTileSet tileSet = AssetDatabase.LoadAssetAtPath<WFCTileSet>(TileSetPath);
            if (tileSet == null)
            {
                tileSet = ScriptableObject.CreateInstance<WFCTileSet>();
                AssetDatabase.CreateAsset(tileSet, TileSetPath);
            }

            Undo.RecordObject(tileSet, "Configure Wide Corridor WFC Tile Set");
            tileSet.boundarySocket = "Closed";
            tileSet.tiles.Clear();
            tileSet.tiles.AddRange(definitions);
            EditorUtility.SetDirty(tileSet);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = tileSet;
            EditorGUIUtility.PingObject(tileSet);
            Debug.Log("WFC: Wide Corridor Tile Set berhasil dibuat/diperbarui. Gunakan Cell Size (8, 4, 8).");
        }

        private static GameObject CreateOrUpdateStairLandingPrefab()
        {
            // Preserve collider and landing adjustments made directly on the prefab.
            // Rebuilding the tile set must not silently replace the user's stair geometry.
            GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                StairLandingPrefabPath);
            if (existingPrefab != null)
            {
                return existingPrefab;
            }

            GameObject stairModel = LoadModel("stairs-wide");
            if (stairModel == null)
            {
                return null;
            }

            var root = new GameObject("PF_Wide_Stairs_With_Landing");
            try
            {
                GameObject stairInstance = (GameObject)PrefabUtility.InstantiatePrefab(stairModel);
                stairInstance.name = "Stairs Wide Visual";
                stairInstance.transform.SetParent(root.transform, false);
                stairInstance.transform.localPosition = Vector3.zero;
                stairInstance.transform.localRotation = Quaternion.identity;
                stairInstance.transform.localScale = Vector3.one;

                GameObject floorModel = LoadModel("template-floor-big");
                Renderer materialSource = floorModel != null
                    ? floorModel.GetComponentInChildren<Renderer>(true)
                    : stairInstance.GetComponentInChildren<Renderer>(true);
                Material fillerMaterial = materialSource != null
                    ? materialSource.sharedMaterial
                    : null;

                // Mesh stairs-wide berhenti sekitar z=2 pada lantai atas,
                // sedangkan batas sel berada di z=4. Pelat tipis berikut
                // menyambungkan tangga ke corridor atas tanpa menutup ramp.
                CreateFillerCube(
                    root.transform,
                    "Gap Filler Upper Landing",
                    new Vector3(-0.1026f, 3.9931f, 3.0116f),
                    new Vector3(7.155539f, 0.10271688f, 2.3008628f),
                    fillerMaterial);
                CreateFillerCube(
                    root.transform,
                    "Gap Filler Upper Wall Left",
                    new Vector3(-3.69f, 6.1939f, 3.0116f),
                    new Vector3(0.56743425f, 4.5562353f, 2.3008628f),
                    fillerMaterial);
                CreateFillerCube(
                    root.transform,
                    "Gap Filler Upper Wall Right",
                    new Vector3(3.7161f, 6.1939f, 3.0116f),
                    new Vector3(0.56743425f, 4.5562353f, 2.3008628f),
                    fillerMaterial);
                CreateSmoothStairRamp(root.transform);

                PrefabUtility.SaveAsPrefabAsset(root, StairLandingPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(StairLandingPrefabPath);
        }

        private static void CreateSmoothStairRamp(Transform parent)
        {
            var ramp = new GameObject("AI Smooth Stair Ramp");
            ramp.transform.SetParent(parent, false);
            ramp.transform.localPosition = new Vector3(0f, 1.97f, -1.07f);
            ramp.transform.localRotation = Quaternion.Euler(-33.9f, 0f, 0f);
            ramp.transform.localScale = Vector3.one;

            BoxCollider collider = ramp.AddComponent<BoxCollider>();
            collider.center = Vector3.zero;
            collider.size = new Vector3(4f, 0.2f, 7.06f);
            collider.isTrigger = false;
        }

        private static void CreateFillerCube(
            Transform parent,
            string objectName,
            Vector3 localPosition,
            Vector3 localScale,
            Material material)
        {
            GameObject filler = GameObject.CreatePrimitive(PrimitiveType.Cube);
            filler.name = objectName;
            filler.transform.SetParent(parent, false);
            filler.transform.localPosition = localPosition;
            filler.transform.localRotation = Quaternion.identity;
            filler.transform.localScale = localScale;

            Renderer renderer = filler.GetComponent<Renderer>();
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        private static WFCTileDefinition CreateCorridor(
            string assetName, string modelName, float weight, float modelYaw,
            params WFCFaceSocket[] sockets)
        {
            return CreateDefinition(assetName, LoadModel(modelName), Vector3Int.one, weight, true,
                WFCTileCategory.Corridor, modelYaw, false, Vector3.zero, sockets);
        }

        private static WFCTileDefinition CreateDefinition(
            string assetName,
            GameObject prefab,
            Vector3Int size,
            float weight,
            bool allowRotations,
            WFCTileCategory category,
            float modelYaw,
            bool isBoundaryEndCap,
            Vector3 prefabPositionOffset,
            params WFCFaceSocket[] sockets)
        {
            string path = $"{DefinitionRoot}/TD_{assetName}.asset";
            WFCTileDefinition definition = AssetDatabase.LoadAssetAtPath<WFCTileDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<WFCTileDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            Undo.RecordObject(definition, "Configure Wide Corridor WFC Tile");
            definition.displayName = assetName;
            definition.prefab = prefab;
            definition.prefabPositionOffset = prefabPositionOffset;
            definition.prefabEulerOffset = new Vector3(0f, modelYaw, 0f);
            definition.category = category;
            definition.isBoundaryEndCap = isBoundaryEndCap;
            definition.size = size;
            definition.weight = weight;
            definition.allowYRotations = allowRotations;
            definition.defaultOuterSocket = "Closed";
            definition.faceSockets = new List<WFCFaceSocket>(sockets);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static WFCFaceSocket Face(Vector3Int cell, WFCDirection direction)
        {
            return new WFCFaceSocket
            {
                localCell = cell,
                direction = direction,
                socket = "WideWalkway"
            };
        }

        private static GameObject LoadModel(string modelName)
        {
            string path = $"{ModelRoot}/{modelName}.fbx";
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                Debug.LogWarning($"WFC: model tidak ditemukan: {path}");
            }

            return model;
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }
    }
}
