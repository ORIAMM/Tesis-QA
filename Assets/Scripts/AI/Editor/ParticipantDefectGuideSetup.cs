using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class ParticipantDefectGuideSetup
    {
        private const string Folder = "Assets/Experiments/CollisionSelected/Resources/ParticipantDefectGuide";

        public static void Run()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use the isolated batch project.");
            string output = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-defectOutput") output = Path.GetFullPath(args[i + 1]);
            if (output == null) throw new InvalidOperationException("Check output required.");
            foreach (string name in new[] { "PartialColliderExample", "FullTileColliderExample" })
            {
                string path = Folder + "/" + name + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Missing reference image: " + path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.mipmapEnabled = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                if (Resources.Load<Sprite>("ParticipantDefectGuide/" + name) == null)
                    throw new InvalidOperationException("Example sprite cannot be loaded in player resources.");
            }
            ParticipantMenuPreview.Prepare();
            ParticipantMenuPreview.Capture(output + "-preview");
            ParticipantGameChecks.Run();
        }
    }
}
