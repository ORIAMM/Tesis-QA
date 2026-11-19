using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TesisQA.AI
{
    // Outlines accepted findings only. Bounds feedback never changes detection or physics.
    public sealed class ParticipantAnomalyHighlights : IDisposable
    {
        public const string MaterialResource = "ParticipantAnomalyHighlight";
        private readonly List<GameObject> markers = new List<GameObject>();
        private IReadOnlyList<DefectFinding> activeFindings;
        private GameObject root;
        private Material material;
        public int Count => markers.Count;
        public Transform Root => root != null ? root.transform : null;

        public void Sync(IReadOnlyList<DefectFinding> findings, bool visible,
            Func<DefectFinding, Vector3[]> outlineResolver = null)
        {
            if (!ReferenceEquals(activeFindings, findings) || (findings?.Count ?? 0) < markers.Count)
            {
                Dispose();
                activeFindings = findings;
            }
            if (findings != null && findings.Count > 0 && root == null)
            {
                // Resources keeps the material and its shader in standalone builds.
                material = Resources.Load<Material>(MaterialResource);
                if (material == null) { Debug.LogError("Missing participant highlight material resource."); return; }
                root = new GameObject("Confirmed anomaly highlights");
            }
            if (root == null) return;
            while (markers.Count < findings.Count)
                CreateMarker(findings[markers.Count], outlineResolver?.Invoke(findings[markers.Count]));
            root.SetActive(visible);
        }

        public void Hide() { if (root != null) root.SetActive(false); }

        private void CreateMarker(DefectFinding finding, Vector3[] outline)
        {
            var marker = new GameObject("Confirmed anomaly " + (markers.Count + 1));
            marker.transform.SetParent(root.transform, false);
            Vector3 normal = finding.normal.sqrMagnitude > .1f ? finding.normal.normalized : Vector3.up;
            Vector3 surfaceUp = Mathf.Abs(normal.y) > .9f ? Vector3.forward : Vector3.up;
            marker.transform.SetPositionAndRotation(finding.position + normal * .045f,
                Quaternion.LookRotation(-normal, surfaceUp));
            Vector3[] corners;
            if (outline != null && outline.Length >= 3)
            {
                corners = new Vector3[outline.Length];
                for (int i = 0; i < outline.Length; i++) corners[i] = marker.transform.InverseTransformPoint(outline[i]);
            }
            else corners = new[] { new Vector3(-.65f, -.65f, 0), new Vector3(.65f, -.65f, 0),
                new Vector3(.65f, .65f, 0), new Vector3(-.65f, .65f, 0) };
            Stroke(marker.transform, "Anomaly boundary", corners, true, .065f);
            markers.Add(marker);
        }

        private void Stroke(Transform parent, string name, Vector3[] points, bool loop, float width)
        {
            var line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.sharedMaterial = material;
            line.useWorldSpace = false;
            line.loop = loop; line.widthMultiplier = width;
            line.numCapVertices = 4; line.numCornerVertices = 4;
            line.positionCount = points.Length; line.SetPositions(points);
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        }

        public void Dispose()
        {
            if (root != null)
            {
                root.SetActive(false);
                if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
            root = null; markers.Clear(); activeFindings = null;
        }
    }
}
