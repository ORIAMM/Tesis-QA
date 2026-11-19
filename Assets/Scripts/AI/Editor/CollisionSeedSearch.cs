using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.MLAgents;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TesisQA.AI.Editor
{
    // Calibration only: every attempted seed and rollout is retained. Selected
    // scenarios are deliberately conditioned on AI performance, not held-out tests.
    [InitializeOnLoad]
    public static class CollisionSeedSearch
    {
        public const string AssetRoot = "Assets/Experiments/CollisionSelected";
        public const string ProtocolPath = AssetRoot + "/CollisionSelected.asset";
        public const string StudyProtocolId = "collision-small-large-study-v1";
        public static readonly int[] StudyTargets = { 1, 2, 2, 1, 2 };
        private const string CandidatePath = AssetRoot + "/SearchCandidateStudy.asset";
        private const string Active = "CollisionSeedSearch.Active", Output = "CollisionSeedSearch.Output";

        [Serializable] private sealed class State
        {
            public string goal, roundOutput;
            public int round;
            public bool validating;
            public int[] attempts = new int[5];
            public bool[] replace = { true, true, true, true, true };
            public int[] testedIndices;
        }

        static CollisionSeedSearch()
        { EditorApplication.playModeStateChanged += Changed; }

        public static void Run()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Seed calibration requires the isolated .ai-eval project.");
            string output = null, goal = "all";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "-defectOutput") output = Path.GetFullPath(args[i + 1]);
                if (args[i] == "-seedSearchGoal") goal = args[i + 1];
            }
            if (output == null || Directory.Exists(output) || goal != "all" && goal != "any")
                throw new InvalidOperationException("Use a NEW output and seedSearchGoal all or any.");
            Directory.CreateDirectory(output); Directory.CreateDirectory(AssetRoot + "/Manifests");
            SessionState.SetString(Output, output); SessionState.SetBool(Active, true);
            if (AssetDatabase.LoadAssetAtPath<DefectExperimentProtocol>(CandidatePath) != null)
                throw new InvalidOperationException("A search candidate already exists; preserve/rename its assets before a new search.");
            AssetDatabase.Refresh();
            var source = AssetDatabase.LoadAssetAtPath<DefectExperimentProtocol>(LinearCollisionSetup.ProtocolPath);
            var protocol = UnityEngine.Object.Instantiate(source);
            protocol.name = "SearchCandidate";
            protocol.protocolId = StudyProtocolId;
            protocol.deterministicInference = true;
            protocol.fixedStepTransitions = true;
            protocol.automaticRepetitions = 1;
            protocol.scenarios.Clear();
            // Prior calibrated layouts provide warm candidates; changed target
            // counts are rebuilt and must pass real rollouts again.
            int[] maps = { 281008198, 281104102, 283827465, 283113394, 282796144 };
            int[] defects = { 411016382, 411032861, 416250625, 414424226, 414612522 };
            int[] spawns = { 28324, 28323, 52039, 43790, 29053 };
            for (int i = 0; i < 5; i++) protocol.scenarios.Add(new DefectScenario
            {
                id = "L" + (i + 1), fullDefects = (StudyTargets[i] + 1) / 2, partialDefects = StudyTargets[i] / 2,
                mapSeed = maps[i], defectSeed = defects[i], spawnSeed = spawns[i],
                gridSize = i < 3 ? new Vector3Int(6, 2, 6) : new Vector3Int(12, 2, 12)
            });
            AssetDatabase.CreateAsset(protocol, CandidatePath); AssetDatabase.SaveAssets();
            File.WriteAllText(Path.Combine(output, "METHOD.txt"),
                "AI-conditioned seed calibration, not unbiased held-out evaluation.\n"
                + "Goal=" + goal + "; budget=150 simulation seconds; Phase3R frozen; no training.\n"
                + "Trials L1-L3: 6x2x6; L4-L5: 12x2x12; targets 1,2,2,1,2 (user revised design).\n"
                + "Initial candidates reuse prior calibrated seed triplets, rebuilt under the new target counts.\n"
                + "Search uses one rollout per candidate set. A final three-repetition campaign is retained.\n"
                + "Acceptance requires every trial to meet the goal on ALL three final repetitions.\n"
                + "Unsuccessful validation campaigns remain in the search history.\n"
                + "Deterministic inference model outputs; inference seed 58231; LSTM resets per episode.\n"
                + "Automatic trial/respawn transitions on physics ticks; initial decision phase aligned per episode.\n"
                + "Unsuccessful seeds, generation failures, and all raw reports are preserved.\n"
                + "Trace-guided seed filtering may use prior completed runs with >=30% coverage on both floors.\n"
                + "Up to 64 defect seeds per round may be screened near that recorded route (partial 3.5m/full 5.5m).\n"
                + "Topology reachability screens removed full-collider cells before candidate rollouts.\n"
                + "Calibration may run only unresolved scenarios; final validation always runs all five.\n"
                + "These are candidate filters, not detections; every candidate still requires real injected-map rollouts.\n");
            Save(new State { goal = goal });
            BeginRound();
        }

        public static void Resume()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use the isolated batch project.");
            var args = Environment.GetCommandLineArgs();
            string output = null;
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-defectOutput") output = Path.GetFullPath(args[i + 1]);
            if (output == null || !File.Exists(Path.Combine(output, "search-state.json")))
                throw new InvalidOperationException("Existing search state required.");
            SessionState.SetString(Output, output); SessionState.SetBool(Active, true);
            string error = Path.Combine(output, "search-error.txt");
            if (File.Exists(error)) File.Move(error, error + ".resolved-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"));
            Journal("RESUME existing search; all previous evidence preserved.");
            BeginRound();
        }

        private static State Load() => JsonUtility.FromJson<State>(File.ReadAllText(Path.Combine(SessionState.GetString(Output, ""), "search-state.json")));
        private static void Save(State state) => File.WriteAllText(Path.Combine(SessionState.GetString(Output, ""), "search-state.json"), JsonUtility.ToJson(state, true));
        private static void Journal(string message) => File.AppendAllText(Path.Combine(SessionState.GetString(Output, ""), "search-journal.txt"), DateTime.UtcNow.ToString("o") + " " + message + "\n");

        private static void BeginRound()
        {
            try
            {
                var state = Load();
                EditorSceneManager.OpenScene(DefectExperimentSetup.ScenePath);
                var protocol = AssetDatabase.LoadAssetAtPath<DefectExperimentProtocol>(CandidatePath);
                var session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
                for (int i = 0; i < 5; i++)
                {
                    if (!state.replace[i]) continue;
                    var scenario = protocol.scenarios[i];
                    int warmMap = scenario.mapSeed, warmDefect = scenario.defectSeed, warmSpawn = scenario.spawnSeed;
                    // Alternate guided candidates with fresh layouts/spawns so
                    // one promising but fragile reference cannot monopolize search.
                    var reference = state.round % 2 == 0 ? BestReference(scenario.id, state.round / 2) : null;
                    bool baked = false;
                    for (int generationAttempt = 0; generationAttempt < 2000; generationAttempt++)
                    {
                        int attempt = ++state.attempts[i];
                        int guidedLimit = 64;
                        bool guided = reference != null && generationAttempt < guidedLimit;
                        scenario.mapSeed = guided ? reference.manifest.requestedMapSeed : 281000000 + i * 100003 + attempt * 4099;
                        scenario.defectSeed = 411000000 + i * 97 + attempt * 8191;
                        // Explore both starting floors; an even stride would pin
                        // every candidate of a trial to the same floor forever.
                        bool retainSpawn = guided && state.round % 4 != 0;
                        scenario.spawnSeed = retainSpawn ? reference.manifest.spawnSeed : 28320 + i + attempt * 37;
                        bool warm = state.round == 0 && generationAttempt == 0;
                        if (warm)
                        {
                            scenario.mapSeed = warmMap; scenario.spawnSeed = warmSpawn; scenario.defectSeed = warmDefect;
                            Journal("WARM_CANDIDATE " + scenario.id + ": rebuild prior seed triplet under revised target count.");
                        }
                        try
                        {
                            using (var built = DefectScenarioBuilder.Build(protocol, scenario, session.generator, session.adapter, false))
                            {
                                if (!TargetsRemainReachable(built, session))
                                {
                                    Journal($"TOPOLOGY_PREFILTER_REJECT {scenario.id} attempt={attempt} map={scenario.mapSeed} defect={scenario.defectSeed} spawn={scenario.spawnSeed}");
                                    continue;
                                }
                                if (guided && !warm && !NearRecordedRoute(built.manifest, reference))
                                {
                                    Journal($"ROUTE_PREFILTER_REJECT {scenario.id} attempt={attempt} map={scenario.mapSeed} defect={scenario.defectSeed} spawn={scenario.spawnSeed}");
                                    continue;
                                }
                                scenario.frozenManifest = JsonUtility.ToJson(built.manifest);
                                Journal($"BAKED {scenario.id} attempt={attempt} map={scenario.mapSeed} actual={built.manifest.actualMapSeed} defect={scenario.defectSeed} spawn={scenario.spawnSeed} size={scenario.gridSize} routeGuided={guided}");
                            }
                            baked = true; break;
                        }
                        catch (Exception error)
                        { Journal($"GENERATION_REJECT {scenario.id} attempt={attempt} map={scenario.mapSeed}: {error.Message}"); }
                        finally { session.generator.ClearGenerated(); }
                    }
                    if (!baked) throw new InvalidOperationException("2000 candidates failed generation/prefilters for " + scenario.id + "; inspect constraints.");
                }
                protocol.automaticRepetitions = state.validating ? 3 : 1;
                state.testedIndices = state.validating ? Enumerable.Range(0, 5).ToArray()
                    : Enumerable.Range(0, 5).Where(index => state.replace[index]).ToArray();
                EditorUtility.SetDirty(protocol); AssetDatabase.SaveAssets();
                state.round++;
                state.roundOutput = Path.Combine(SessionState.GetString(Output, ""), "round_" + state.round.ToString("D4") + (state.validating ? "_validation" : "_search"));
                Save(state);
                session.protocol = protocol; session.participantPresentation = false;
                session.mode = DefectExperimentMode.AI; session.participant = "AI_SEED_CALIBRATION";
                session.autoStart = session.batchRun = true; session.outputDirectory = state.roundOutput;
                session.smokeDurationOverride = 0; session.automaticTimeScale = 20;
                session.calibrationScenarioIndices = state.validating ? Array.Empty<int>() : state.testedIndices;
                CommunicatorFactory.Enabled = false;
                Journal("START " + state.roundOutput);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error) { Fail(error); }
        }

        private static DefectTrialReport BestReference(string scenarioId, int variant)
        {
            // Selecting seeds using prior AI traces is a calibration heuristic,
            // never an observation/goal provided to the running policy.
            string root = SessionState.GetString(Output, "");
            var paths = Directory.GetFiles(root, scenarioId + "_r1.json", SearchOption.AllDirectories).AsEnumerable();
            var references = paths
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => JsonUtility.FromJson<DefectTrialReport>(File.ReadAllText(path)))
                .Where(t => t.status == "complete" && t.floorZeroCoverage >= .3f && t.floorOneCoverage >= .3f)
                .GroupBy(t => t.manifest.geometrySha256 + "|" + t.manifest.spawnSeed)
                .Select(group => group.OrderByDescending(t => t.truePositives).ThenByDescending(t => t.coverage).First())
                .OrderByDescending(t => t.truePositives)
                .ThenByDescending(t => Mathf.Min(t.floorZeroCoverage, t.floorOneCoverage) + .25f * t.coverage)
                .Take(5).ToArray();
            return references.Length == 0 ? null : references[variant % references.Length];
        }

        private static bool TargetsRemainReachable(DefectScenarioBuild built, DefectExperimentSession session)
        {
            var missing = new HashSet<string>(built.manifest.defects
                .Where(defect => defect.kind == CollisionDefectKind.FullCollider).Select(defect => defect.surfaceId));
            var blocked = new HashSet<Vector3Int>(built.floorSurfaceIds.Where(cell => missing.Contains(cell.Value)).Select(cell => cell.Key));
            var graph = session.generator.WalkwayConnections;
            var spawn = session.generator.WorldToWalkwayCell(built.manifest.spawn);
            var reached = new HashSet<Vector3Int>(); var queue = new Queue<Vector3Int>();
            reached.Add(spawn); queue.Enqueue(spawn);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                if (!graph.TryGetValue(cell, out var neighbours)) continue;
                foreach (var next in neighbours)
                    if (!blocked.Contains(next) && reached.Add(next)) queue.Enqueue(next);
            }
            foreach (var defect in built.manifest.defects)
            {
                var cells = built.floorSurfaceIds.Where(cell => cell.Value == defect.surfaceId).Select(cell => cell.Key).ToArray();
                if (defect.kind == CollisionDefectKind.PartialHole)
                {
                    if (!cells.Any(reached.Contains)) return false;
                }
                else if (!cells.Any(cell => graph.TryGetValue(cell, out var neighbours)
                    && neighbours.Any(next => next.y == cell.y && reached.Contains(next)))) return false;
            }
            return true;
        }

        private static bool NearRecordedRoute(DefectScenarioManifest manifest, DefectTrialReport reference)
        {
            return manifest.defects.All(defect => reference.trace.Any(sample =>
            {
                if (Mathf.Abs(sample.position.y - defect.center.y) > .6f) return false;
                // Trace samples are 0.5s apart; a conservative screen must allow
                // the real ray fan to see surfaces between those sample poses.
                float radius = defect.kind == CollisionDefectKind.PartialHole ? 3.5f : 5.5f;
                var delta = sample.position - defect.center; delta.y = 0;
                return delta.sqrMagnitude <= radius * radius;
            }));
        }

        private static bool MeetsGoal(DefectTrialReport trial, State state) =>
            trial.truePositives >= (state.goal == "all" ? trial.manifest.defects.Count : 1);

        private static void Changed(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Active, false)) return;
            EditorApplication.delayCall += FinishRound;
        }

        private static void FinishRound()
        {
            try
            {
                var state = Load();
                string done = Path.Combine(state.roundOutput, "session.done");
                if (!File.Exists(done) || File.ReadAllText(done) != "complete")
                    throw new InvalidOperationException("Rollout failed; inspect " + state.roundOutput + "/error.txt");
                var trials = Directory.GetFiles(state.roundOutput, "L*_r*.json").Select(path =>
                    JsonUtility.FromJson<DefectTrialReport>(File.ReadAllText(path))).ToList();
                var tested = state.testedIndices ?? Enumerable.Range(0, 5).ToArray();
                if (trials.Count != (state.validating ? 15 : tested.Length) || trials.Any(t => t.status != "complete"))
                    throw new InvalidOperationException("Incomplete campaign evidence.");
                for (int i = 0; i < 5; i++)
                {
                    if (!tested.Contains(i)) continue;
                    var scenarioTrials = trials.Where(t => t.manifest.scenarioId == "L" + (i + 1)).ToList();
                    state.replace[i] = scenarioTrials.Any(t => !MeetsGoal(t, state));
                    foreach (var trial in scenarioTrials.OrderBy(t => t.repetition))
                        Journal($"RESULT round={state.round} {trial.trialId} found={trial.truePositives}/{trial.manifest.defects.Count} first={trial.firstTrueDetectionSeconds.ToString("R", CultureInfo.InvariantCulture)} accepted={!state.replace[i]}");
                }
                if (state.replace.All(replace => !replace))
                {
                    if (state.validating) { Save(state); Publish(state, trials); return; }
                    state.validating = true;
                    Journal("All five search rollouts passed; starting final three-repetition campaign.");
                }
                else state.validating = false;
                Save(state); BeginRound();
            }
            catch (Exception error) { Fail(error); }
        }

        private static void Publish(State state, List<DefectTrialReport> trials)
        {
            var protocol = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<DefectExperimentProtocol>(CandidatePath));
            protocol.name = "CollisionSelected";
            AssetDatabase.CreateAsset(protocol, ProtocolPath);
            foreach (var scenario in protocol.scenarios)
                File.WriteAllText(AssetRoot + "/Manifests/" + scenario.id + ".json",
                    JsonUtility.ToJson(JsonUtility.FromJson<DefectScenarioManifest>(scenario.frozenManifest), true));
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            foreach (string scenePath in new[] { ParticipantGameBuild.ScenePath, LinearCollisionSetup.EvaluationScene })
            {
                EditorSceneManager.OpenScene(scenePath);
                protocol = AssetDatabase.LoadAssetAtPath<DefectExperimentProtocol>(ProtocolPath);
                var session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
                session.protocol = protocol;
                session.calibrationScenarioIndices = Array.Empty<int>();
                foreach (var scenario in protocol.scenarios)
                {
                    using (var built = DefectScenarioBuilder.Build(protocol, scenario, session.generator, session.adapter, true))
                    {
                        if (session.generator.GridSize != scenario.gridSize || built.manifest.defects.Count != scenario.fullDefects + scenario.partialDefects)
                            throw new InvalidOperationException("Scene/manifest size or target drift.");
                    }
                }
                session.generator.ClearGenerated(); session.generator.ConfigureRuntimeGrid(protocol.scenarios[0].gridSize);
                EditorSceneManager.MarkSceneDirty(session.gameObject.scene); EditorSceneManager.SaveScene(session.gameObject.scene);
            }
            var chosen = new List<string> { "trial,grid_x,grid_y,grid_z,map_seed,actual_map_seed,defect_seed,spawn_seed,target,found_first_rollout,first_true_seconds,all_found_seconds" };
            foreach (var scenario in protocol.scenarios)
            {
                var trial = trials.Single(t => t.manifest.scenarioId == scenario.id && t.repetition == 1);
                var firstHits = trial.findings.Where(f => f.truePositive).GroupBy(f => f.matchedDefectId)
                    .Select(group => group.Min(f => f.simulationSeconds)).ToList();
                string all = firstHits.Count == trial.manifest.defects.Count ? firstHits.Max().ToString("R", CultureInfo.InvariantCulture) : "";
                chosen.Add(string.Join(",", scenario.id, scenario.gridSize.x, scenario.gridSize.y, scenario.gridSize.z,
                    scenario.mapSeed, trial.manifest.actualMapSeed, scenario.defectSeed, scenario.spawnSeed,
                    trial.manifest.defects.Count, trial.truePositives,
                    trial.firstTrueDetectionSeconds.ToString("R", CultureInfo.InvariantCulture), all));
            }
            File.WriteAllLines(Path.Combine(SessionState.GetString(Output, ""), "selected-seeds.csv"), chosen);
            File.WriteAllText(Path.Combine(SessionState.GetString(Output, ""), "scene-parity-checks.txt"),
                "PASS: AI and participant scenes reference the same selected protocol; all five frozen manifests rebuilt identically in both scenes; grid 6x2x6 / 12x2x12; targets 1,2,2,1,2.\n");
            File.WriteAllText(Path.Combine(SessionState.GetString(Output, ""), "selection.done"), state.roundOutput);
            Journal("PUBLISHED " + ProtocolPath + "; selection-conditioned results retained at " + state.roundOutput);
            SessionState.SetBool(Active, false);
            EditorApplication.delayCall += () => EditorApplication.Exit(0);
        }

        private static void Fail(Exception error)
        {
            SessionState.SetBool(Active, false);
            File.WriteAllText(Path.Combine(SessionState.GetString(Output, ""), "search-error.txt"), error.ToString());
            Debug.LogException(error); EditorApplication.delayCall += () => EditorApplication.Exit(1);
        }
    }
}
