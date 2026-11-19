using System;
using System.Collections.Generic;
using Unity.MLAgents.Sensors;
using UnityEngine;

namespace TesisQA.AI
{
    /// <summary>Observed episodic branch features ONLY. No actuator, reward,
    /// action mask, WFC access, area tags, target coordinates or route planner.</summary>
    [DisallowMultipleComponent]
    public sealed class BranchMemorySensorComponent : SensorComponent
    {
        public const int ObservationSize = 32;
        // Sort AFTER VectorSensor so the existing 117 inputs remain a prefix.
        public const string SensorName = "Z Explorer Branch Memory v1";
        public const float ScanInterval = 0.1f;
        private const float Spacing = 2f;
        private const float ViewRadius = 6f;
        private const float VisitedRadius = 2.5f;
        private const int NodesPerScan = 4;
        private const int MaximumNodes = 4096;
        private readonly AIObservedBranchGraph graph = new AIObservedBranchGraph();
        private readonly RaycastHit[] hits = new RaycastHit[64];
        private readonly Collider[] overlaps = new Collider[64];
        private readonly Dictionary<Vector3Int, Vector3> lastAttempts = new Dictionary<Vector3Int, Vector3>();
        private readonly List<Vector3> candidates = new List<Vector3>();
        private readonly float[] features = new float[ObservationSize];
        private CharacterController body;
        private float nextScan;
        private bool initialized;
        private AIObservedBranchGraph.Node current;
        private int lastProbeFailure; // Geometry-test diagnostics; never an observation.
        public bool ObservationEnabled { get; set; } = true;
        public int ObservedNodes => graph.Count;
        public int CompletedExits { get; private set; }
        public int UnknownPorts { get; private set; }
        public float UnknownFraction => UnknownPorts / (float)Mathf.Max(1, graph.Count * 8);

        public override ISensor[] CreateSensors() => ObservationEnabled
            ? new ISensor[] { new BranchSensor(this) } : Array.Empty<ISensor>();

        public void ResetEpisode()
        {
            graph.Clear(); lastAttempts.Clear(); current = null;
            CompletedExits = UnknownPorts = 0; nextScan = 0f;
            body = GetComponent<CharacterController>(); initialized = true;
            Array.Clear(features, 0, features.Length);
        }

        private static Vector3Int Key(Vector3 p) => new Vector3Int(
            Mathf.RoundToInt(p.x / Spacing), Mathf.RoundToInt(p.y * 4f), Mathf.RoundToInt(p.z / Spacing));
        private static Vector3 Step(int d)
        {
            // Integer lattice: diagonal edges must not round to a different position.
            Vector3 v = Quaternion.Euler(0f, d * 45f, 0f) * Vector3.forward;
            return new Vector3(Mathf.RoundToInt(v.x), 0f, Mathf.RoundToInt(v.z)) * Spacing;
        }
        private Vector3 Feet => new Vector3(transform.position.x, body.bounds.min.y, transform.position.z);

        public void RecordPosition()
        {
            if (!ObservationEnabled) return;
            if (!initialized) ResetEpisode();
            if (body == null || !body.enabled) return;
            if (Time.fixedTime >= nextScan)
            {
                nextScan = Time.fixedTime + ScanInterval;
                SampleNow();
            }
        }

        // Public for deterministic geometry checks; this never changes an action.
        public void SampleNow()
        {
            if (!initialized) ResetEpisode();
            if (body == null || !body.enabled) return;
            Vector3 feet = Feet;
            if (!Ground(feet, feet.y, out RaycastHit underfoot) || !Flat(underfoot, feet.y))
            { current = null; return; } // Ramp remains unresolved, not a wall.
            feet.y = underfoot.point.y;
            candidates.Clear();
            Vector3Int center = Key(feet);
            for (int x = -3; x <= 3; x++)
                for (int z = -3; z <= 3; z++)
                {
                    Vector3 p = new Vector3((center.x + x) * Spacing, feet.y, (center.z + z) * Spacing);
                    if (Vector3.Distance(p, feet) <= ViewRadius) candidates.Add(p);
                }
            candidates.Sort((a, b) => (a - feet).sqrMagnitude.CompareTo((b - feet).sqrMagnitude));
            int budget = NodesPerScan;
            foreach (Vector3 p in candidates)
            {
                Vector3Int candidateKey = Key(p);
                if (lastAttempts.TryGetValue(candidateKey, out Vector3 previous)
                    && Vector3.Distance(previous, feet) < 1f) continue;
                if (budget-- <= 0) break;
                lastAttempts[candidateKey] = feet;
                if (!Ground(p, feet.y, out RaycastHit ground) || !Flat(ground, feet.y)) continue;
                Vector3 position = ground.point;
                if (!Visible(feet, position + Vector3.up * 0.15f) || !StandingClear(position)) continue;
                if (graph.Count >= MaximumNodes) break;
                var node = graph.Get(Key(position), position);
                for (int d = 0; d < 8; d++)
                {
                    Vector3 end = position + Step(d);
                    var state = Probe(position, end, feet);
                    if (state == AIObservedBranchGraph.PortState.Unknown) continue; // Retain past static-map evidence.
                    AIObservedBranchGraph.Node next = null;
                    if (state == AIObservedBranchGraph.PortState.Open)
                        next = graph.Get(Key(end), end);
                    graph.Observe(node, d, state, next);
                }
            }
            // Visited = the locally observed/reachable REGION around the actual
            // trajectory, not every arbitrary lattice centre physically touched.
            // A wall between the agent and a node prevents credit across it.
            current = null;
            float nearest = float.PositiveInfinity;
            UnknownPorts = 0;
            foreach (var node in graph.Nodes)
            {
                for (int d = 0; d < 8; d++) if (node.Ports[d] == AIObservedBranchGraph.PortState.Unknown) UnknownPorts++;
                float distance = Vector3.Distance(feet, node.Position);
                if (distance > VisitedRadius || Mathf.Abs(node.Position.y - feet.y) > 0.12f) continue;
                if (Probe(feet, node.Position, feet) != AIObservedBranchGraph.PortState.Open) continue;
                node.Visited = true;
                if (distance < nearest) { nearest = distance; current = node; }
            }
        }

        public void Capture(float[] output)
        {
            if (output == null || output.Length != ObservationSize) throw new ArgumentException("Branch memory requires 32 values.");
            RecordPosition(); Array.Clear(output, 0, output.Length); CompletedExits = 0;
            if (current == null) return;
            int facing = Mathf.RoundToInt(transform.eulerAngles.y / 45f) & 7;
            // Agent-relative: front, front-right, right, back-right, back,
            // back-left, left, front-left. Each: state, visited, frontier, complete.
            for (int local = 0; local < 8; local++)
            {
                int d = (facing + local) & 7;
                var state = current.Ports[d];
                output[local * 4] = state == AIObservedBranchGraph.PortState.Open ? 1f
                    : state == AIObservedBranchGraph.PortState.Blocked ? -1f : 0f;
                if (state != AIObservedBranchGraph.PortState.Open) continue;
                graph.Describe(current, d, out float visited, out float frontier, out bool complete);
                output[local * 4 + 1] = visited;
                output[local * 4 + 2] = frontier;
                output[local * 4 + 3] = complete ? 1f : 0f;
                if (complete) CompletedExits++;
            }
        }

        private AIObservedBranchGraph.PortState Probe(Vector3 start, Vector3 end, Vector3 observer)
        {
            lastProbeFailure = 0;
            Vector3 delta = end - start;
            if (delta.magnitude < 0.03f) return StandingClear(end)
                ? AIObservedBranchGraph.PortState.Open : AIObservedBranchGraph.PortState.Unknown;
            float radius = body.radius + body.skinWidth;
            int count = Physics.CapsuleCastNonAlloc(start + Vector3.up * (radius + 0.15f),
                start + Vector3.up * (body.height - radius), radius, delta.normalized, hits,
                delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return AIObservedBranchGraph.PortState.Unknown;
            float nearest = float.PositiveInfinity;
            RaycastHit obstacle = default;
            for (int i = 0; i < count; i++)
                if (!Self(hits[i].collider) && hits[i].distance < nearest)
                { nearest = hits[i].distance; obstacle = hits[i]; }
            if (!float.IsPositiveInfinity(nearest))
            {
                // Confirm the actual swept-body contact, not a centre ray that
                // can entirely miss a curved wall grazing the capsule's side.
                // Probe two heights along the contact's horizontal normal.
                Vector3 normal = Vector3.ProjectOnPlane(obstacle.normal, Vector3.up).normalized;
                Vector3 witness = obstacle.point + normal * 0.6f;
                Vector3 lowOrigin = new Vector3(witness.x, start.y + 0.45f, witness.z);
                Vector3 highOrigin = new Vector3(witness.x, start.y + 1.45f, witness.z);
                if (nearest < 0.01f || normal.sqrMagnitude < 0.5f) return Unknown(1);
                if (!Closest(lowOrigin, -normal, 1.2f, out RaycastHit low)) return Unknown(2);
                if (!Closest(highOrigin, -normal, 1.2f, out RaycastHit high)) return Unknown(3);
                if (Mathf.Abs(low.normal.y) > 0.1f || Mathf.Abs(high.normal.y) > 0.1f) return Unknown(4);
                if (Vector3.Dot(low.normal, high.normal) < 0.8f) return Unknown(5);
                    // Kenney walls have bevel/base thickness differences. A
                    // 1m height rise over <=0.6m is steeper than the 50deg motor
                    // slope limit, unlike the walkable stair/ramp profile.
                if (Mathf.Abs(low.distance - high.distance) > 0.6f) return Unknown(6);
                if (!VisibleWall(observer, low)) return Unknown(7);
                if (!VisibleWall(observer, high)) return Unknown(8);
                if (!SupportedApproach(start, start + delta.normalized * Mathf.Max(0f, nearest - 0.1f))) return Unknown(9);
                return AIObservedBranchGraph.PortState.Blocked;
            }
            if (!Visible(observer, end + Vector3.up * 0.15f)) return Unknown(10);
            if (!StandingClear(end)) return Unknown(11);
            if (!FlatSegment(start, end)) return Unknown(12);
            return AIObservedBranchGraph.PortState.Open;
        }
        private AIObservedBranchGraph.PortState Unknown(int reason)
        { lastProbeFailure = reason; return AIObservedBranchGraph.PortState.Unknown; }

        private bool FlatSegment(Vector3 a, Vector3 b)
        {
            for (int i = 0; i <= 4; i++)
                if (!Ground(Vector3.Lerp(a, b, i / 4f), a.y, out RaycastHit ground) || !Flat(ground, a.y)) return false;
            return true;
        }
        private bool SupportedApproach(Vector3 a, Vector3 b)
        {
            // A confirmed tall wall can have a low bevel/plinth underneath it.
            // Requiring a perfectly flat normal there leaves every end-cap
            // corner UNKNOWN. Allow only step-sized relief, not an ascent/ramp.
            float relief = body.stepOffset + body.skinWidth + 0.05f;
            for (int i = 0; i <= 4; i++)
                if (!Ground(Vector3.Lerp(a, b, i / 4f), a.y, out RaycastHit ground)
                    || ground.collider == null || ground.normal.y < 0.1f
                    || Mathf.Abs(ground.point.y - a.y) > relief) return false;
            return true;
        }
        private static bool Flat(RaycastHit hit, float height)
            => hit.collider != null && hit.normal.y > 0.98f && Mathf.Abs(hit.point.y - height) < 0.12f;
        private bool Ground(Vector3 point, float height, out RaycastHit hit)
            => Closest(new Vector3(point.x, height + 0.4f, point.z), Vector3.down, 0.8f, out hit);
        private bool Visible(Vector3 observer, Vector3 point)
        {
            Vector3 eye = observer + Vector3.up * 1.5f;
            Vector3 delta = point - eye;
            return !Closest(eye, delta.normalized, Mathf.Max(0f, delta.magnitude - 0.02f), out _);
        }
        private bool VisibleWall(Vector3 observer, RaycastHit surface)
        {
            Vector3 eye = observer + Vector3.up * 1.5f;
            Vector3 point = surface.point + surface.normal * 0.05f;
            Vector3 delta = point - eye;
            if (!Closest(eye, delta.normalized, Mathf.Max(0f, delta.magnitude - 0.02f), out RaycastHit hit)) return true;
            // A bevel may self-occlude the exact witness endpoint. Accept only
            // the SAME wall within body-radius distance of that endpoint, never
            // an unrelated/remote occluder. Open-floor visibility stays strict.
            return hit.collider != null && hit.collider == surface.collider
                && hit.distance >= delta.magnitude - 0.4f && Mathf.Abs(hit.normal.y) < 0.2f;
        }
        private bool StandingClear(Vector3 p)
        {
            float radius = body.radius + body.skinWidth;
            int count = Physics.OverlapCapsuleNonAlloc(p + Vector3.up * (radius + 0.15f),
                p + Vector3.up * (body.height - radius), radius, overlaps,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++) if (!Self(overlaps[i])) return false;
            return true;
        }
        private bool Self(Collider c) => c.transform == transform || c.transform.IsChildOf(transform);
        private bool Closest(Vector3 start, Vector3 direction, float distance, out RaycastHit hit)
        {
            hit = default;
            int count = Physics.RaycastNonAlloc(start, direction, hits, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return true; // Fail closed on saturated visibility probes.
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (!Self(hits[i].collider) && hits[i].distance < nearest) { nearest = hits[i].distance; hit = hits[i]; }
            return hit.collider != null;
        }

        private sealed class BranchSensor : ISensor
        {
            private readonly BranchMemorySensorComponent owner;
            public BranchSensor(BranchMemorySensorComponent owner) { this.owner = owner; }
            public ObservationSpec GetObservationSpec() => ObservationSpec.Vector(ObservationSize);
            public int Write(ObservationWriter writer)
            { owner.Capture(owner.features); for (int i = 0; i < ObservationSize; i++) writer[i] = owner.features[i]; return ObservationSize; }
            public string GetName() => SensorName;
            public byte[] GetCompressedObservation() => null;
            public CompressionSpec GetCompressionSpec() => CompressionSpec.Default();
            public void Update() { }
            public void Reset() { }
        }
    }
}
