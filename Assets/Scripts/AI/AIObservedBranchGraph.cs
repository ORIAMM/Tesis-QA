using System.Collections.Generic;
using UnityEngine;

namespace TesisQA.AI
{
    // Evidence supplied by local probes/travel only. Unknown is NEVER a wall.
    // Completion is recomputed, not a permanent blacklist. No WFC/area/tag access.
    public sealed class AIObservedBranchGraph
    {
        public const int Directions = 8;
        public enum PortState { Unknown, Blocked, Open }
        public sealed class Node
        {
            public readonly Vector3Int Key;
            public readonly Vector3 Position;
            public bool Visited;
            public readonly PortState[] Ports = new PortState[Directions];
            public readonly Node[] Neighbors = new Node[Directions];
            public Node(Vector3Int key, Vector3 position) { Key = key; Position = position; }
        }

        private readonly Dictionary<Vector3Int, Node> nodes = new Dictionary<Vector3Int, Node>();
        private readonly HashSet<Node> seen = new HashSet<Node>();
        private readonly Queue<Node> pending = new Queue<Node>();
        public int Count => nodes.Count;
        public IEnumerable<Node> Nodes => nodes.Values;
        public bool TryGet(Vector3Int key, out Node node) => nodes.TryGetValue(key, out node);
        public void Clear() { nodes.Clear(); seen.Clear(); pending.Clear(); }
        public Node Get(Vector3Int key, Vector3 position)
        {
            if (!nodes.TryGetValue(key, out Node node))
            { node = new Node(key, position); nodes.Add(key, node); }
            return node;
        }

        public void Observe(Node from, int direction, PortState state, Node to = null)
        {
            // Revoke stale reciprocal evidence when a local probe changes.
            Node old = from.Neighbors[direction];
            int opposite = (direction + 4) % Directions;
            if (old != null && old != to && old.Neighbors[opposite] == from)
            {
                old.Neighbors[opposite] = null;
                old.Ports[opposite] = PortState.Unknown;
            }
            from.Ports[direction] = state;
            from.Neighbors[direction] = state == PortState.Open ? to : null;
            if (state == PortState.Open && to != null)
            {
                to.Ports[opposite] = PortState.Open;
                to.Neighbors[opposite] = from;
            }
        }

        public bool IsCompletedBranch(Node junction, int direction)
        {
            Node first = junction.Neighbors[direction];
            if (junction.Ports[direction] != PortState.Open || first == null) return false;
            seen.Clear(); pending.Clear(); seen.Add(first); pending.Enqueue(first);
            while (pending.Count > 0)
            {
                Node node = pending.Dequeue();
                if (!node.Visited) return false;
                for (int d = 0; d < Directions; d++)
                {
                    if (node.Ports[d] == PortState.Unknown) return false;
                    if (node.Ports[d] != PortState.Open) continue;
                    Node next = node.Neighbors[d];
                    if (next == null) return false;
                    if (next == junction)
                    {
                        // A cycle connecting another exit is transit, not a cul-de-sac.
                        if (node != first || d != (direction + 4) % Directions) return false;
                        continue;
                    }
                    if (seen.Add(next)) pending.Enqueue(next);
                }
            }
            return true;
        }

        // Summary of the OBSERVED component behind an exit. This is not an
        // oracle for the whole map. An unknown port is an unresolved frontier.
        public void Describe(Node junction, int direction, out float visitedFraction,
            out float frontierFraction, out bool complete)
        {
            visitedFraction = frontierFraction = 0f; complete = false;
            Node first = junction.Neighbors[direction];
            if (junction.Ports[direction] != PortState.Open || first == null) return;
            seen.Clear(); pending.Clear(); seen.Add(first); pending.Enqueue(first);
            int visited = 0, unresolved = 0, ports = 0;
            bool transit = false;
            Vector3 forward = (first.Position - junction.Position).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            while (pending.Count > 0)
            {
                Node node = pending.Dequeue();
                if (node.Visited) visited++;
                if (Vector3.Dot(node.Position - junction.Position, forward) < -0.9f) transit = true;
                for (int d = 0; d < Directions; d++)
                {
                    ports++;
                    if (node.Ports[d] == PortState.Unknown) { unresolved++; continue; }
                    if (node.Ports[d] != PortState.Open) continue;
                    Node next = node.Neighbors[d];
                    if (next == null) { unresolved++; continue; }
                    // Cut a local cross-section, not a single lattice point.
                    // Otherwise parallel lanes in a wide corridor form tiny
                    // grid cycles and NO dead-end can ever be completed.
                    Vector3 relative = next.Position - junction.Position;
                    if (next == junction || Mathf.Abs(Vector3.Dot(relative, forward)) < 0.9f
                        && Mathf.Abs(Vector3.Dot(relative, right)) <= 6f) continue;
                    if (seen.Add(next)) pending.Enqueue(next);
                }
            }
            visitedFraction = visited / (float)seen.Count;
            frontierFraction = unresolved / (float)System.Math.Max(1, ports);
            complete = visited == seen.Count && unresolved == 0 && !transit;
        }
    }
}
