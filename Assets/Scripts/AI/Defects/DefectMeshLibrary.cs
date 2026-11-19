using System.Collections.Generic;
using UnityEngine;

namespace TesisQA.AI
{
    // CPU geometry is baked in the Editor. Runtime never needs Read/Write enabled
    // on shared FBX assets, nor auxiliary colliders that could heal a missing floor.
    public sealed class DefectMeshLibrary : ScriptableObject
    {
        public List<DefectMeshData> meshes = new List<DefectMeshData>();
        public DefectMeshData Find(Mesh mesh) => meshes.Find(item => item.source == mesh);
    }
}
