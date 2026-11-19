using System.Collections.Generic;
using UnityEngine;

namespace TesisQA.WFC
{
    [CreateAssetMenu(fileName = "TS_NewTileSet", menuName = "WFC/Tile Set")]
    public sealed class WFCTileSet : ScriptableObject
    {
        [Tooltip("Socket yang boleh menghadap keluar batas map.")]
        public string boundarySocket = "Closed";

        public List<WFCTileDefinition> tiles = new List<WFCTileDefinition>();

        public bool SocketsMatch(string first, string second)
        {
            return string.Equals(
                WFCTileDefinition.NormalizeSocket(first),
                WFCTileDefinition.NormalizeSocket(second),
                System.StringComparison.Ordinal);
        }

        private void OnValidate()
        {
            boundarySocket = WFCTileDefinition.NormalizeSocket(boundarySocket);
        }
    }
}
