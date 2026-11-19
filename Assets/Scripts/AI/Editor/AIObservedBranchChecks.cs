using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TesisQA.AI.Editor
{
    public static class AIObservedBranchChecks
    {
        [MenuItem("Tools/AI/Validate Completed Branch Memory")]
        public static void Validate()
        {
            Check(Mathf.Approximately(AICompletedBranchAssist.ScanIntervalSeconds, 0.1f),
                "Branch scan interval must remain 0.1 simulated seconds for speed 6.");
            GraphChecks();
            GeometryChecks();
            Debug.Log("AI BRANCH CHECKS PASSED: leaf, nested, unknown, unvisited, transit cycle, reset, floors, wall, ramp, void, triggers, redirect, sole return.");
        }

        private static AIObservedBranchGraph.Node Closed(AIObservedBranchGraph graph, int x, int y = 0)
        {
            var n = graph.Get(new Vector3Int(x, y, 0), new Vector3(x, y, 0));
            n.Visited = true;
            for (int d = 0; d < 8; d++) graph.Observe(n, d, AIObservedBranchGraph.PortState.Blocked);
            return n;
        }

        private static void GraphChecks()
        {
            var g = new AIObservedBranchGraph();
            var root = Closed(g, 0);
            var leaf = Closed(g, 1);
            g.Observe(root, 0, AIObservedBranchGraph.PortState.Open, leaf);
            Check(g.IsCompletedBranch(root, 0), "Known visited leaf was not completed.");
            leaf.Visited = false;
            Check(!g.IsCompletedBranch(root, 0), "Observed but unvisited ground was pruned.");
            leaf.Visited = true;
            g.Observe(leaf, 1, AIObservedBranchGraph.PortState.Unknown);
            Check(!g.IsCompletedBranch(root, 0), "Unknown/stair exit was treated as blocked.");
            var end = Closed(g, 2);
            g.Observe(leaf, 1, AIObservedBranchGraph.PortState.Open, end);
            Check(g.IsCompletedBranch(root, 0), "Nested leaf completion did not propagate.");
            // The same component now connects back to a DIFFERENT junction exit.
            g.Observe(root, 2, AIObservedBranchGraph.PortState.Open, end);
            Check(!g.IsCompletedBranch(root, 0), "Transit cycle was falsely removed.");
            Check(!g.IsCompletedBranch(root, 2), "Reverse transit cycle was falsely removed.");
            var upper = g.Get(new Vector3Int(1, 16, 0), new Vector3(1f, 4f, 0f));
            Check(!ReferenceEquals(upper, leaf) && !upper.Visited, "Floor memories were merged.");
            g.Observe(root, 0, AIObservedBranchGraph.PortState.Unknown);
            Check(leaf.Neighbors[4] == null && leaf.Ports[4] == AIObservedBranchGraph.PortState.Unknown,
                "Revoked evidence left a stale reciprocal link.");
            g.Clear(); Check(g.Count == 0, "Episode reset retained graph state.");
        }

        private static void GeometryChecks()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Branch checks temporary geometry");
            root.transform.position = new Vector3(9000f, 9000f, 9000f);
            try
            {
                var agent = new GameObject("Branch probe");
                agent.transform.SetParent(root.transform, false);
                var cc = agent.AddComponent<CharacterController>();
                cc.height = 1.8f; cc.radius = 0.35f; cc.center = Vector3.up * 0.9f;
                var assist = agent.AddComponent<AICompletedBranchAssist>();
                assist.ResetEpisode(agent.transform.position);
                var passage = typeof(AICompletedBranchAssist).GetMethod("Passage", BindingFlags.NonPublic | BindingFlags.Instance);
                var scan = typeof(AICompletedBranchAssist).GetMethod("Scan", BindingFlags.NonPublic | BindingFlags.Instance);
                Func<Vector3, Vector3, AIObservedBranchGraph.PortState> probe = (a, b) =>
                    (AIObservedBranchGraph.PortState)passage.Invoke(assist, new object[] { root.transform.position + a, root.transform.position + b });
                Check(probe(Vector3.zero, Vector3.forward * 2f) == AIObservedBranchGraph.PortState.Unknown,
                    "Void must remain unknown, not a dead-end wall.");
                var floor = Box(root.transform, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(20f, 0.2f, 20f));
                Physics.SyncTransforms();
                Check(probe(Vector3.zero, Vector3.forward * 2f) == AIObservedBranchGraph.PortState.Open, "Flat passage failed.");
                var wall = Box(root.transform, "Wall", new Vector3(0f, 1f, 1f), new Vector3(6f, 2f, 0.2f));
                Physics.SyncTransforms();
                Check(probe(Vector3.zero, Vector3.forward * 2f) == AIObservedBranchGraph.PortState.Blocked, "Tall wall was not confirmed.");
                wall.GetComponent<BoxCollider>().isTrigger = true;
                Physics.SyncTransforms();
                Check(probe(Vector3.zero, Vector3.forward * 2f) == AIObservedBranchGraph.PortState.Open, "Traversal trigger blocked graph.");
                UnityEngine.Object.DestroyImmediate(wall);
                floor.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                Physics.SyncTransforms();
                Check(probe(Vector3.zero, Vector3.forward * 2f) == AIObservedBranchGraph.PortState.Unknown, "Uphill ramp was pruned.");
                floor.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
                Physics.SyncTransforms();
                Check(probe(Vector3.zero, Vector3.forward * 2f) == AIObservedBranchGraph.PortState.Unknown, "Downhill ramp was pruned.");
                floor.transform.localRotation = Quaternion.identity;
                Box(root.transform, "Left", new Vector3(-1.2f, 1f, 0f), new Vector3(0.2f, 2f, 16f));
                Box(root.transform, "Right", new Vector3(1.2f, 1f, 0f), new Vector3(0.2f, 2f, 16f));
                Box(root.transform, "End", new Vector3(0f, 1f, 3.2f), new Vector3(4f, 2f, 0.2f));
                Physics.SyncTransforms();
                assist.ResetEpisode(root.transform.position);
                Action<float> visit = z =>
                {
                    cc.enabled = false; agent.transform.localPosition = Vector3.forward * z; cc.enabled = true;
                    Physics.SyncTransforms();
                    scan.Invoke(assist, new object[] { root.transform.position + Vector3.forward * z });
                };
                visit(0f); visit(2f); visit(0f);
                float forward = 1f, turn = 0f;
                assist.Filter(ref forward, ref turn);
                Check(assist.RedirectCount == 1 && forward == 0f, "Confirmed leaf re-entry was not redirected toward unfinished return.");
                // From inside the dead-end, the only way OUT must remain available.
                assist.ResetEpisode(root.transform.position);
                visit(0f); visit(2f);
                agent.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                forward = 1f; turn = 0f; assist.Filter(ref forward, ref turn);
                Check(assist.RedirectCount == 0 && forward == 1f, "The sole backtracking route was forbidden.");
                assist.ResetEpisode(root.transform.position);
                Check(assist.ObservedNodes == 0 && assist.RedirectCount == 0, "Reset retained episode assistance.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static void ValidateBatch()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use isolated .ai-eval batch project.");
            EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase2_SmallWFC.unity");
            Validate();
            foreach (string path in new[] { "Assets/Scenes/AI/AI_Phase1_Movement.unity",
                "Assets/Scenes/AI/AI_Phase2_SmallWFC.unity", "Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity" })
            {
                EditorSceneManager.OpenScene(path);
                var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
                Check(!new SerializedObject(agent).FindProperty("useCompletedBranchAssist").boolValue,
                    "Scene legacy assistance must be OFF: " + path);
                Check(Mathf.Approximately(agent.GetComponent<AICharacterMotor>().MoveSpeed,
                    path.Contains("Phase1") ? 4f : 6f), "Scene movement speed mismatch: " + path);
            }
            Debug.Log("AI SPEED CHECKS PASSED: Phase1 baseline 4, Phase2/3 speed 6, branch scan 0.1s.");
        }

        private static GameObject Box(Transform root, string name, Vector3 at, Vector3 size)
        {
            var box = new GameObject(name); box.transform.SetParent(root, false);
            box.transform.localPosition = at; box.AddComponent<BoxCollider>().size = size;
            return box;
        }
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
