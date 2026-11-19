using System;
using System.Collections.Generic;
using UnityEngine;

namespace TesisQA.AI
{
    // Not an ML-Agents SensorComponent: no new observations, rewards or steering.
    public sealed class LocalCollisionDetector
    {
        private readonly DefectGeometry geometry;
        private readonly bool inspectWalls;
        private readonly Dictionary<string, int> confirmations = new Dictionary<string, int>();
        private readonly HashSet<string> emitted = new HashSet<string>();
        public LocalCollisionDetector(DefectGeometry geometry, bool inspectWalls = false)
        { this.geometry = geometry; this.inspectWalls = inspectWalls; }

        public void Scan(Vector3 eye, Quaternion yaw, float range, Action<DefectVisualHit> found)
        {
            var current = new Dictionary<string, DefectVisualHit>();
            for (int row = 0; row < 6; row++)
                for (int column = 0; column < 9; column++)
                {
                    Vector3 direction = yaw * Quaternion.Euler(10f + row * 12f, -50f + column * 12.5f, 0f) * Vector3.forward;
                    if (!geometry.Cast(new Ray(eye, direction), range, out var hit) || !hit.IsInspectableFloor
                        || DefectGeometry.Supported(hit.point)) continue;
                    // A spatial candidate must recur in two scans, not just two rays.
                    string key = hit.surface.id + ":" + Mathf.FloorToInt(hit.point.x) + ":"
                        + Mathf.FloorToInt(hit.point.y * 4f) + ":" + Mathf.FloorToInt(hit.point.z);
                    current[key] = hit;
                }
            if (inspectWalls)
                for (int row = 0; row < 4; row++)
                    for (int column = 0; column < 13; column++)
                    {
                        float height = row == 0 ? .45f : row == 1 ? .75f : row == 2 ? 1.15f : 1.65f;
                        Vector3 origin = eye + Vector3.up * (height - 1.6f);
                        Vector3 direction = yaw * Quaternion.Euler(0, -60f + column * 10f, 0) * Vector3.forward;
                        if (!geometry.Cast(new Ray(origin, direction), range, out var hit)
                            || !hit.IsInspectableWall || DefectGeometry.CollisionPresent(hit)) continue;
                        string key = "wall:" + hit.surface.id + ":" + Mathf.FloorToInt(hit.point.x) + ":"
                            + Mathf.FloorToInt(hit.point.y * 4f) + ":" + Mathf.FloorToInt(hit.point.z);
                        current[key] = hit;
                    }
            foreach (string old in new List<string>(confirmations.Keys))
                if (!current.ContainsKey(old)) confirmations.Remove(old);
            foreach (var sample in current)
            {
                confirmations.TryGetValue(sample.Key, out int count);
                confirmations[sample.Key] = count + 1;
                if (count >= 1 && emitted.Add(sample.Key)) found(sample.Value);
            }
        }
    }
}
