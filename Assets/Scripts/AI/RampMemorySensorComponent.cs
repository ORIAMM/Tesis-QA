using System;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace TesisQA.AI
{
    /// <summary>Episode-local memory of observed inclined colliders. No tags,
    /// WFC/area access, planner, reward, actuator or full-map floor coverage.</summary>
    [DisallowMultipleComponent]
    public sealed class RampMemorySensorComponent : SensorComponent
    {
        public const string SensorName = "ZZZ Explorer Ramp Memory v1";
        public const int ObservationSize = 16;
        private const int Capacity = 32;
        private const float MinSlope = 8f;
        private const float MaxSlope = 50f;
        private readonly List<Landmark> landmarks = new List<Landmark>();
        private readonly RaycastHit[] hits = new RaycastHit[64];
        private readonly float[] values = new float[ObservationSize];
        private CharacterController body;
        private Landmark active;
        private Vector3 previousFeet, lastFlat, entry, lastRampFeet;
        private bool hasPrevious, hasFlat;
        public bool ObservationEnabled { get; set; } = true;
        public int RememberedCount => landmarks.Count;
        public int TouchedCount { get { int n = 0; foreach (var p in landmarks) if (p.Touched) n++; return n; } }
        public int TraversedCount { get { int n = 0; foreach (var p in landmarks) if (p.Traversed) n++; return n; } }

        private sealed class Landmark
        {
            public Collider Surface;
            public Vector3 Anchor, Normal, Start, End;
            public readonly List<Vector3> Samples = new List<Vector3>();
            public bool Touched, Traversed;
        }

        public override ISensor[] CreateSensors() => ObservationEnabled
            ? new ISensor[] { new MemorySensor(this) } : Array.Empty<ISensor>();

        public void ResetEpisode()
        {
            landmarks.Clear(); active = null; hasPrevious = hasFlat = false;
            Array.Clear(values, 0, values.Length);
            body = GetComponent<CharacterController>();
        }

        private Vector3 Feet
        {
            get
            {
                if (body == null) body = GetComponent<CharacterController>();
                return body != null && body.enabled
                    ? new Vector3(transform.position.x, body.bounds.min.y, transform.position.z)
                    : transform.position;
            }
        }

        private bool Self(Collider c) => c != null && (c.transform == transform || c.transform.IsChildOf(transform));

        private bool Closest(Vector3 start, Vector3 direction, float range, out RaycastHit hit)
        {
            hit = default;
            int count = Physics.RaycastNonAlloc(start, direction, hits, range,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            float closest = float.PositiveInfinity;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (Self(hits[i].collider) || hits[i].distance >= closest) continue;
                closest = hits[i].distance; hit = hits[i]; found = true;
            }
            return found;
        }

        // Only called with an actual terrain ray hit or actual supported contact.
        // Recheck visibility defensively; data behind a wall cannot become memory.
        public void ObserveVisibleSurface(RaycastHit sample)
        {
            if (!ObservationEnabled || sample.collider == null || sample.collider.isTrigger || Self(sample.collider)) return;
            Vector3 eye = Feet + Vector3.up * 1.6f;
            float angle = Vector3.Angle(sample.normal, Vector3.up);
            float limit = body != null ? Mathf.Min(MaxSlope, body.slopeLimit) : MaxSlope;
            if (angle < MinSlope || angle > limit) return;
            Vector3 sight = sample.point + Vector3.up * 0.03f - eye;
            if (Closest(eye, sight.normalized, sight.magnitude, out _)) return;
            Remember(sample);
        }

        private Landmark Remember(RaycastHit sample)
        {
            Landmark known = null;
            foreach (var p in landmarks)
            {
                // Never merge different physical colliders just because two stairs
                // happen to be nearby. A combined mesh is split spatially as well.
                if (p.Surface == sample.collider && Vector3.Distance(p.Anchor, sample.point) <= 8f
                    && Vector3.Dot(p.Normal, sample.normal) >= 0.8f) { known = p; break; }
            }
            if (known == null)
            {
                if (landmarks.Count >= Capacity) return null;
                known = new Landmark { Surface = sample.collider, Anchor = sample.point, Normal = sample.normal };
                landmarks.Add(known);
            }
            if (known.Samples.Count < 16)
            {
                bool duplicate = false;
                foreach (Vector3 p in known.Samples) if ((p - sample.point).sqrMagnitude < 0.25f) { duplicate = true; break; }
                if (!duplicate) known.Samples.Add(sample.point);
            }
            return known;
        }

        // Called after the motor, with real ground observations. Merely seeing
        // an incline does NOT prove traversal or reveal either unseen landing.
        public void RecordMotion(float deltaTime)
        {
            if (!ObservationEnabled || deltaTime <= 0f) return;
            Vector3 feet = Feet;
            if (body != null && !body.enabled) { active = null; hasPrevious = hasFlat = false; return; }
            bool continuous = hasPrevious && Vector3.Distance(previousFeet, feet) <= 12f * deltaTime + 0.25f;
            previousFeet = feet; hasPrevious = true;
            if (!continuous) { active = null; hasFlat = false; }
            if (!Closest(feet + Vector3.up * 0.3f, Vector3.down, 0.6f, out RaycastHit ground)
                || Mathf.Abs(feet.y - ground.point.y) > 0.22f)
            { active = null; hasFlat = false; return; }
            float angle = Vector3.Angle(ground.normal, Vector3.up);
            if (angle >= MinSlope && angle <= Mathf.Min(MaxSlope, body != null ? body.slopeLimit : MaxSlope))
            {
                Landmark p = Remember(ground); // Underfoot surface is directly sensed, not an oracle.
                if (p == null) return;
                p.Touched = true;
                if (active != p)
                {
                    active = p;
                    entry = hasFlat && Vector3.Distance(lastFlat, feet) <= 3f ? lastFlat : feet;
                    // Spawn halfway onto a ramp must not prove a whole landing-to-landing traversal.
                    if (!hasFlat) active = null;
                }
                lastRampFeet = feet;
                return;
            }
            if (angle < MinSlope)
            {
                if (active != null && continuous && Vector3.Distance(feet, lastRampFeet) <= 2f
                    && Mathf.Abs(ground.point.y - entry.y) >= 2f
                    && Vector3.ProjectOnPlane(ground.point - entry, Vector3.up).magnitude >= 1f)
                {
                    active.Traversed = true; active.Start = entry; active.End = ground.point;
                }
                active = null; lastFlat = ground.point; hasFlat = true;
            }
            else { active = null; hasFlat = false; }
        }

        private static Vector3 NearestPoint(Landmark p, Vector3 feet)
        {
            Vector3 best = p.Traversed ? p.Start : p.Anchor;
            float distance = (best - feet).sqrMagnitude;
            if (p.Traversed)
                return (p.End - feet).sqrMagnitude < distance ? p.End : best;
            foreach (Vector3 q in p.Samples)
                if ((q - feet).sqrMagnitude < distance) { best = q; distance = (q - feet).sqrMagnitude; }
            return best;
        }

        public void Capture(float[] output)
        {
            if (output == null || output.Length != ObservationSize) throw new ArgumentException("Ramp memory requires 16 values.");
            Array.Clear(output, 0, output.Length);
            if (!ObservationEnabled) return;
            Vector3 feet = Feet;
            Landmark first = null, second = null;
            float d1 = float.PositiveInfinity, d2 = float.PositiveInfinity;
            foreach (var p in landmarks)
            {
                if (p.Surface == null) continue;
                float d = (NearestPoint(p, feet) - feet).sqrMagnitude;
                if (d < d1) { second = first; d2 = d1; first = p; d1 = d; }
                else if (d < d2) { second = p; d2 = d; }
            }
            WriteLandmark(first, 0, feet, output); WriteLandmark(second, 8, feet, output);
        }

        private void WriteLandmark(Landmark p, int offset, Vector3 feet, float[] output)
        {
            if (p == null) return;
            Vector3 delta = NearestPoint(p, feet) - feet;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 horizontal = Vector3.ProjectOnPlane(delta, Vector3.up);
            Vector3 direction = horizontal.normalized;
            output[offset] = 1f;
            output[offset + 1] = Vector3.Dot(direction, right);
            output[offset + 2] = Vector3.Dot(direction, forward);
            output[offset + 3] = Mathf.Clamp01(horizontal.magnitude / 50f);
            output[offset + 4] = Mathf.Clamp(delta.y / 8f, -1f, 1f);
            output[offset + 5] = Mathf.Clamp(Mathf.Atan2(-Vector3.Dot(p.Normal, forward), p.Normal.y) / (Mathf.PI * 0.5f), -1f, 1f);
            output[offset + 6] = p.Touched ? 1f : 0f;
            output[offset + 7] = p.Traversed ? 1f : 0f;
        }

        private sealed class MemorySensor : ISensor
        {
            private readonly RampMemorySensorComponent owner;
            public MemorySensor(RampMemorySensorComponent owner) { this.owner = owner; }
            public ObservationSpec GetObservationSpec() => ObservationSpec.Vector(ObservationSize);
            public int Write(ObservationWriter writer)
            { owner.Capture(owner.values); for (int i = 0; i < ObservationSize; i++) writer[i] = owner.values[i]; return ObservationSize; }
            public string GetName() => SensorName;
            public byte[] GetCompressedObservation() => null;
            public CompressionSpec GetCompressionSpec() => CompressionSpec.Default();
            public void Update() { }
            public void Reset() { }
        }
    }
}
