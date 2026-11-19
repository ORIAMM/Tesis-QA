using System;
using System.Collections.Generic;
using UnityEngine;

namespace TesisQA.WFC
{
    public enum WFCTileCategory
    {
        Other,
        Filler,
        Corridor,
        Room,
        Stairs
    }

    [Serializable]
    public sealed class WFCFaceSocket
    {
        [Tooltip("Sel lokal pada volume tile. Nilainya dimulai dari (0,0,0).")]
        public Vector3Int localCell;

        public WFCDirection direction;

        [Tooltip("Socket hanya cocok dengan teks socket yang sama persis.")]
        public string socket = "Walkway";
    }

    [CreateAssetMenu(fileName = "TD_NewTile", menuName = "WFC/Tile Definition")]
    public sealed class WFCTileDefinition : ScriptableObject
    {
        [Header("Visual")]
        public GameObject prefab;

        [Tooltip("Nama yang digunakan pada object hasil generate.")]
        public string displayName;

        [Tooltip("Offset tambahan sesudah prefab diletakkan pada pusat footprint tile.")]
        public Vector3 prefabPositionOffset;

        [Tooltip("Rotasi bawaan model. Rotasi WFC 0/90/180/270 ditambahkan setelah nilai ini.")]
        public Vector3 prefabEulerOffset;

        [Header("Grid")]
        [Tooltip("Jenis tile. Generator memakai Filler untuk Void dan Stairs untuk tile bertingkat.")]
        public WFCTileCategory category;

        [Tooltip("Aktifkan hanya pada tile satu-bukaan yang menutup ujung jalan. Pada perimeter, bukaannya wajib menghadap ke dalam map.")]
        public bool isBoundaryEndCap;

        [Tooltip("Jumlah sel yang ditempati tile. Stairs Wide memakai ukuran 1x2x1.")]
        public Vector3Int size = Vector3Int.one;

        [Min(0.0001f)]
        [Tooltip("Peluang relatif tile dipilih. Nilai besar membuat tile lebih sering muncul.")]
        public float weight = 1f;

        [Tooltip("Buat varian rotasi Y 0, 90, 180, dan 270 derajat secara otomatis.")]
        public bool allowYRotations = true;

        [Header("Sockets")]
        [Tooltip("Socket seluruh sisi luar yang tidak ditimpa oleh daftar Face Sockets.")]
        public string defaultOuterSocket = "Closed";

        [Tooltip("Override socket pada sisi tertentu. Sisi internal volume dibuat otomatis.")]
        public List<WFCFaceSocket> faceSockets = new List<WFCFaceSocket>();

        public string TileName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        public WFCTileCategory EffectiveCategory
        {
            get
            {
                if (category != WFCTileCategory.Other)
                {
                    return category;
                }

                string tileName = TileName.ToLowerInvariant();
                if (tileName.Contains("room")) return WFCTileCategory.Room;
                if (tileName.Contains("corridor") || tileName.Contains("hallway")) return WFCTileCategory.Corridor;
                if (tileName.Contains("stair")) return WFCTileCategory.Stairs;
                if (tileName.Contains("void") || prefab == null) return WFCTileCategory.Filler;
                return WFCTileCategory.Other;
            }
        }

        public string GetOuterSocket(Vector3Int localCell, WFCDirection direction)
        {
            for (int i = faceSockets.Count - 1; i >= 0; i--)
            {
                WFCFaceSocket face = faceSockets[i];
                if (face != null && face.localCell == localCell && face.direction == direction)
                {
                    return NormalizeSocket(face.socket);
                }
            }

            return NormalizeSocket(defaultOuterSocket);
        }

        public bool ContainsLocalCell(Vector3Int localCell)
        {
            return localCell.x >= 0 && localCell.x < size.x
                && localCell.y >= 0 && localCell.y < size.y
                && localCell.z >= 0 && localCell.z < size.z;
        }

        public static string NormalizeSocket(string socket)
        {
            return string.IsNullOrWhiteSpace(socket) ? "Closed" : socket.Trim();
        }

        private void OnValidate()
        {
            size = new Vector3Int(
                Mathf.Max(1, size.x),
                Mathf.Max(1, size.y),
                Mathf.Max(1, size.z));
            weight = Mathf.Max(0.0001f, weight);
            defaultOuterSocket = NormalizeSocket(defaultOuterSocket);
        }
    }
}
