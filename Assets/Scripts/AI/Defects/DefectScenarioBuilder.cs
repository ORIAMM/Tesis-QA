using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TesisQA.WFC;
using UnityEngine;

namespace TesisQA.AI
{
    public sealed class DefectScenarioBuild : IDisposable
    {
        public DefectGeometry geometry;
        public DefectScenarioManifest manifest;
        public readonly Dictionary<Vector3Int, Vector3> coverageCells = new Dictionary<Vector3Int, Vector3>();
        public readonly Dictionary<Vector3Int, string> floorSurfaceIds = new Dictionary<Vector3Int, string>();
        public readonly List<Mesh> runtimeMeshes = new List<Mesh>();
        public void Dispose()
        {
            foreach (Mesh mesh in runtimeMeshes)
                if (mesh != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(mesh); else UnityEngine.Object.DestroyImmediate(mesh); }
            runtimeMeshes.Clear();
        }
    }

    public static class DefectScenarioBuilder
    {
        private sealed class Candidate { public DefectVisualHit hit; public Vector3Int cell; }

        public static DefectScenarioBuild Build(DefectExperimentProtocol protocol, DefectScenario scenario,
            WFCGenerator generator, AIWFCTrainingAdapter adapter, bool enforceFrozen)
        {
            var build = new DefectScenarioBuild();
            try
            {
                if (scenario.gridSize != Vector3Int.zero)
                {
                    if (scenario.gridSize.y != 2)
                        throw new InvalidOperationException("Collider scenarios require two floors.");
                    generator.ConfigureRuntimeGrid(scenario.gridSize);
                }
                if (!adapter.PrepareDefectExperiment(scenario.mapSeed, protocol.meshLibrary, build.runtimeMeshes))
                    throw new InvalidOperationException("Map generation failed: " + scenario.id);
                Physics.SyncTransforms();
                build.geometry = new DefectGeometry(generator.GeneratedRoot, protocol.meshLibrary);
                var manifest = build.manifest = new DefectScenarioManifest { scenarioId = scenario.id,
                    requestedMapSeed = scenario.mapSeed, actualMapSeed = generator.LastUsedSeed,
                    spawnSeed = scenario.spawnSeed, defectSeed = scenario.defectSeed };
                var fingerprint = new StringBuilder();
                foreach (Transform tile in generator.GeneratedRoot)
                    manifest.layout.Add(new DefectLayoutModule { name = tile.name, position = tile.localPosition,
                        euler = tile.localEulerAngles, scale = tile.localScale });
                foreach (var surface in build.geometry.surfaces)
                    fingerprint.Append(surface.id).Append('|').Append(surface.data.sha256).Append('|')
                        .Append(DefectGeometry.Vector(surface.transform.position)).Append('|')
                        .Append(DefectGeometry.Vector(surface.transform.eulerAngles)).Append('|')
                        .Append(DefectGeometry.Vector(surface.transform.lossyScale)).Append(';');
                manifest.geometrySha256 = DefectGeometry.Hash(fingerprint.ToString());
                manifest.configurationSha256 = DefectGeometry.Hash("collision-pilot-v1|" +
                    DefectGeometry.Vector(generator.CellSize) + "|" + protocol.partialHoleWidth.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                    + (scenario.gridSize != Vector3Int.zero ? "|grid=" + generator.GridSize : ""));
                var candidates = new List<Candidate>();
                foreach (var cell in generator.WalkwayConnections.Keys.OrderBy(c => c.y).ThenBy(c => c.x).ThenBy(c => c.z))
                {
                    if (generator.IsStairCell(cell)) continue;
                    Vector3 center = generator.WalkwayCellToWorld(cell);
                    if (!build.geometry.Cast(new Ray(center + Vector3.up * 1.8f, Vector3.down), 3f, out var hit)
                        || hit.normal.y < .99f || hit.surface.collider == null || !DefectGeometry.Supported(hit.point)) continue;
                    candidates.Add(new Candidate { cell = cell, hit = hit });
                    build.coverageCells[cell] = hit.point;
                    build.floorSurfaceIds[cell] = hit.surface.id;
                }
                var spawnChoices = candidates.Where(c => c.cell.y == (scenario.spawnSeed & 1)).ToList();
                if (spawnChoices.Count == 0) throw new InvalidOperationException("No safe spawn on requested floor.");
                var spawn = spawnChoices[new System.Random(scenario.spawnSeed).Next(spawnChoices.Count)];
                manifest.spawn = spawn.hit.point + Vector3.up * .08f;
                manifest.spawnYaw = new System.Random(scenario.spawnSeed ^ 0x73821).Next(4) * 90f;
                var random = new System.Random(scenario.defectSeed);
                for (int i = candidates.Count - 1; i > 0; i--)
                { int j = random.Next(i + 1); var temp = candidates[i]; candidates[i] = candidates[j]; candidates[j] = temp; }
                int requested = scenario.fullDefects + scenario.partialDefects;
                var used = new HashSet<string>();
                for (int index = 0; index < requested; index++)
                {
                    var kind = index < scenario.fullDefects ? CollisionDefectKind.FullCollider : CollisionDefectKind.PartialHole;
                    bool placed = false;
                    foreach (var candidate in candidates)
                    {
                        var hit = candidate.hit;
                        if (candidate.cell.y != (index & 1) || used.Contains(hit.surface.id)
                            || Vector3.Distance(hit.point, manifest.spawn) < 12f
                            || manifest.defects.Any(d => Vector3.Distance(d.center, hit.point) < 10f)) continue;
                        MeshCollider collider = hit.surface.collider;
                        Mesh original = collider.sharedMesh, modified = null;
                        if (kind == CollisionDefectKind.FullCollider) collider.enabled = false;
                        else { modified = DefectGeometry.CutHole(hit.surface, hit.point, protocol.partialHoleWidth); collider.sharedMesh = modified; }
                        Physics.SyncTransforms();
                        bool verified = !DefectGeometry.Supported(hit.point);
                        // The actual removed area must be player-sized, with no fallback collider.
                        foreach (Vector3 offset in new[] { Vector3.left * .6f, Vector3.right * .6f, Vector3.forward * .6f, Vector3.back * .6f })
                            verified &= !DefectGeometry.Supported(hit.point + offset);
                        if (!verified)
                        {
                            collider.enabled = true; collider.sharedMesh = original;
                            if (modified != null) { if (Application.isPlaying) UnityEngine.Object.Destroy(modified); else UnityEngine.Object.DestroyImmediate(modified); }
                            continue;
                        }
                        if (modified != null) build.runtimeMeshes.Add(modified);
                        manifest.defects.Add(new InjectedCollisionDefect { id = scenario.id + "-D" + (index + 1),
                            surfaceId = hit.surface.id, center = hit.point, floor = candidate.cell.y,
                            kind = kind, width = kind == CollisionDefectKind.PartialHole ? protocol.partialHoleWidth : 0f });
                        used.Add(hit.surface.id); placed = true; break;
                    }
                    if (!placed) throw new InvalidOperationException("Cannot place verified defect " + index + " in " + scenario.id);
                }
                Physics.SyncTransforms();
                if (!DefectGeometry.Supported(spawn.hit.point)) throw new InvalidOperationException("Injection affected the spawn.");
                if (enforceFrozen && (string.IsNullOrEmpty(scenario.frozenManifest)
                    || scenario.frozenManifest != JsonUtility.ToJson(manifest)))
                    throw new InvalidOperationException("Scenario drift: " + scenario.id + ". Re-bake a NEW protocol before recruiting participants.");
                return build;
            }
            catch { build.Dispose(); throw; }
        }

        // This scorer is the ONLY reader of injection labels during a trial.
        // Detector emits visual/physics mismatches without access to the manifest.
        public static void Score(DefectTrialReport trial, DefectFinding finding)
        {
            bool wall = finding.surfaceKind == "wall";
            var defect = trial.manifest.defects.Find(d => d.surfaceId == finding.surfaceId
                // Match the same 0.2m nominal-floor band admitted by the detector,
                // including trim on a fully removed collider. Extra 0.01m is numeric tolerance.
                && (wall ? finding.normal.sqrMagnitude > .5f && Mathf.Abs(finding.normal.y) <= .15f
                    && finding.position.y - d.center.y >= .05f && finding.position.y - d.center.y <= 2.5f
                    : Mathf.Abs(d.center.y - finding.position.y) <= .21f)
                && (d.kind == CollisionDefectKind.FullCollider
                    || Mathf.Abs(d.center.x - finding.position.x) <= d.width * .5f + .01f
                    && Mathf.Abs(d.center.z - finding.position.z) <= d.width * .5f + .01f));
            finding.matchedDefectId = defect?.id;
            finding.truePositive = defect != null;
            finding.duplicate = trial.findings.Exists(previous => defect != null
                ? previous.matchedDefectId == defect.id
                : !previous.truePositive && previous.surfaceId == finding.surfaceId
                    && Vector3.Distance(previous.position, finding.position) < 1f);
            if (finding.duplicate) trial.duplicates++;
            else if (finding.truePositive)
            {
                trial.truePositives++;
                if (trial.firstTrueDetectionSeconds < 0f) trial.firstTrueDetectionSeconds = finding.simulationSeconds;
            }
            else trial.falsePositives++;
            if (trial.firstDetectionSeconds < 0f) trial.firstDetectionSeconds = finding.simulationSeconds;
            trial.findings.Add(finding);
        }
    }
}
