using System;
using System.Linq;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TesisQA.AI.Editor
{
    public static class AIForwardTerrainChecks
    {
        [MenuItem("Tools/AI/Validate Forward Terrain Sensor")]
        public static void Validate()
        {
            // Temporary scene + a distant test area; never edit the user's map.
            Scene previous = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(previous.path))
                throw new InvalidOperationException("Open a saved training scene before running terrain checks.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            GameObject root = new GameObject("Terrain test objects");
            root.transform.position = new Vector3(7500f, 7500f, 7500f);
            try
            {
                GameObject agent = new GameObject("Terrain probe agent");
                agent.transform.SetParent(root.transform, false);
                var controller = agent.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.35f;
                controller.center = Vector3.up * 0.9f;
                var sensor = agent.AddComponent<ForwardTerrainSensorComponent>();
                var values = new float[ForwardTerrainSensorComponent.ObservationSize];
                if (sensor.CreateSensors()[0].GetObservationSpec().Shape[0] != 24)
                    throw new InvalidOperationException("Terrain schema is not 24 values.");
                sensor.ObservationEnabled = false;
                if (sensor.CreateSensors().Length != 0)
                    throw new InvalidOperationException("Legacy model opt-out failed.");
                sensor.ObservationEnabled = true;
                sensor.Capture(values);
                Expect(values[4] == 0f, "Void must not be reported as ground.");

                GameObject surface = Box(root.transform, "Floor", new Vector3(0f, -0.1f, 2f),
                    new Vector3(12f, 0.2f, 12f));
                Physics.SyncTransforms();
                sensor.Capture(values);
                Expect(values[4] == 1f && Mathf.Abs(values[5]) < 0.01f
                    && Mathf.Abs(values[6]) < 0.01f, "Flat floor profile is incorrect.");

                // Rising ramp is untagged; the same collider is measured in both directions.
                surface.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
                surface.transform.localPosition = new Vector3(0f, 0.5f, 2f);
                Physics.SyncTransforms();
                sensor.Capture(values);
                Expect(values[4] == 1f && values[6] > 0.15f && values[5] > 0f,
                    "Uphill ramp was not sensed before stepping onto it.");
                surface.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
                surface.transform.localPosition = new Vector3(0f, -0.8f, 2f);
                Physics.SyncTransforms();
                sensor.Capture(values);
                Expect(values[4] == 1f && values[6] < -0.15f,
                    "Downhill ramp sign is incorrect.");

                surface.transform.localRotation = Quaternion.identity;
                surface.transform.localPosition = new Vector3(0f, -0.1f, 2f);
                GameObject wall = Box(root.transform, "Occluder", new Vector3(0f, 1f, 0.75f),
                    new Vector3(10f, 2f, 0.2f));
                Physics.SyncTransforms();
                sensor.Capture(values);
                Expect(values[4] == 0f && values[16] == 0f,
                    "Probe leaked ground information through a wall.");
                wall.GetComponent<BoxCollider>().isTrigger = true;
                Physics.SyncTransforms();
                sensor.Capture(values);
                Expect(values[4] == 1f, "Traversal trigger must not occlude terrain.");
                UnityEngine.Object.DestroyImmediate(wall);
                UnityEngine.Object.DestroyImmediate(surface);

                // Stair treads are flat: relative height, not a tag/normal label, detects the rise.
                Box(root.transform, "Near tread", new Vector3(0f, 0.15f, 1.5f),
                    new Vector3(1f, 0.3f, 1f));
                Box(root.transform, "Far tread", new Vector3(0f, 0.4f, 3f),
                    new Vector3(1f, 0.8f, 1f));
                Physics.SyncTransforms();
                sensor.Capture(values);
                Expect(values[4] == 1f && values[16] == 1f && values[17] > values[5]
                    && Mathf.Abs(values[6]) < 0.01f,
                    "Stepped geometry must retain height information even on horizontal treads.");
                agent.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                Physics.SyncTransforms();
                sensor.Capture(values);
                Expect(values[4] == 0f, "Probes did not rotate with agent yaw.");
                foreach (float value in values)
                    Expect(!float.IsNaN(value) && value >= -1f && value <= 1f,
                        "Terrain observation was not finite/normalized.");
                ValidateExtendedRange(root.transform, agent, sensor, values);
                ValidateMultiRange(root.transform, agent, sensor, values);
                Debug.Log("AI TERRAIN CHECKS passed: schema, opt-out, void, flat, uphill, downhill, wall, trigger, steps, yaw.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        // Editor integration check; no trainer/model and no source scene saves.
        public static void ValidateMultiRangeBatch()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use the isolated .ai-eval project for batch checks.");
            ValidateScenes();
            AIBranchObservationChecks.ValidateBatch();
            Debug.Log("AI TERRAIN FULL PREFLIGHT PASSED: current schema and geometry; no training started.");
        }

        public static void ValidateScenes()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("ValidateScenes is reserved for the isolated batch project.");
            EditorSceneManager.OpenScene("Assets/Scenes/AI/AI_Phase1_Movement.unity", OpenSceneMode.Single);
            Validate();
            CommunicatorFactory.Enabled = false;
            foreach (string path in new[] { "Assets/Scenes/AI/AI_Phase1_Movement.unity",
                "Assets/Scenes/AI/AI_Phase2_SmallWFC.unity", "Assets/Scenes/AI/AI_Phase3_ComplexRouting.unity" })
            {
                Scene opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                var agent = UnityEngine.Object.FindAnyObjectByType<ExplorationAgent>();
                var serialized = new SerializedObject(agent);
                Expect(serialized.FindProperty("useTerrainProfileSensor").boolValue,
                    "Terrain is disabled in " + path);
                Expect(agent.GetComponent<BehaviorParameters>().BrainParameters.VectorObservationSize == 10,
                    "Base vector must remain 10; terrain is a separate sensor.");
                // Exercise Initialize's component installation without running/altering the map.
                agent.Initialize();
                var terrain = agent.GetComponent<ForwardTerrainSensorComponent>();
                bool multiRange = !path.Contains("Phase1");
                Expect(terrain != null && terrain.CreateSensors().Length == (multiRange ? 2 : 1),
                    "Terrain sensor not installed in " + path);
                Expect(!serialized.FindProperty("useLongRangeTerrainProfile").boolValue
                    && !terrain.ExtendedRangeEnabled
                    && serialized.FindProperty("useDistantTerrainSensor").boolValue == multiRange
                    && terrain.DistantObservationEnabled == multiRange
                    && Mathf.Approximately(terrain.FarProbeDistance, 3f),
                    "Wrong terrain range profile in " + path);
                var values = new float[24];
                terrain.Capture(values);
                var sensors = agent.GetComponents<Unity.MLAgents.Sensors.SensorComponent>()
                    .SelectMany(c => c.CreateSensors()).ToList();
                sensors.Add(new Unity.MLAgents.Sensors.VectorSensor(10));
                sensors.Sort((a, b) => string.CompareOrdinal(a.GetName(), b.GetName()));
                int[] shapes = sensors.Select(s => s.GetObservationSpec().Shape[0]).ToArray();
                Expect(shapes.SequenceEqual(multiRange ? new[] {24,34,49,10,32,12,16} : new[] {24,34,49,10}),
                    "Multi-range changed old input order: " + string.Join(",", shapes));
                if (multiRange) Expect(sensors[sensors.Count - 2].GetName() == ForwardTerrainSensorComponent.DistantSensorName
                    && sensors.Last().GetName() == RampMemorySensorComponent.SensorName,
                    "Ramp memory must append after all 161 existing values.");
                Debug.Log("AI TERRAIN SCENE CHECK passed: " + opened.path);
            }
        }

        private static void ValidateMultiRange(Transform parent, GameObject agent,
            ForwardTerrainSensorComponent sensor, float[] legacy)
        {
            agent.transform.localPosition = new Vector3(60f, 0f, 0f);
            agent.transform.localRotation = Quaternion.identity;
            sensor.ExtendedRangeEnabled = false;
            sensor.DistantObservationEnabled = false;
            var region = new GameObject("Simultaneous multi-range tests");
            region.transform.SetParent(parent, false);
            region.transform.localPosition = new Vector3(60f, 0f, 0f);
            var near = Box(region.transform, "Near 1.5m", new Vector3(0f, -0.1f, 1.5f), new Vector3(4f, 0.2f, 1f));
            var middle = Box(region.transform, "Middle 3m", new Vector3(0f, 0.2f, 3f), new Vector3(5f, 0.2f, 1f));
            var far = Box(region.transform, "Far 6m", new Vector3(0f, -0.1f, 6f), new Vector3(8f, 0.2f, 3f));
            Physics.SyncTransforms(); sensor.Capture(legacy);
            float[] before = (float[])legacy.Clone();
            sensor.DistantObservationEnabled = true;
            var farValues = new float[12];
            sensor.Capture(legacy); sensor.CaptureDistant(farValues);
            Expect(legacy.SequenceEqual(before), "Adding distant observations changed one of the 24 old values.");
            Expect(legacy[4] == 1f && legacy[16] == 1f && farValues[4] == 1f,
                "Near, middle and far must be observed simultaneously.");
            var created = sensor.CreateSensors();
            Expect(created.Length == 2 && created[0].GetObservationSpec().Shape[0] == 24
                && created[1].GetObservationSpec().Shape[0] == 12
                && created[1].GetName() == ForwardTerrainSensorComponent.DistantSensorName,
                "Multi-range must append a separate 12-input sensor.");
            sensor.ObservationEnabled = false;
            Expect(sensor.CreateSensors().Length == 0, "Disabled terrain must disable both bands.");
            sensor.ObservationEnabled = true;
            sensor.ExtendedRangeEnabled = true;
            bool rejected = false;
            try { sensor.CreateSensors(); } catch (InvalidOperationException) { rejected = true; }
            Expect(rejected, "Conflicting legacy replacement and new multi-range profiles were accepted.");
            sensor.ExtendedRangeEnabled = false;

            var ceiling = Box(region.transform, "Upper floor", new Vector3(0f, 3.9f, 6f), new Vector3(8f, 0.2f, 3f));
            Physics.SyncTransforms(); sensor.CaptureDistant(farValues);
            Expect(farValues[4] == 1f && Mathf.Abs(farValues[5]) < 0.01f,
                "Distant sensor confused an occluded upper floor with the visible lower floor.");
            UnityEngine.Object.DestroyImmediate(ceiling);
            var wall = Box(region.transform, "Wall at 4m", new Vector3(0f, 2f, 4f), new Vector3(20f, 4f, 0.2f));
            Physics.SyncTransforms(); sensor.Capture(legacy); sensor.CaptureDistant(farValues);
            Expect(legacy[4] == 1f && legacy[16] == 1f && farValues[4] == 0f,
                "Distant ray leaked through wall, or wall invalidated unobstructed near/middle probes.");
            wall.GetComponent<BoxCollider>().isTrigger = true;
            Physics.SyncTransforms(); sensor.CaptureDistant(farValues);
            Expect(farValues[4] == 1f, "Trigger blocks distant samples.");
            UnityEngine.Object.DestroyImmediate(wall);
            UnityEngine.Object.DestroyImmediate(near); UnityEngine.Object.DestroyImmediate(middle);
            far.transform.localPosition = new Vector3(0f, 3f, 6f);
            far.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            Physics.SyncTransforms(); sensor.CaptureDistant(farValues);
            Expect(farValues[4] == 1f && farValues[5] > 0.4f && farValues[5] < 0.6f && farValues[6] > 0.15f,
                "6m uphill height/slope incorrect; new height scale must be +/-6m.");
            far.transform.localPosition = new Vector3(0f, -3f, 6f);
            far.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            Physics.SyncTransforms(); sensor.CaptureDistant(farValues);
            Expect(farValues[4] == 1f && farValues[5] < -0.4f && farValues[6] < -0.15f,
                "6m downhill height/slope incorrect.");
            foreach (float value in farValues)
                Expect(!float.IsNaN(value) && !float.IsInfinity(value) && value >= -1f && value <= 1f,
                    "New distant observation outside its normalized range.");
            agent.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Physics.SyncTransforms(); sensor.CaptureDistant(farValues);
            Expect(farValues[4] == 0f, "Distant band did not rotate with agent.");
            agent.transform.localRotation = Quaternion.identity;
            far.SetActive(false); Physics.SyncTransforms(); sensor.CaptureDistant(farValues);
            Expect(farValues.All(v => v == 0f), "Distant void must be unknown.");
            Debug.Log("AI MULTI-RANGE CHECKS PASSED: simultaneous 1.5/3/6m, exact old 24, appended 12, conflict guard, wall, trigger, stacked floors, up/down, yaw, void.");
        }

        private static void ValidateExtendedRange(Transform parent, GameObject agent,
            ForwardTerrainSensorComponent sensor, float[] values)
        {
            // Keep the original legacy tests/geometry intact, isolate these probes laterally.
            agent.transform.localPosition = new Vector3(30f, 0f, 0f);
            agent.transform.localRotation = Quaternion.identity;
            var region = new GameObject("Extended range tests");
            region.transform.SetParent(parent, false);
            region.transform.localPosition = new Vector3(30f, 0f, 0f);
            var near = Box(region.transform, "Near floor", new Vector3(0f, -0.1f, 1.5f),
                new Vector3(4f, 0.2f, 2f));
            var far = Box(region.transform, "Distant untagged surface", new Vector3(0f, -0.1f, 6f),
                new Vector3(8f, 0.2f, 3f));
            Physics.SyncTransforms();
            sensor.ExtendedRangeEnabled = false;
            sensor.Capture(values);
            float[] oldNear = new float[12]; Array.Copy(values, oldNear, 12);
            Expect(values[4] == 1f && values[16] == 0f, "Legacy samples should remain at 1.5/3m.");
            sensor.ExtendedRangeEnabled = true;
            sensor.Capture(values);
            for (int i = 0; i < 12; i++)
                Expect(values[i] == oldNear[i], "Extended range changed a near probe.");
            Expect(sensor.CreateSensors()[0].GetObservationSpec().Shape[0] == 24
                && values[16] == 1f && Mathf.Abs(values[18]) < 0.01f,
                "6m flat surface missing or schema changed.");

            var overhead = Box(region.transform, "Overlapping upper floor", new Vector3(0f, 3.9f, 6f),
                new Vector3(8f, 0.2f, 3f));
            Physics.SyncTransforms(); sensor.Capture(values);
            Expect(values[16] == 1f && Mathf.Abs(values[17]) < 0.01f,
                "Occluded upper floor hid a directly visible lower floor or leaked its height.");
            UnityEngine.Object.DestroyImmediate(overhead);

            UnityEngine.Object.DestroyImmediate(near);
            far.transform.localRotation = Quaternion.Euler(-20f, 0f, 0f);
            far.transform.localPosition = new Vector3(0f, 3f, 6f);
            Physics.SyncTransforms(); sensor.Capture(values);
            Expect(values[16] == 1f && values[17] == 1f && values[18] > 0.15f,
                "Visible 6m uphill beyond the old +2m height range was not detected.");
            var wall = Box(region.transform, "Far occluder", new Vector3(0f, 2f, 3f),
                new Vector3(20f, 4f, 0.2f));
            Physics.SyncTransforms(); sensor.Capture(values);
            Expect(values[16] == 0f, "Extended uphill leaked through a wall.");
            wall.GetComponent<BoxCollider>().isTrigger = true;
            Physics.SyncTransforms(); sensor.Capture(values);
            Expect(values[16] == 1f, "Trigger obscured the distant ramp.");
            UnityEngine.Object.DestroyImmediate(wall);

            far.transform.localRotation = Quaternion.Euler(20f, 0f, 0f);
            far.transform.localPosition = new Vector3(0f, -3f, 6f);
            Physics.SyncTransforms(); sensor.Capture(values);
            Expect(values[16] == 1f && values[17] == -1f && values[18] < -0.15f,
                "Visible 6m downhill beyond the old -2m range was not detected.");
            foreach (float value in values)
                Expect(!float.IsNaN(value) && !float.IsInfinity(value) && value >= -1f && value <= 1f,
                    "Extended profile has invalid values.");
            far.transform.localPosition = new Vector3(0f, 7f, 6f);
            Physics.SyncTransforms(); sensor.Capture(values);
            Expect(values[16] == 0f, "Surface outside +/-6m range was accepted.");
            far.transform.localPosition = new Vector3(0f, 3f, 6f);
            agent.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Physics.SyncTransforms(); sensor.Capture(values);
            Expect(values[16] == 0f, "Distant probe did not rotate with agent yaw.");
            agent.transform.localRotation = Quaternion.identity;
            far.SetActive(false);
            Physics.SyncTransforms(); sensor.Capture(values);
            Expect(values[16] == 0f, "Distant void became visible ground.");
            Debug.Log("AI EXTENDED TERRAIN CHECKS passed: 1.5m unchanged, 6m flat/up/down, +/-6m range, overlapping floors, wall/trigger, yaw, void, 24 inputs.");
        }

        private static GameObject Box(Transform parent, string name, Vector3 position, Vector3 size)
        {
            var box = new GameObject(name);
            box.transform.SetParent(parent, false);
            box.transform.localPosition = position;
            box.AddComponent<BoxCollider>().size = size;
            return box;
        }

        private static void Expect(bool passed, string message)
        {
            if (!passed) throw new InvalidOperationException(message);
        }
    }
}
