using UnityEditor;
using UnityEngine;

namespace TesisQA.WFC.Editor
{
    [CustomEditor(typeof(WFCGenerator))]
    public sealed class WFCGeneratorEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space(10f);

            var generator = (WFCGenerator)target;
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Generate WFC", GUILayout.Height(32f)))
                {
                    generator.Generate();
                    EditorUtility.SetDirty(generator);
                }

                if (GUILayout.Button("Clear", GUILayout.Height(32f)))
                {
                    generator.ClearGenerated();
                    EditorUtility.SetDirty(generator);
                }
            }

            EditorGUILayout.HelpBox(
                "Wide Corridor memakai Cell Size (8, 4, 8). Batas horizontal hanya berisi Void " +
                "atau Corridor Wide End yang bukaannya menghadap ke dalam. Grid Y = 1 tidak memakai tangga; " +
                "gunakan Grid Y >= 2 untuk mengizinkan Stairs Wide.",
                MessageType.Info);
        }
    }
}
