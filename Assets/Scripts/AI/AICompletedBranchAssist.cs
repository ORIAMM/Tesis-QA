using UnityEngine;

namespace TesisQA.AI
{
    /// <summary>
    /// Optional hybrid action assistance, NOT a learned PPO memory/sensor.
    /// Restricts only re-entry into a fully observed + visited leaf component
    /// when another confirmed open, unfinished exit exists. Never bans backtracking.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AICompletedBranchAssist : MonoBehaviour
    {
        // At speed 6 the maximum straight-line travel per scan is about 0.6 m.
        public const float ScanIntervalSeconds = 0.1f;
        private const float Spacing = 2f;
        private const float CenterTolerance = 0.6f;
        private const int MaximumNodes = 4096;
        private readonly AIObservedBranchGraph graph = new AIObservedBranchGraph();
        private readonly RaycastHit[] hits = new RaycastHit[64];
        private readonly bool[] completed = new bool[AIObservedBranchGraph.Directions];
        private CharacterController body;
        private Vector3 origin;
        private float nextScan;
        private AIObservedBranchGraph.Node current;
        private Vector3 redirect;
        private float redirectUntil;
        public int RedirectCount { get; private set; }
        public int CompletedExits { get; private set; }
        public int ObservedNodes => graph.Count;

        public void ResetEpisode(Vector3 spawn)
        {
            graph.Clear(); current = null; origin = spawn;
            nextScan = 0f; redirectUntil = 0f;
            RedirectCount = 0; CompletedExits = 0;
            System.Array.Clear(completed, 0, completed.Length);
            body = GetComponent<CharacterController>();
        }

        private static Vector3 Direction(int d)
            => Quaternion.Euler(0f, d * 45f, 0f) * Vector3.forward;

        // Feet-surface key, not floor index obtained from the generator.
        private Vector3Int Key(Vector3 p) => new Vector3Int(
            Mathf.RoundToInt((p.x - origin.x) / Spacing),
            Mathf.RoundToInt((p.y - origin.y) * 4f),
            Mathf.RoundToInt((p.z - origin.z) / Spacing));

        public void Filter(ref float forward, ref float turn)
        {
            if (body == null || !body.enabled) return;
            Vector3 feet = transform.position;
            feet.y = body.bounds.min.y;
            if (Time.fixedTime >= nextScan)
            {
                nextScan = Time.fixedTime + ScanIntervalSeconds;
                Scan(feet);
            }
            if (Time.fixedTime < redirectUntil)
            {
                Vector3 delta = Vector3.ProjectOnPlane(redirect - feet, Vector3.up);
                if (delta.magnitude > 0.35f && Passage(feet, redirect) == AIObservedBranchGraph.PortState.Open)
                {
                    float yaw = Vector3.SignedAngle(transform.forward, delta, Vector3.up);
                    turn = Mathf.Clamp(yaw / 45f, -1f, 1f);
                    forward = Mathf.Abs(yaw) < 20f ? 0.6f : 0f;
                    return;
                }
                redirectUntil = 0f;
            }
            if (current == null || Mathf.Abs(forward) < 0.1f
                || Vector3.Distance(feet, current.Position) > CenterTolerance) return;
            Vector3 requested = transform.forward * Mathf.Sign(forward);
            int attempted = -1;
            float alignment = 0.85f;
            for (int d = 0; d < AIObservedBranchGraph.Directions; d++)
            {
                float dot = Vector3.Dot(requested, Direction(d));
                if (dot > alignment && completed[d])
                { alignment = dot; attempted = d; }
            }
            if (attempted < 0) return;
            // Only choose confirmed walkable, unfinished alternatives. Unknown
            // exits cannot justify a steering command into a wall/void/stair.
            int alternative = -1;
            float best = float.NegativeInfinity;
            for (int d = 0; d < AIObservedBranchGraph.Directions; d++)
            {
                var next = current.Neighbors[d];
                if (next == null || completed[d]
                    || Passage(feet, next.Position) != AIObservedBranchGraph.PortState.Open) continue;
                float score = Vector3.Dot(requested, Direction(d));
                if (score > best) { best = score; alternative = d; }
            }
            if (alternative < 0) return; // All done/no safe alternative: preserve PPO.
            redirect = current.Neighbors[alternative].Position;
            redirectUntil = Time.fixedTime + 3f;
            RedirectCount++;
            // Stop before redirecting next physics step. No teleport/action mask.
            forward = 0f;
        }

        private void Scan(Vector3 feet)
        {
            current = null; CompletedExits = 0;
            System.Array.Clear(completed, 0, completed.Length);
            Vector3Int key = Key(feet);
            Vector3 center = origin + new Vector3(key.x * Spacing, 0f, key.z * Spacing);
            center.y = feet.y;
            if (Vector3.Distance(center, feet) > CenterTolerance || graph.Count >= MaximumNodes) return;
            if (!Ground(center, out RaycastHit ground) || !Flat(ground, feet.y)) return;
            center.y = ground.point.y;
            key = Key(center);
            current = graph.Get(key, center);
            current.Visited = true; // Seen ground alone does not count as a visit.
            for (int d = 0; d < AIObservedBranchGraph.Directions; d++)
            {
                Vector3 end = center + Direction(d) * (Spacing * (d % 2 == 0 ? 1f : Mathf.Sqrt(2f)));
                AIObservedBranchGraph.PortState state = Passage(center, end);
                AIObservedBranchGraph.Node next = null;
                if (state == AIObservedBranchGraph.PortState.Open)
                    next = graph.Get(Key(end), end);
                graph.Observe(current, d, state, next);
            }
            for (int d = 0; d < AIObservedBranchGraph.Directions; d++)
            {
                completed[d] = graph.IsCompletedBranch(current, d);
                if (completed[d]) CompletedExits++;
            }
        }

        private static bool Flat(RaycastHit ground, float height)
            => ground.collider != null && ground.normal.y > 0.98f
                && Mathf.Abs(ground.point.y - height) < 0.12f;

        private bool Ground(Vector3 p, out RaycastHit hit)
            => Closest(p + Vector3.up * 0.4f, Vector3.down, 0.8f, out hit);

        private AIObservedBranchGraph.PortState Passage(Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            // Swept body clearance; wall evidence only. Slopes, steps, missing
            // ground, buffer overflow and ambiguous obstruction stay UNKNOWN.
            float radius = body.radius + body.skinWidth;
            int count = Physics.CapsuleCastNonAlloc(start + Vector3.up * (radius + 0.15f),
                start + Vector3.up * (body.height - radius), radius, delta.normalized,
                hits, delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return AIObservedBranchGraph.PortState.Unknown;
            bool obstruction = false;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (Self(hits[i].collider)) continue;
                obstruction = true;
                nearest = Mathf.Min(nearest, hits[i].distance);
            }
            if (obstruction)
            {
                // Two co-planar vertical face hits distinguish a tall wall from
                // a low stair riser. A ramp/step or offset return stays unknown.
                if (!Closest(start + Vector3.up * 0.45f, delta.normalized, delta.magnitude, out RaycastHit low)
                    || !Closest(start + Vector3.up * 1.45f, delta.normalized, delta.magnitude, out RaycastHit high)
                    || Mathf.Abs(low.normal.y) >= 0.1f || Mathf.Abs(high.normal.y) >= 0.1f
                    || Mathf.Abs(low.distance - high.distance) > 0.05f
                    || low.distance > nearest + radius / Mathf.Max(0.1f,
                        Mathf.Abs(Vector3.Dot(low.normal, delta.normalized))) + 0.1f || nearest <= 0.01f)
                    return AIObservedBranchGraph.PortState.Unknown;
                // Sample only the VISIBLE approach, never the floor behind a wall.
                float approach = Mathf.Max(0f, nearest - 0.1f);
                if (!FlatSegment(start, start + delta.normalized * approach))
                    return AIObservedBranchGraph.PortState.Unknown;
                return AIObservedBranchGraph.PortState.Blocked;
            }
            if (!FlatSegment(start, end)) return AIObservedBranchGraph.PortState.Unknown;
            return AIObservedBranchGraph.PortState.Open;
        }

        private bool FlatSegment(Vector3 start, Vector3 end)
        {
            for (int sample = 0; sample <= 4; sample++)
            {
                Vector3 p = Vector3.Lerp(start, end, sample / 4f);
                if (!Ground(p, out RaycastHit ground) || !Flat(ground, start.y)) return false;
            }
            return true;
        }

        private bool Self(Collider collider)
            => collider.transform == transform || collider.transform.IsChildOf(transform);

        private bool Closest(Vector3 start, Vector3 direction, float distance, out RaycastHit hit)
        {
            hit = default;
            int count = Physics.RaycastNonAlloc(start, direction, hits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count == hits.Length) return false;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (Self(hits[i].collider) || hits[i].distance >= nearest) continue;
                hit = hits[i]; nearest = hit.distance;
            }
            return hit.collider != null;
        }
    }
}
