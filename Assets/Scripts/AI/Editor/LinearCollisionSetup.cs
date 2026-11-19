using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    public static class LinearCollisionSetup
    {
        public const string Root = "Assets/Experiments/CollisionLinear";
        public const string ProtocolPath = Root + "/CollisionLinear.asset";
        public const string EvaluationScene = "Assets/Scenes/AI/QA_LinearCollisionExperiment.unity";

        [MenuItem("Tesis QA/Defect Experiment/Prepare participant trial profile")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.OpenScene(ParticipantGameBuild.ScenePath);
            var session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
            var source = AssetDatabase.LoadAssetAtPath<DefectExperimentProtocol>(DefectExperimentSetup.ProtocolPath);
            // Once calibration is published, rebuilding the participant Canvas
            // must retain the selected seeds and per-trial map dimensions.
            var protocol = AssetDatabase.LoadAssetAtPath<DefectExperimentProtocol>(CollisionSeedSearch.ProtocolPath);
            bool selectedSeeds = protocol != null;
            if (!selectedSeeds) protocol = AssetDatabase.LoadAssetAtPath<DefectExperimentProtocol>(ProtocolPath);
            if (protocol == null)
            {
                Directory.CreateDirectory(Root + "/Manifests");
                AssetDatabase.Refresh();
                protocol = UnityEngine.Object.Instantiate(source);
                protocol.name = "CollisionLinear";
                protocol.protocolId = "collision-linear-v1";
                protocol.fixedScenarioOrder = true;
                protocol.scenarios.Clear();
                for (int i = 0; i < 5; i++)
                {
                    var old = source.scenarios[i];
                    int total = i + 1;
                    var scenario = new DefectScenario { id = "L" + total, mapSeed = old.mapSeed,
                        defectSeed = old.defectSeed, spawnSeed = old.spawnSeed,
                        fullDefects = (total + 1) / 2, partialDefects = total / 2 };
                    using (var build = DefectScenarioBuilder.Build(protocol, scenario, session.generator, session.adapter, false))
                    {
                        if (build.manifest.defects.Count != total) throw new InvalidOperationException("Wrong linear defect count.");
                        scenario.frozenManifest = JsonUtility.ToJson(build.manifest);
                        File.WriteAllText(Root + "/Manifests/" + scenario.id + ".json", JsonUtility.ToJson(build.manifest, true));
                    }
                    protocol.scenarios.Add(scenario);
                }
                session.generator.ClearGenerated();
                AssetDatabase.CreateAsset(protocol, ProtocolPath);
                AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            }
            protocol.inspectWalls = true;
            if (!selectedSeeds) protocol.protocolId = "collision-linear-walls-v2";
            protocol.detectorVersion = "local-floor-wall-v3";
            protocol.scorerVersion = "collision-region-wall-v3";
            EditorUtility.SetDirty(protocol); AssetDatabase.SaveAssets();
            ValidateProtocol(protocol, session);
            session.protocol = protocol;
            if (protocol.scenarios[0].gridSize != Vector3Int.zero)
                session.generator.ConfigureRuntimeGrid(protocol.scenarios[0].gridSize);
            session.participantOrder = 0;
            ParticipantCanvasUI.Create(session);
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            EditorSceneManager.SaveScene(session.gameObject.scene);
            // Matching AI/researcher scene uses exactly the same new manifests.
            EditorSceneManager.OpenScene(DefectExperimentSetup.ScenePath);
            protocol = AssetDatabase.LoadAssetAtPath<DefectExperimentProtocol>(selectedSeeds ? CollisionSeedSearch.ProtocolPath : ProtocolPath);
            session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
            session.protocol = protocol; session.participantOrder = 0;
            if (protocol.scenarios[0].gridSize != Vector3Int.zero)
                session.generator.ConfigureRuntimeGrid(protocol.scenarios[0].gridSize);
            EditorSceneManager.SaveScene(session.gameObject.scene, EvaluationScene);
            EditorSceneManager.OpenScene(ParticipantGameBuild.ScenePath);
            Debug.Log("COLLISION TRIALS READY: fixed order with targets " + string.Join(",", protocol.scenarios.ConvertAll(s => s.fullDefects + s.partialDefects)) + "; original pilot preserved.");
        }

        private static void ValidateProtocol(DefectExperimentProtocol protocol, DefectExperimentSession session)
        {
            if (!protocol.fixedScenarioOrder || protocol.scenarios.Count != 5)
                throw new InvalidOperationException("Linear protocol must contain five fixed-order scenarios.");
            for (int i = 0; i < 5; i++)
            {
                var scenario = protocol.scenarios[i];
                int expected = protocol.protocolId == CollisionSeedSearch.StudyProtocolId ? CollisionSeedSearch.StudyTargets[i] : i + 1;
                if (scenario.fullDefects + scenario.partialDefects != expected)
                    throw new InvalidOperationException("Frozen trial target count drift.");
                for (int order = 0; order < 10; order++)
                    if (protocol.ScenarioIndex(i, order) != i) throw new InvalidOperationException("Progressive order was counterbalanced.");
                using (var build = DefectScenarioBuilder.Build(protocol, scenario, session.generator, session.adapter, true))
                {
                    foreach (var defect in build.manifest.defects)
                        if (DefectGeometry.Supported(defect.center)) throw new InvalidOperationException("Injected collider defect was healed.");
                }
            }
            session.generator.ClearGenerated();
        }
    }
}
