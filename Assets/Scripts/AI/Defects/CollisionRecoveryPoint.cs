using System.Linq;
using TesisQA.WFC;
using UnityEngine;

namespace TesisQA.AI
{
    // Recovery uses real support at the tile centre and the player's footprint.
    // The healthy rim of a partially defective tile is not a new checkpoint.
    public static class CollisionRecoveryPoint
    {
        public static bool TryGet(DefectScenarioBuild map, WFCGenerator generator, Vector3 feet,
            float radius, out Vector3 safeFeet)
        {
            safeFeet = feet;
            RaycastHit? nearest = null;
            foreach (var hit in Physics.RaycastAll(feet + Vector3.up * .35f, Vector3.down, .85f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider is CharacterController || hit.normal.y < .95f) continue;
                if (!nearest.HasValue || hit.distance < nearest.Value.distance) nearest = hit;
            }
            if (!nearest.HasValue) return false;
            Vector3 point = nearest.Value.point;
            var cell = generator.WorldToWalkwayCell(point);
            if (!map.coverageCells.TryGetValue(cell, out var centre)
                || Mathf.Abs(centre.y - point.y) > .2f || !DefectGeometry.Supported(centre)) return false;
            foreach (var offset in new[] { Vector3.zero, Vector3.left * radius, Vector3.right * radius,
                Vector3.forward * radius, Vector3.back * radius })
                if (!DefectGeometry.Supported(point + offset)) return false;
            safeFeet = point + Vector3.up * .08f;
            return true;
        }

        public static bool TryNearestOnFloor(DefectScenarioBuild map, WFCGenerator generator,
            Vector3 reference, float radius, out Vector3 safeFeet)
        {
            foreach (var point in map.coverageCells.Values
                .Where(point => Mathf.Abs(point.y - reference.y) < .5f)
                .OrderBy(point => (point - reference).sqrMagnitude))
                if (TryGet(map, generator, point + Vector3.up * .08f, radius, out safeFeet)) return true;
            safeFeet = reference;
            return false;
        }
    }
}
