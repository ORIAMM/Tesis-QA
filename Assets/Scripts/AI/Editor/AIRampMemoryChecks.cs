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
    public static class AIRampMemoryChecks
    {
        public static void ValidateBatch()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use isolated .ai-eval batch project.");
            CommunicatorFactory.Enabled = false;
            EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase2_SmallWFC.unity");
            Validate();
            AIForwardTerrainChecks.ValidateMultiRangeBatch();
            Debug.Log("AI RAMP MEMORY FULL PREFLIGHT PASSED: 177 inputs, observed-only episodic landmarks, no training.");
        }

        [MenuItem("Tools/AI/Validate Ramp Memory")]
        public static void Validate()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("Ramp memory temporary tests");
            root.transform.position = new Vector3(11000f, 11000f, 11000f);
            try
            {
                var agent = new GameObject("No policy or motor"); agent.transform.SetParent(root.transform, false);
                agent.layer = 2; // Synthetic support rays must not hit the test body itself.
                var body = agent.AddComponent<CharacterController>();
                body.height = 1.8f; body.radius = 0.35f; body.center = Vector3.up * 0.9f; body.slopeLimit = 50f;
                var memory = agent.AddComponent<RampMemorySensorComponent>(); memory.ResetEpisode();
                var output = new float[16];
                Check(memory.CreateSensors()[0].GetObservationSpec().Shape[0] == 16, "Wrong memory shape.");
                memory.ObservationEnabled = false; Check(memory.CreateSensors().Length == 0, "Opt-out failed.");
                memory.ObservationEnabled = true;
                var floor = Box(root.transform, "Flat floor", new Vector3(0f, -0.1f, 3f), new Vector3(8f, 0.2f, 8f));
                Physics.SyncTransforms(); memory.ObserveVisibleSurface(TopHit(root.transform, 0f, 3f));
                Check(memory.RememberedCount == 0, "Flat floor became a ramp.");
                UnityEngine.Object.DestroyImmediate(floor);
                var ramp = Box(root.transform, "Untagged incline", new Vector3(0f, 3f, 6f), new Vector3(8f, 0.2f, 3f));
                ramp.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                Physics.SyncTransforms(); RaycastHit hit = TopHit(root.transform, 0f, 6f);
                for (int i = 0; i < 100; i++) memory.ObserveVisibleSurface(hit);
                memory.Capture(output);
                Check(memory.RememberedCount == 1 && output[0] == 1f && output[2] > 0.9f
                    && output[6] == 0f && output[7] == 0f, "Seen ramp missing, duplicated or falsely touched/traversed.");
                Vector3 position = agent.transform.position; Quaternion rotation = agent.transform.rotation;
                for (int i = 0; i < 20; i++) memory.Capture(output);
                Check(position == agent.transform.position && rotation == agent.transform.rotation, "Memory moved agent.");
                agent.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); memory.Capture(output);
                Check(output[0] == 1f && output[2] < -0.9f && output[5] < 0f, "Memory lost behind agent or wrong relative yaw/slope.");
                agent.transform.localRotation = Quaternion.identity;
                var wall = Box(root.transform, "Occluding wall", new Vector3(0f, 3f, 3f), new Vector3(20f, 6f, 0.2f));
                Physics.SyncTransforms(); memory.Capture(output);
                Check(output[0] == 1f, "Known landmark was forgotten just because it became occluded.");
                memory.ResetEpisode(); memory.ObserveVisibleSurface(hit);
                Check(memory.RememberedCount == 0, "Unseen ramp leaked through wall.");
                wall.GetComponent<BoxCollider>().isTrigger = true;
                Physics.SyncTransforms(); memory.ObserveVisibleSurface(hit);
                Check(memory.RememberedCount == 1, "Trigger occluded memory input.");
                UnityEngine.Object.DestroyImmediate(wall);
                ramp.GetComponent<BoxCollider>().isTrigger = true; memory.ResetEpisode(); memory.ObserveVisibleSurface(hit);
                Check(memory.RememberedCount == 0, "Trigger surface became a physical ramp.");
                ramp.GetComponent<BoxCollider>().isTrigger = false; Physics.SyncTransforms();

                var terrain = agent.AddComponent<ForwardTerrainSensorComponent>(); terrain.DistantObservationEnabled = true;
                terrain.VisibleSurfaceSampled += memory.ObserveVisibleSurface;
                terrain.Capture(new float[24]); terrain.CaptureDistant(new float[12]);
                Check(memory.RememberedCount == 0, "Gizmos/diagnostic capture modified episodic memory.");
                var distant = terrain.CreateSensors()[1];
                var writer = new ObservationWriter(); var data = new float[12];
                typeof(ObservationWriter).GetMethod("SetTarget", BindingFlags.Instance | BindingFlags.NonPublic,
                    null, new[] { typeof(IList<float>), typeof(ObservationSpec), typeof(int) }, null)
                    .Invoke(writer, new object[] {data, distant.GetObservationSpec(), 0});
                distant.Write(writer);
                Check(memory.RememberedCount == 1, "Actual sensor write did not feed visible ramp memory.");
                terrain.VisibleSurfaceSampled -= memory.ObserveVisibleSurface;
                UnityEngine.Object.DestroyImmediate(ramp);
                memory.ResetEpisode(); memory.Capture(output);
                Check(output.All(v => v == 0f), "Episode reset left old locations.");

                ValidateTravel(root.transform, agent, body, memory);
                memory.Capture(output);
                Check(output.All(v => !float.IsNaN(v) && !float.IsInfinity(v) && v >= -1f && v <= 1f), "Invalid observation.");
                Debug.Log("AI RAMP MEMORY GEOMETRY PASSED: sight, persistence, reset, yaw, duplicate suppression, wall/trigger, actual sensor event, up/down supported travel, no teleport/short-return false confirmation.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void ValidateTravel(Transform parent, GameObject agent, CharacterController body,
            RampMemorySensorComponent memory)
        {
            var route = new GameObject("Continuous supported route"); route.transform.SetParent(parent, false);
            route.transform.localPosition = Vector3.right * 30f;
            float rise = Mathf.Tan(20f * Mathf.Deg2Rad) * 6f;
            Box(route.transform, "Lower landing", new Vector3(0f, -0.1f, -2f), new Vector3(4f, 0.2f, 4f));
            var ramp = Box(route.transform, "Walked ramp", new Vector3(0f, rise * 0.5f - 0.1f / Mathf.Cos(20f * Mathf.Deg2Rad), 3f),
                new Vector3(4f, 0.2f, 6f / Mathf.Cos(20f * Mathf.Deg2Rad)));
            ramp.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            Box(route.transform, "Upper landing", new Vector3(0f, rise - 0.1f, 8f), new Vector3(4f, 0.2f, 4f));
            Physics.SyncTransforms();
            Action<float> step = z =>
            {
                RaycastHit support = TopHit(route.transform, 0f, z);
                body.enabled = false; agent.transform.position = support.point + Vector3.up * 0.01f;
                body.enabled = true; Physics.SyncTransforms(); memory.RecordMotion(0.02f);
            };
            memory.ResetEpisode();
            for (float z = -0.5f; z <= 6.6f; z += 0.1f) step(z);
            Check(memory.TouchedCount == 1 && memory.TraversedCount == 1,
                "Supported lower-to-upper travel not confirmed: touched=" + memory.TouchedCount + "; traversed=" + memory.TraversedCount);
            memory.ResetEpisode();
            for (float z = 6.5f; z >= -0.6f; z -= 0.1f) step(z);
            Check(memory.TraversedCount == 1, "Supported upper-to-lower travel not confirmed.");
            memory.ResetEpisode(); step(-0.5f); step(6.5f);
            Check(memory.TraversedCount == 0, "Teleport between floors falsely confirmed traversal.");
            memory.ResetEpisode();
            for (float z = -0.5f; z <= 2f; z += 0.1f) step(z);
            for (float z = 2f; z >= -0.5f; z -= 0.1f) step(z);
            Check(memory.TouchedCount > 0 && memory.TraversedCount == 0, "Short ramp touch/return became traversal.");
        }

        private static RaycastHit TopHit(Transform root, float x, float z)
        {
            if (!Physics.Raycast(root.position + new Vector3(x, 10f, z), Vector3.down, out RaycastHit hit,
                20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Missing synthetic support.");
            return hit;
        }
        private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var p = new GameObject(name); p.transform.SetParent(parent, false); p.transform.localPosition = position;
            p.AddComponent<BoxCollider>().size = size; return p;
        }
        private static void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
