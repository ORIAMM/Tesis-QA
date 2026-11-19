using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TesisQA.AI.Editor
{
    public static class AIBranchObservationChecks
    {
        [MenuItem("Tools/AI/Validate Branch Observation")]
        public static void Validate()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Branch OBS temporary tests");
            root.transform.position = new Vector3(9000f, 9000f, 9000f);
            try
            {
                var obj = new GameObject("Sensor only, no motor/policy");
                obj.transform.SetParent(root.transform, false);
                var body = obj.AddComponent<CharacterController>();
                body.height = 1.8f; body.radius = 0.35f; body.center = Vector3.up * 0.9f; body.skinWidth = 0.04f;
                var sensor = obj.AddComponent<BranchMemorySensorComponent>();
                sensor.ResetEpisode();
                Check(sensor.CreateSensors()[0].GetObservationSpec().Shape[0] == 32, "Wrong branch schema.");
                sensor.ObservationEnabled = false; Check(sensor.CreateSensors().Length == 0, "Opt-out failed.");
                sensor.ObservationEnabled = true;
                float[] values = new float[32]; sensor.Capture(values);
                Check(values.All(v => v == 0f), "Void must be unknown, not blocked/complete.");
                var floor = Box(root.transform, "Floor", new Vector3(0f, -0.1f, -2f), new Vector3(20f, 0.2f, 24f));
                var probe = typeof(BranchMemorySensorComponent).GetMethod("Probe", BindingFlags.Instance | BindingFlags.NonPublic);
                Func<AIObservedBranchGraph.PortState> ahead = () => (AIObservedBranchGraph.PortState)probe.Invoke(sensor,
                    new object[] {root.transform.position, root.transform.position + Vector3.forward * 2f, root.transform.position});
                var ramp = Box(root.transform, "Untagged ramp", new Vector3(0f, 0.35f, 1f), new Vector3(4f, 0.2f, 4f));
                ramp.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f); Physics.SyncTransforms();
                Check(ahead() == AIObservedBranchGraph.PortState.Unknown, "Uphill ramp became a closed wall.");
                floor.SetActive(false); ramp.transform.localPosition = new Vector3(0f, -0.35f, 1f);
                ramp.transform.localRotation = Quaternion.Euler(20f, 0f, 0f); Physics.SyncTransforms();
                Check(ahead() == AIObservedBranchGraph.PortState.Unknown, "Downhill ramp became a closed wall.");
                floor.SetActive(true); UnityEngine.Object.DestroyImmediate(ramp);
                var step = Box(root.transform, "Low tread", new Vector3(0f, 0.15f, 1f), new Vector3(4f, 0.3f, 1f));
                Physics.SyncTransforms();
                Check(ahead() == AIObservedBranchGraph.PortState.Unknown, "Stair tread became a closed wall.");
                UnityEngine.Object.DestroyImmediate(step);
                var occluder = Box(root.transform, "Occluder", new Vector3(0f, 1f, 1f), new Vector3(20f, 2f, 0.2f));
                Physics.SyncTransforms(); sensor.ResetEpisode();
                for (int i = 0; i < 20; i++) sensor.SampleNow();
                var hiddenGraph = (AIObservedBranchGraph)typeof(BranchMemorySensorComponent).GetField("graph", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sensor);
                Check(!hiddenGraph.Nodes.Any(n => n.Position.z > root.transform.position.z + 1f), "Floor behind occluding wall leaked into memory.");
                occluder.GetComponent<BoxCollider>().isTrigger = true; Physics.SyncTransforms(); sensor.ResetEpisode();
                for (int i = 0; i < 20; i++) sensor.SampleNow(); sensor.Capture(values);
                Check(values[0] == 1f, "Traversal trigger obstructed observation.");
                UnityEngine.Object.DestroyImmediate(occluder);
                Box(root.transform, "Left", new Vector3(-4.2f, 1f, -2f), new Vector3(0.2f, 2f, 24f));
                Box(root.transform, "Right", new Vector3(4.2f, 1f, -2f), new Vector3(0.2f, 2f, 24f));
                Box(root.transform, "End", new Vector3(0f, 1f, 6.2f), new Vector3(9f, 2f, 0.2f));
                Physics.SyncTransforms();
                sensor.ResetEpisode();
                Action<float> visit = z =>
                {
                    body.enabled = false; obj.transform.localPosition = Vector3.forward * z; body.enabled = true;
                    Physics.SyncTransforms();
                    for (int n = 0; n < 20; n++) sensor.SampleNow();
                };
                visit(0f); sensor.Capture(values);
                Check(values[0] == 1f && values[3] == 0f, "Seen but unvisited dead-end was prematurely complete.");
                for (float z = 1f; z <= 5f; z++) visit(z);
                visit(0f); sensor.Capture(values);
                Debug.Log("BRANCH OBS synthetic front = " + string.Join(",", values.Take(4)) + "; nodes=" + sensor.ObservedNodes);
                Check(values[3] == 1f, "Wide explored dead-end never became a completed observation.");
                Check(values[4 * 4 + 3] == 0f, "Unexplored return direction was completed.");
                Vector3 position = obj.transform.position; Quaternion rotation = obj.transform.rotation;
                for (int i = 0; i < 20; i++) sensor.Capture(values);
                Check(obj.transform.position == position && obj.transform.rotation == rotation, "Observation changed movement.");
                obj.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); sensor.Capture(values);
                Check(values[6 * 4 + 3] == 1f, "Branch directions did not rotate with yaw.");
                Check(values.All(v => !float.IsNaN(v) && v >= -1f && v <= 1f), "Invalid feature range.");
                sensor.ResetEpisode(); Check(sensor.ObservedNodes == 0, "Episode memory was not reset.");
                TransitCheck();
                Debug.Log("AI BRANCH OBS GEOMETRY PASSED: 32 inputs, opt-out, void, ramps, steps, occlusion, trigger, wide dead-end completion, unvisited protection, transit, no action, yaw, reset.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static void ValidateBatch()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use isolated .ai-eval batch project.");
            CommunicatorFactory.Enabled = false;
            EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase2_SmallWFC.unity");
            Validate();
            foreach (string path in new[] { "Assets/Scenes/AI/AI_Phase1_Movement.unity",
                "Assets/Scenes/AI/AI_Phase2_SmallWFC.unity", "Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity" })
            {
                EditorSceneManager.OpenScene(path);
                var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
                var serialized = new SerializedObject(agent);
                Check(!serialized.FindProperty("useCompletedBranchAssist").boolValue, "Hybrid remains enabled.");
                bool expected = !path.Contains("Phase1");
                Check(serialized.FindProperty("useBranchMemorySensor").boolValue == expected, "Branch sensor opt-in mismatch.");
                agent.Initialize();
                var sensors = agent.GetComponents<SensorComponent>().SelectMany(c => c.CreateSensors()).ToList();
                sensors.Add(new VectorSensor(10)); sensors.Sort((a, b) => string.CompareOrdinal(a.GetName(), b.GetName()));
                int[] shapes = sensors.Select(s => s.GetObservationSpec().Shape[0]).ToArray();
                Check(shapes.SequenceEqual(expected ? new[] {24,34,49,10,32,12,16} : new[] {24,34,49,10}),
                    "Sensor order incompatible with migration: " + string.Join(",", shapes));
                Check(agent.GetComponent<AICompletedBranchAssist>() == null, "Legacy actuator was installed.");
                Debug.Log("AI BRANCH OBS SCHEMA PASSED: " + path + " = " + string.Join(",", shapes));
            }
            foreach (int seed in new[] {283003, 314159, 92017}) ValidateWfc(seed);
        }

        private static void ValidateWfc(int seed)
        {
            EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase2_SmallWFC.unity");
            var adapter = UnityEngine.Object.FindAnyObjectByType<AIWFCTrainingAdapter>();
            var parameters = Academy.Instance.EnvironmentParameters;
            object channel = typeof(EnvironmentParameters).GetField("m_Channel", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(parameters);
            var entries = (Dictionary<string, Func<float>>)channel.GetType().GetField("m_Parameters", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(channel);
            entries["terrain_curriculum"] = () => 1f; entries["terrain_driver"] = () => 1f;
            entries["terrain_level"] = () => 8f; entries["graduated_transfer"] = () => 1f;
            entries["terrain_map_seed"] = () => seed;
            Check(adapter.PrepareEpisode(0), "WFC preparation failed.");
            Physics.SyncTransforms();
            var positions = (List<Vector3>)typeof(AIWFCTrainingAdapter).GetField("walkablePositions", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
            var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
            var body = agent.GetComponent<CharacterController>();
            // Test oracle uses map positions ONLY to replay geometry coverage.
            // The sensor itself never receives adapter/generator/area information.
            var sensor = agent.gameObject.AddComponent<BranchMemorySensorComponent>();
            sensor.ResetEpisode();
            float[] values = new float[32]; int completions = 0;
            foreach (Vector3 center in positions)
                foreach (Vector3 offset in new[] {Vector3.zero, Vector3.forward * 1.5f, Vector3.back * 1.5f,
                    Vector3.left * 1.5f, Vector3.right * 1.5f})
                {
                    // walkablePositions already includes the adapter's spawn clearance.
                    body.enabled = false; agent.transform.position = center + offset; body.enabled = true;
                    Physics.SyncTransforms();
                    for (int n = 0; n < 8; n++) sensor.SampleNow();
                    sensor.Capture(values); completions += sensor.CompletedExits;
                }
            // Revisit after the full replay: a branch visited early can only
            // become complete once the neighboring observations arrive later.
            foreach (Vector3 p in positions)
            {
                body.enabled = false; agent.transform.position = p; body.enabled = true;
                Physics.SyncTransforms();
                for (int n = 0; n < 8; n++) sensor.SampleNow();
                sensor.Capture(values); completions += sensor.CompletedExits;
            }
            Debug.Log("AI BRANCH OBS WFC REPLAY: seed=" + adapter.LastGeneratedSeed + "; positions=" + positions.Count
                + "; mapped nodes=" + sensor.ObservedNodes + "; completed observations=" + completions);
            if (completions == 0)
            {
                var graph = (AIObservedBranchGraph)typeof(BranchMemorySensorComponent).GetField("graph", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sensor);
                Debug.Log("BRANCH OBS WFC DIAGNOSTIC: visited=" + graph.Nodes.Count(n => n.Visited)
                    + "; unknown ports=" + graph.Nodes.Sum(n => n.Ports.Count(p => p == AIObservedBranchGraph.PortState.Unknown)));
                foreach (var collider in UnityEngine.Object.FindObjectsByType<MeshCollider>(FindObjectsSortMode.None)
                    .Where(c => c.name.Contains("End")).Take(2))
                {
                    Debug.Log("BRANCH OBS END GEOMETRY: " + collider.name + " bounds=" + collider.bounds);
                    foreach (var n in graph.Nodes.Where(n => Vector3.Distance(n.Position, collider.bounds.center - Vector3.up * collider.bounds.extents.y) < 4f).Take(8))
                    {
                        Debug.Log("BRANCH OBS END NODE " + n.Position + " visited=" + n.Visited + " ports=" + string.Join(",", n.Ports));
                        int unknown = Array.IndexOf(n.Ports, AIObservedBranchGraph.PortState.Unknown);
                        if (unknown < 0) continue;
                        Vector3 dir = Quaternion.Euler(0f, unknown * 45f, 0f) * Vector3.forward;
                        Vector3 step = new Vector3(Mathf.RoundToInt(dir.x), 0f, Mathf.RoundToInt(dir.z)) * 2f;
                        var result = typeof(BranchMemorySensorComponent).GetMethod("Probe", BindingFlags.Instance | BindingFlags.NonPublic)
                            .Invoke(sensor, new object[] {n.Position, n.Position + step, n.Position});
                        int reason = (int)typeof(BranchMemorySensorComponent).GetField("lastProbeFailure", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sensor);
                        Debug.Log("BRANCH OBS END REPRO port=" + unknown + " result=" + result + " reason=" + reason);
                    }
                }
                foreach (var n in graph.Nodes.Where(n => n.Ports.Any(p => p == AIObservedBranchGraph.PortState.Unknown))
                    .OrderByDescending(n => n.Ports.Count(p => p == AIObservedBranchGraph.PortState.Blocked)).Take(5))
                {
                    Debug.Log("BRANCH OBS NODE " + n.Position + " visited=" + n.Visited + " ports=" + string.Join(",", n.Ports));
                    int d = Array.IndexOf(n.Ports, AIObservedBranchGraph.PortState.Unknown);
                    if (d < 0) continue;
                    Vector3 direction = Quaternion.Euler(0f, d * 45f, 0f) * Vector3.forward;
                    foreach (float height in new[] {0.45f, 0.8f, 1.45f})
                        if (Physics.Raycast(n.Position + Vector3.up * height, direction, out RaycastHit h, 4f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                            Debug.Log("BRANCH OBS WITNESS h=" + height + " distance=" + h.distance + " normal=" + h.normal + " object=" + h.collider.name);
                }
            }
            Check(completions > 0, "Real WFC geometry still never produces completed observations.");
            Debug.Log("AI BRANCH OBS WFC PASSED (geometry replay, NOT policy performance evaluation).");
        }

        private static void TransitCheck()
        {
            var g = new AIObservedBranchGraph();
            Func<Vector3Int, AIObservedBranchGraph.Node> closed = p =>
            {
                var n = g.Get(p, p); n.Visited = true;
                for (int d = 0; d < 8; d++) g.Observe(n, d, AIObservedBranchGraph.PortState.Blocked);
                return n;
            };
            var root = closed(Vector3Int.zero); var a = closed(new Vector3Int(0,0,2));
            var b = closed(new Vector3Int(8,0,2)); var c = closed(new Vector3Int(8,0,-2));
            var back = closed(new Vector3Int(0,0,-2));
            g.Observe(root, 0, AIObservedBranchGraph.PortState.Open, a);
            g.Observe(a, 2, AIObservedBranchGraph.PortState.Open, b);
            g.Observe(b, 4, AIObservedBranchGraph.PortState.Open, c);
            g.Observe(c, 6, AIObservedBranchGraph.PortState.Open, back);
            g.Observe(back, 0, AIObservedBranchGraph.PortState.Open, root);
            g.Describe(root, 0, out _, out _, out bool complete);
            Check(!complete, "A transit loop crossing behind the entrance was marked completed.");
        }

        private static GameObject Box(Transform parent, string name, Vector3 p, Vector3 size)
        { var obj = new GameObject(name); obj.transform.SetParent(parent, false); obj.transform.localPosition = p;
            obj.AddComponent<BoxCollider>().size = size; return obj; }
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
