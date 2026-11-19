using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace TesisQA.AI
{
    public sealed class DefectVisualSurface
    {
        public string id;
        public Transform transform;
        public Renderer renderer;
        public MeshCollider collider;
        public DefectMeshData data;
        public Transform tile;
        public bool inspectFloor = true;
    }

    public struct DefectVisualHit
    {
        public DefectVisualSurface surface;
        public Vector3 point, normal;
        public float distance;
        public bool IsInspectableFloor => surface != null && surface.inspectFloor && normal.y >= .95f
            && Mathf.Abs(point.y - surface.tile.position.y) <= .2f;
        public bool IsInspectableWall => surface != null && surface.inspectFloor && Mathf.Abs(normal.y) <= .15f
            && point.y - surface.tile.position.y >= .3f && point.y - surface.tile.position.y <= 2.25f;
    }

    public sealed class DefectGeometry
    {
        public readonly List<DefectVisualSurface> surfaces = new List<DefectVisualSurface>();

        public DefectGeometry(Transform root, DefectMeshLibrary library)
        {
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                var renderer = filter.GetComponent<Renderer>();
                if (renderer == null || !renderer.enabled || filter.sharedMesh == null) continue;
                var data = library.Find(filter.sharedMesh);
                if (data == null) throw new InvalidOperationException("Unbaked visual mesh: " + filter.sharedMesh.name);
                Transform tile = filter.transform;
                while (tile.parent != root && tile.parent != null) tile = tile.parent;
                surfaces.Add(new DefectVisualSurface { id = Path(filter.transform, root),
                    transform = filter.transform, renderer = renderer, data = data, tile = tile,
                    // Stair treads intentionally use a smooth collision ramp in this
                    // project. They remain visual occluders, outside flat-floor inspection.
                    inspectFloor = tile.name.IndexOf("stair", StringComparison.OrdinalIgnoreCase) < 0,
                    collider = filter.GetComponent<MeshCollider>() });
            }
            surfaces.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        }

        public static string Path(Transform item, Transform root)
        {
            string result = item.name + "#" + item.GetSiblingIndex();
            while (item.parent != null && item.parent != root)
            { item = item.parent; result = item.name + "#" + item.GetSiblingIndex() + "/" + result; }
            return result;
        }

        // Cast against VISIBLE triangles, independent of the actual physics mesh.
        // Bounds prune most modules; rays stop at the nearest rendered wall/floor.
        public bool Cast(Ray ray, float range, out DefectVisualHit hit)
        {
            hit = default;
            float nearest = range;
            foreach (var surface in surfaces)
            {
                if (!surface.renderer.enabled || !surface.transform.gameObject.activeInHierarchy
                    || !surface.renderer.bounds.IntersectRay(ray, out float boundDistance)
                    || boundDistance > nearest) continue;
                Vector3 origin = surface.transform.InverseTransformPoint(ray.origin);
                // Do not normalize: local ray parameter remains world distance.
                Vector3 direction = surface.transform.InverseTransformVector(ray.direction);
                var v = surface.data.vertices; var t = surface.data.triangles;
                for (int i = 0; i < t.Length; i += 3)
                {
                    Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                    if (!Intersect(origin, direction, a, b, c, out float distance)
                        || distance >= nearest) continue;
                    nearest = distance;
                    hit = new DefectVisualHit { surface = surface, distance = distance,
                        point = ray.GetPoint(distance), normal = surface.transform.localToWorldMatrix
                            .inverse.transpose.MultiplyVector(Vector3.Cross(b - a, c - a)).normalized };
                }
            }
            return hit.surface != null;
        }

        public static bool Intersect(Vector3 origin, Vector3 direction,
            Vector3 a, Vector3 b, Vector3 c, out float distance)
        {
            distance = 0f;
            Vector3 e1 = b - a, e2 = c - a, p = Vector3.Cross(direction, e2);
            float determinant = Vector3.Dot(e1, p);
            if (determinant <= .0000001f) return false; // Front faces only.
            float inverse = 1f / determinant;
            Vector3 offset = origin - a;
            float u = Vector3.Dot(offset, p) * inverse;
            if (u < -.00001f || u > 1.00001f) return false;
            Vector3 q = Vector3.Cross(offset, e1);
            float w = Vector3.Dot(direction, q) * inverse;
            if (w < -.00001f || u + w > 1.00001f) return false;
            distance = Vector3.Dot(e2, q) * inverse;
            return distance > .00001f;
        }

        public static bool Supported(Vector3 point)
        {
            // A hit on the lower floor cannot satisfy this short surface probe.
            foreach (var hit in Physics.RaycastAll(point + Vector3.up * .12f, Vector3.down,
                .24f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (hit.normal.y > .8f && !(hit.collider is CharacterController)
                    && Mathf.Abs(hit.point.y - point.y) <= .1f) return true;
            return false;
        }

        public static bool CollisionPresent(DefectVisualHit visual)
        {
            if (visual.IsInspectableFloor) return Supported(visual.point);
            if (!visual.IsInspectableWall) return false;
            Vector3 normal = visual.normal.normalized;
            // Probe only at the visible wall. A different wall farther behind it
            // cannot mask this wall's missing collision.
            foreach (var hit in Physics.RaycastAll(visual.point + normal * .12f, -normal, .24f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!(hit.collider is CharacterController) && Vector3.Dot(hit.normal, normal) > .8f
                    && Vector3.Distance(hit.point, visual.point) <= .1f) return true;
            return false;
        }

        // Subtract a world-XZ rectangle from every triangle (including slab bottom).
        // Polygon clipping preserves unaffected triangle pieces and leaves a real hole.
        public static Mesh CutHole(DefectVisualSurface surface, Vector3 center, float width)
        {
            var output = new List<Vector3>(); var indices = new List<int>();
            var source = surface.data; float half = width * .5f;
            for (int i = 0; i < source.triangles.Length; i += 3)
            {
                var remaining = new List<Vector3>();
                for (int j = 0; j < 3; j++) remaining.Add(surface.transform.TransformPoint(source.vertices[source.triangles[i+j]]));
                // Retain the outside strip, then clip the remainder to the inside.
                for (int edge = 0; edge < 4 && remaining.Count >= 3; edge++)
                {
                    int axis = edge < 2 ? 0 : 2;
                    float sign = edge % 2 == 0 ? 1f : -1f;
                    float boundary = center[axis] + (edge % 2 == 0 ? -half : half);
                    Emit(Clip(remaining, axis, boundary, sign, false), surface.transform, output, indices);
                    remaining = Clip(remaining, axis, boundary, sign, true);
                }
            }
            var mesh = new Mesh { name = "Runtime collision hole (visual unchanged)",
                indexFormat = output.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(output); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
            return mesh;
        }

        private static List<Vector3> Clip(List<Vector3> polygon, int axis, float boundary, float sign, bool inside)
        {
            var result = new List<Vector3>();
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector3 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                float da = (a[axis] - boundary) * sign, db = (b[axis] - boundary) * sign;
                bool keepA = inside ? da >= 0f : da <= 0f, keepB = inside ? db >= 0f : db <= 0f;
                if (keepA) result.Add(a);
                if (keepA != keepB) result.Add(Vector3.LerpUnclamped(a, b, da / (da - db)));
            }
            return result;
        }

        private static void Emit(List<Vector3> polygon, Transform transform, List<Vector3> vertices, List<int> indices)
        {
            for (int i = 1; i + 1 < polygon.Count; i++)
            {
                if (Vector3.Cross(polygon[i] - polygon[0], polygon[i+1] - polygon[0]).sqrMagnitude < 1e-12f) continue;
                int start = vertices.Count;
                vertices.Add(transform.InverseTransformPoint(polygon[0]));
                vertices.Add(transform.InverseTransformPoint(polygon[i]));
                vertices.Add(transform.InverseTransformPoint(polygon[i+1]));
                indices.Add(start); indices.Add(start+1); indices.Add(start+2);
            }
        }

        public static string Hash(string value)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant();
        }
        public static string Vector(Vector3 v) => v.x.ToString("R", CultureInfo.InvariantCulture) + ","
            + v.y.ToString("R", CultureInfo.InvariantCulture) + "," + v.z.ToString("R", CultureInfo.InvariantCulture);
    }
}
