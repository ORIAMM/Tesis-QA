using System;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace TesisQA.AI
{
    /// <summary>Local collider geometry only: no tags, WFC graph or stair coordinates.</summary>
    [DisallowMultipleComponent]
    public sealed class ForwardTerrainSensorComponent : SensorComponent
    {
        public const int ProbeCount = 6;
        public const int ValuesPerProbe = 4;
        public const int ObservationSize = ProbeCount * ValuesPerProbe;
        public const string SensorName = "Explorer Forward Terrain v1";
        public const int DistantObservationSize = 12;
        // Must sort AFTER Z Explorer Branch Memory v1: preserve all 149 old columns.
        public const string DistantSensorName = "ZZ Explorer Distant Terrain v1";
        private const float EyeHeight = 1.6f;
        private const float HeightRange = 2f;
        public const float LegacyFarDistance = 3f;
        public const float ExtendedFarDistance = 6f;
        private const float ExtendedHeightRange = 6f;
        private const float SurfaceClearance = 0.03f;

        // Fixed 24-value layout: near L/C/R, then far L/C/R. Extended mode
        // changes the FAR sampling geometry only, not the input shape/order.
        // Each probe writes [visible ground, relative height, forward slope, right slope].
        private readonly RaycastHit[] hits = new RaycastHit[64];
        private readonly RaycastHit[] surfaceHits = new RaycastHit[64];
        private readonly float[] observations = new float[ObservationSize];
        private readonly float[] distantObservations = new float[DistantObservationSize];
        private CharacterController controller;

        // Set by ExplorationAgent before ML-Agents creates the observation spec.
        // Disabling MonoBehaviour.enabled alone does NOT exclude a SensorComponent.
        public bool ObservationEnabled { get; set; } = true;
        public bool ExtendedRangeEnabled { get; set; }
        public bool DistantObservationEnabled { get; set; }
        // Emitted only while ML-Agents writes observations, never by editor Gizmos.
        public event Action<RaycastHit> VisibleSurfaceSampled;
        public float FarProbeDistance => ExtendedRangeEnabled ? ExtendedFarDistance : LegacyFarDistance;

        private float ProbeDistance(int probe) => probe < 3 ? 1.5f : FarProbeDistance;
        private float ProbeHeightRange(int probe) => probe >= 3 && ExtendedRangeEnabled
            ? ExtendedHeightRange : HeightRange;

        public override ISensor[] CreateSensors()
        {
            if (!ObservationEnabled) return Array.Empty<ISensor>();
            if (DistantObservationEnabled && ExtendedRangeEnabled)
                throw new InvalidOperationException("Multi-range requires the original 1.5/3m profile, not the archived 1.5/6m replacement.");
            return DistantObservationEnabled
                ? new ISensor[] { new TerrainSensor(this), new TerrainSensor(this, true) }
                : new ISensor[] { new TerrainSensor(this) };
        }

        public void Capture(float[] output) => CaptureProfile(output, false);
        public void CaptureDistant(float[] output) => CaptureProfile(output, true);

        private void CaptureProfile(float[] output, bool distant, bool remember = false)
        {
            int size = distant ? DistantObservationSize : ObservationSize;
            if (output == null || output.Length != size)
                throw new ArgumentException($"Terrain observations require {size} values.");
            Array.Clear(output, 0, output.Length);
            if (controller == null) controller = GetComponent<CharacterController>();

            Vector3 feet = transform.position;
            if (controller != null && controller.enabled)
                feet.y = controller.bounds.min.y;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f) forward = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 eye = feet + Vector3.up * EyeHeight;

            for (int probe = 0; probe < (distant ? 3 : ProbeCount); probe++)
            {
                float distance = distant ? ExtendedFarDistance : ProbeDistance(probe);
                float verticalRange = distant ? ExtendedHeightRange : ProbeHeightRange(probe);
                float yaw = ((probe % 3) - 1) * 30f;
                Vector3 direction = Quaternion.AngleAxis(yaw, Vector3.up) * forward;
                Vector3 origin = feet + direction * distance + Vector3.up * verticalRange;
                if (!SampleGround(origin, verticalRange * 2f, eye,
                    distant || probe >= 3 && ExtendedRangeEnabled, out RaycastHit ground))
                    continue;
                if (remember) VisibleSurfaceSampled?.Invoke(ground);

                int offset = probe * ValuesPerProbe;
                output[offset] = 1f;
                // Preserve the trained height scale even in extended mode.
                // Samples beyond +/-2 m saturate, but retain visibility/slope.
                output[offset + 1] = Mathf.Clamp((ground.point.y - feet.y)
                    / (distant ? ExtendedHeightRange : HeightRange), -1f, 1f);
                output[offset + 2] = SignedSlope(ground.normal, forward);
                output[offset + 3] = SignedSlope(ground.normal, right);
            }
        }

        private bool SampleGround(Vector3 origin, float range, Vector3 eye,
            bool searchVisibleLayers, out RaycastHit ground)
        {
            ground = default;
            if (!searchVisibleLayers)
            {
                // Exact legacy geometry for the near probes and old profiles.
                return ClosestSolid(origin, Vector3.down, range, out ground)
                    && ground.normal.y >= 0.2f && VisibleFromEye(eye, ground);
            }

            // A +/-6m column can cross BOTH storeys. The highest top-down hit
            // may be occluded while the floor below it is directly visible.
            // Test eye-to-surface visibility for EVERY candidate; never treat a
            // downward survey as permission to see through walls or ceilings.
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, surfaceHits, range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == surfaceHits.Length) return false;
            float closestVisible = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit candidate = surfaceHits[i];
                Transform hitTransform = candidate.collider.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform)
                    || candidate.normal.y < 0.2f) continue;
                float distance = (candidate.point - eye).sqrMagnitude;
                if (distance >= closestVisible || !VisibleFromEye(eye, candidate)) continue;
                closestVisible = distance;
                ground = candidate;
                found = true;
            }
            return found;
        }

        private bool VisibleFromEye(Vector3 eye, RaycastHit ground)
        {
            Vector3 sight = ground.point + Vector3.up * SurfaceClearance - eye;
            return !ClosestSolid(eye, sight.normalized, sight.magnitude, out _);
        }

        private static float SignedSlope(Vector3 normal, Vector3 direction)
        {
            // Positive means uphill in that local direction; negative means downhill.
            return Mathf.Clamp(Mathf.Atan2(-Vector3.Dot(normal, direction), normal.y)
                / (Mathf.PI * 0.5f), -1f, 1f);
        }

        private bool ClosestSolid(Vector3 origin, Vector3 direction, float distance,
            out RaycastHit closest)
        {
            closest = default;
            int count = Physics.RaycastNonAlloc(origin, direction, hits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            // Fail closed if the fixed buffer saturated: a nearer wall could be omitted.
            if (count == hits.Length) return true;
            float nearest = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                Transform hitTransform = hits[i].collider.transform;
                if (hitTransform == transform || hitTransform.IsChildOf(transform)) continue;
                if (hits[i].distance >= nearest) continue;
                nearest = hits[i].distance;
                closest = hits[i];
                found = true;
            }
            return found;
        }

        private void OnDrawGizmosSelected()
        {
            Capture(observations);
            Vector3 feet = transform.position;
            if (controller != null && controller.enabled) feet.y = controller.bounds.min.y;
            for (int probe = 0; probe < ProbeCount; probe++)
            {
                float distance = ProbeDistance(probe);
                float verticalRange = ProbeHeightRange(probe);
                Vector3 direction = Quaternion.Euler(0f, ((probe % 3) - 1) * 30f, 0f)
                    * Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 point = feet + direction * distance;
                Gizmos.color = observations[probe * ValuesPerProbe] > 0f ? Color.green : Color.gray;
                Gizmos.DrawLine(point + Vector3.up * verticalRange, point - Vector3.up * verticalRange);
            }
            if (!DistantObservationEnabled) return;
            CaptureDistant(distantObservations);
            for (int probe = 0; probe < 3; probe++)
            {
                Vector3 direction = Quaternion.Euler(0f, (probe - 1) * 30f, 0f)
                    * Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
                Vector3 point = feet + direction * ExtendedFarDistance;
                Gizmos.color = distantObservations[probe * ValuesPerProbe] > 0f ? Color.cyan : Color.gray;
                Gizmos.DrawLine(point + Vector3.up * ExtendedHeightRange, point - Vector3.up * ExtendedHeightRange);
            }
        }

        private sealed class TerrainSensor : ISensor
        {
            private readonly ForwardTerrainSensorComponent owner;
            private readonly bool distant;
            public TerrainSensor(ForwardTerrainSensorComponent owner, bool distant = false)
            { this.owner = owner; this.distant = distant; }
            public ObservationSpec GetObservationSpec() => ObservationSpec.Vector(distant ? DistantObservationSize : ObservationSize);
            public int Write(ObservationWriter writer)
            {
                float[] values = distant ? owner.distantObservations : owner.observations;
                owner.CaptureProfile(values, distant, true);
                for (int i = 0; i < values.Length; i++) writer[i] = values[i];
                return values.Length;
            }
            public byte[] GetCompressedObservation() => null;
            public CompressionSpec GetCompressionSpec() => CompressionSpec.Default();
            public string GetName() => distant ? DistantSensorName : SensorName;
            public void Update() { }
            public void Reset() { }
        }
    }
}
