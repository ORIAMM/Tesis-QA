using System;
using System.Collections.Generic;
using System.Linq;
using TesisQA.WFC;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TesisQA.AI
{
    // A practice sandbox, with explicit guides and a healthy comparison. It never
    // creates participant sessions, advances study trials, or uses the AI policy.
    public sealed class SmallCollisionDemo : MonoBehaviour
    {
        public DefectExperimentProtocol protocol;
        public WFCGenerator generator;
        public AIWFCTrainingAdapter adapter;
        public AICharacterMotor motor;
        public Camera eye;
        public Vector3[] demonstrationSpawns;
        public int initialDefect = 1;
        public bool showGuides = true;
        public float mouseSensitivity = .12f;
        [SerializeField] private Text instructions, status;
        private DefectScenarioBuild build;
        private readonly List<Mesh> healthyMeshes = new List<Mesh>();
        private readonly List<MeshCollider> defectColliders = new List<MeshCollider>();
        private readonly List<Mesh> damagedMeshes = new List<Mesh>();
        private readonly List<bool> damagedEnabled = new List<bool>();
        private readonly List<LineRenderer> guides = new List<LineRenderer>();
        private readonly List<Material> guideMaterials = new List<Material>();
        private Material guideMaterial;
        private GameObject guideRoot;
        private bool captured, healthy, marked, skipMouse, jumpQueued;
        private int selected;
        private float pitch, safeHeight;
        private Vector3 lastHealthyPosition;
        private Quaternion lastHealthyRotation;
        private double movementGraceUntil;
        private string message = "";
        public bool Ready => build != null;
        public string Error { get; private set; }
        public int Falls { get; private set; }
        public int MarkCount => marked ? 1 : 0;
        public Vector3 SafeSpawn => demonstrationSpawns[selected];
        public Vector3 RecoveryAnchor => lastHealthyPosition;
        public Vector3 LastFallReturn { get; private set; }
        public Vector3 Target => build.manifest.defects[selected].center;
        public bool HealthyControl => healthy;
        public IReadOnlyList<InjectedCollisionDefect> Defects => build.manifest.defects;

        private void Start()
        {
            try
            {
                build = DefectScenarioBuilder.Build(protocol, protocol.scenarios[0], generator, adapter, true);
                foreach (var defect in build.manifest.defects)
                {
                    var surface = build.geometry.surfaces.Single(item => item.id == defect.surfaceId);
                    defectColliders.Add(surface.collider); damagedMeshes.Add(surface.collider.sharedMesh);
                    damagedEnabled.Add(surface.collider.enabled);
                    var baseline = new Mesh { name = "Demo healthy comparison " + healthyMeshes.Count,
                        indexFormat = surface.data.vertices.Length > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                    baseline.vertices = surface.data.vertices; baseline.triangles = surface.data.triangles; baseline.RecalculateBounds();
                    healthyMeshes.Add(baseline);
                }
                CreateGuides();
                if (instructions == null || status == null) CreateCanvas();
                RefreshInstructions();
                SelectDefect(initialDefect);
                Capture(true);
            }
            catch (Exception exception)
            {
                Error = exception.Message; Debug.LogException(exception);
                if (instructions == null || status == null) CreateCanvas();
                message = "Demo gagal dimuat: " + Error; Capture(false);
            }
        }

        public void SelectDefect(int index)
        {
            if (!Ready) return;
            selected = Mathf.Clamp(index, 0, build.manifest.defects.Count - 1);
            SetHealthyControl(false);
            ReturnToSafeSpawn();
            message = selected == 0 ? "Collider penuh dihapus. Coba berjalan ke kotak merah."
                : "Lubang collision 2,4 m. Coba pijak bagian lantai di luar dan di dalam kotak oranye.";
        }

        public void SetHealthyControl(bool value)
        {
            healthy = value; marked = false;
            for (int i = 0; i < defectColliders.Count; i++)
            {
                defectColliders[i].sharedMesh = value ? healthyMeshes[i] : damagedMeshes[i];
                defectColliders[i].enabled = value || damagedEnabled[i];
            }
            Physics.SyncTransforms();
            if (value) message = "PEMBANDING SEHAT: collider dipulihkan, visual sama. Coba berjalan melewati area yang tadi membuatmu jatuh.";
            RefreshGuides();
        }

        public void ReturnToSafeSpawn()
        {
            if (!Ready) return;
            Vector3 direction = Target - SafeSpawn;
            var yaw = Quaternion.LookRotation(new Vector3(direction.x, 0, direction.z));
            motor.ResetMotor(SafeSpawn, yaw); safeHeight = SafeSpawn.y;
            jumpQueued = false;
            float radius = motor.GetComponent<CharacterController>().radius + .45f;
            if (!CollisionRecoveryPoint.TryGet(build, generator, SafeSpawn, radius, out lastHealthyPosition)
                && !CollisionRecoveryPoint.TryNearestOnFloor(build, generator, SafeSpawn, radius, out lastHealthyPosition))
                throw new InvalidOperationException("No healthy tile near the demo approach.");
            lastHealthyRotation = yaw;
            Vector3 fromEye = Target - (SafeSpawn + Vector3.up * 1.6f);
            pitch = Mathf.Asin(-fromEye.normalized.y) * Mathf.Rad2Deg;
            UpdateCamera();
            skipMouse = true;
            movementGraceUntil = Time.realtimeSinceStartupAsDouble + .35;
        }

        private void RememberHealthyPosition()
        {
            float radius = motor.GetComponent<CharacterController>().radius + .45f;
            if (!CollisionRecoveryPoint.TryGet(build, generator, motor.transform.position, radius, out var position)) return;
            lastHealthyPosition = position;
            lastHealthyRotation = Quaternion.Euler(0, motor.transform.eulerAngles.y, 0);
        }

        private void ReturnToLastHealthyTile()
        {
            float radius = motor.GetComponent<CharacterController>().radius + .45f;
            if (!CollisionRecoveryPoint.TryGet(build, generator, lastHealthyPosition, radius, out var safe)
                && !CollisionRecoveryPoint.TryNearestOnFloor(build, generator, lastHealthyPosition, radius, out safe))
                throw new InvalidOperationException("No healthy tile on the last visited floor.");
            lastHealthyPosition = LastFallReturn = safe;
            motor.ResetMotor(safe, lastHealthyRotation);
            jumpQueued = false;
            safeHeight = safe.y;
            UpdateCamera(); skipMouse = true;
            movementGraceUntil = Time.realtimeSinceStartupAsDouble + .35;
        }

        public bool MarkAimedFloor()
        {
            if (!Ready) return false;
            if (!build.geometry.Cast(eye.ViewportPointToRay(new Vector3(.5f, .5f)), 6, out var hit)
                || !(hit.IsInspectableFloor || protocol.inspectWalls && hit.IsInspectableWall))
            { message = protocol.inspectWalls ? "Bidik lantai atau tembok dalam jarak 6 meter." : "Bidik permukaan lantai dalam jarak 6 meter."; return false; }
            if (DefectGeometry.CollisionPresent(hit))
            { message = "Permukaan sehat: collider masih menahan pemain. Tidak ditandai."; return false; }
            if (marked) { message = "Anomali ini sudah ditandai."; return true; }
            marked = true; message = "Anomali collider terkonfirmasi dan ditandai. Coba injak area ini untuk merasakan perbedaannya.";
            RefreshGuides(); return true;
        }

        private void Update()
        {
            if (!Ready) return;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) Capture(!captured);
                if (keyboard.digit1Key.wasPressedThisFrame) SelectDefect(0);
                if (keyboard.digit2Key.wasPressedThisFrame) SelectDefect(1);
                if (keyboard.digit3Key.wasPressedThisFrame) { SetHealthyControl(true); ReturnToSafeSpawn(); }
                if (keyboard.hKey.wasPressedThisFrame) { showGuides = !showGuides; RefreshGuides(); }
                if (keyboard.rKey.wasPressedThisFrame) ReturnToSafeSpawn();
            }
            if (!captured)
            {
                jumpQueued = false;
                motor.ClearParticipantJumpBuffer();
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) Capture(true);
                return;
            }
            Vector2 delta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            if (skipMouse) { skipMouse = false; delta = Vector2.zero; }
            motor.transform.Rotate(0, delta.x * mouseSensitivity, 0, Space.World);
            pitch = Mathf.Clamp(pitch - delta.y * mouseSensitivity, -85, 85);
            UpdateCamera();
            if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame
                && Time.realtimeSinceStartupAsDouble >= movementGraceUntil) jumpQueued = true;
            if (keyboard != null && keyboard.fKey.wasPressedThisFrame) MarkAimedFloor();
#endif
        }

        private void FixedUpdate()
        {
            if (!Ready || !captured) return;
            Vector2 input = Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard != null && Time.realtimeSinceStartupAsDouble >= movementGraceUntil)
                input = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
#endif
            motor.MoveParticipant(input, jumpQueued, Time.fixedDeltaTime);
            jumpQueued = false;
            if (motor.IsGrounded)
            {
                safeHeight = motor.transform.position.y;
                RememberHealthyPosition();
            }
            if (motor.transform.position.y < -10 || !motor.IsGrounded && motor.VerticalVelocity < -7
                && safeHeight - motor.transform.position.y > 1.5f)
            {
                Falls++; ReturnToLastHealthyTile();
                message = "Terjatuh karena defect. Kembali ke tile sehat terakhir sebelum jatuh. Bandingkan dengan mode 3 (sehat).";
            }
        }

        private void LateUpdate()
        {
            if (eye != null && motor != null) UpdateCamera();
            if (status != null)
                status.text = Error != null ? message : !Ready ? "Menyiapkan demo..."
                    : (healthy ? "MODE: LANTAI SEHAT" : selected == 0 ? "MODE: COLLIDER PENUH HILANG" : "MODE: LUBANG COLLISION SEBAGIAN")
                    + "\n" + message + "\nJatuh: " + Falls + "  •  " + (captured ? "Esc melepas kursor" : "Klik kiri untuk kembali bermain");
        }

        private void UpdateCamera()
        {
            eye.transform.SetPositionAndRotation(motor.transform.position + Vector3.up * 1.6f,
                Quaternion.Euler(pitch, motor.transform.eulerAngles.y, 0));
        }

        private void Capture(bool value)
        {
            captured = value; skipMouse = true;
            if (Application.isBatchMode) return;
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None; Cursor.visible = !value;
        }
        private void OnApplicationFocus(bool value) { if (!value && !Application.isBatchMode) Capture(false); }

        private void CreateGuides()
        {
            guideRoot = new GameObject("Demo guides (no colliders)");
            guideMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            guideMaterial.SetColor("_BaseColor", Color.white);
            for (int i = 0; i < build.manifest.defects.Count; i++)
            {
                var defect = build.manifest.defects[i];
                var surface = build.geometry.surfaces.Single(item => item.id == defect.surfaceId);
                var line = new GameObject("Guide " + defect.kind).AddComponent<LineRenderer>();
                line.transform.SetParent(guideRoot.transform);
                var material = new Material(guideMaterial); guideMaterials.Add(material); line.sharedMaterial = material;
                line.useWorldSpace = true; line.loop = true; line.positionCount = 4;
                line.widthMultiplier = .065f;
                float halfX = defect.kind == CollisionDefectKind.PartialHole ? defect.width / 2 : surface.renderer.bounds.extents.x;
                float halfZ = defect.kind == CollisionDefectKind.PartialHole ? defect.width / 2 : surface.renderer.bounds.extents.z;
                var center = defect.kind == CollisionDefectKind.PartialHole ? defect.center
                    : new Vector3(surface.renderer.bounds.center.x, defect.center.y, surface.renderer.bounds.center.z);
                center += Vector3.up * .045f;
                line.SetPositions(new[] { center + new Vector3(-halfX, 0, -halfZ), center + new Vector3(halfX, 0, -halfZ),
                    center + new Vector3(halfX, 0, halfZ), center + new Vector3(-halfX, 0, halfZ) });
                guides.Add(line);
            }
            RefreshGuides();
        }

        private void RefreshGuides()
        {
            for (int i = 0; i < guides.Count; i++)
            {
                Color color = healthy ? Color.cyan : marked && i == selected ? Color.green
                    : i == 0 ? new Color(1, .22f, .22f) : new Color(1, .65f, .12f);
                guides[i].startColor = guides[i].endColor = color;
                guideMaterials[i].SetColor("_BaseColor", color);
                guides[i].enabled = showGuides;
            }
        }

        public void CreateCanvas()
        {
            var root = new GameObject("Demo Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
            instructions = HudText(root.transform, "Instructions", new Vector2(24, -24), new Vector2(-24, -255), true);
            RefreshInstructions();
            status = HudText(root.transform, "Status", new Vector2(24, 24), new Vector2(-24, 214), false);
            var crosshair = new GameObject("Crosshair", typeof(RectTransform), typeof(Text)); crosshair.transform.SetParent(root.transform, false);
            var rect = crosshair.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = new Vector2(40, 40);
            var text = crosshair.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = "+"; text.fontSize = 30; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.raycastTarget = false;
        }

        private void RefreshInstructions()
        {
            instructions.text = "SANDBOX COLLIDER • MAP WFC KECIL 6 × 2 × 6 • DEMO, BUKAN TRIAL PESERTA\n"
                + "WASD bergerak • Space lompat • Mouse melihat • F memeriksa dan menandai collision\n"
                + "1: dekat collider penuh hilang (merah) • 2: dekat lubang sebagian (oranye)\n"
                + "3: pulihkan collider untuk pembanding sehat • H: sembunyikan/tampilkan garis bantu\n"
                + "R: kembali ke pijakan aman • Esc: lepas/kunci kursor";
        }

        private static Text HudText(Transform root, string name, Vector2 min, Vector2 max, bool top)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image)); panel.transform.SetParent(root, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = new Vector2(0, top ? 1 : 0); rect.anchorMax = new Vector2(1, top ? 1 : 0);
            rect.offsetMin = top ? new Vector2(min.x, max.y) : min; rect.offsetMax = top ? new Vector2(max.x, min.y) : max;
            panel.GetComponent<Image>().color = new Color(.025f, .045f, .075f, .94f); panel.GetComponent<Image>().raycastTarget = false;
            var label = new GameObject("Text", typeof(RectTransform), typeof(Text)); label.transform.SetParent(panel.transform, false);
            rect = label.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(20, 16); rect.offsetMax = new Vector2(-20, -16);
            var text = label.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = 26;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 17; text.resizeTextMaxSize = 26;
            text.color = Color.white; text.raycastTarget = false; text.alignment = TextAnchor.MiddleLeft; return text;
        }

        public static bool SafeFootprint(Vector3 point, float radius)
        {
            foreach (var offset in new[] { Vector3.zero, Vector3.left * radius, Vector3.right * radius, Vector3.forward * radius, Vector3.back * radius })
                if (!DefectGeometry.Supported(point + offset)) return false;
            return true;
        }

        public static Vector3 FindApproach(DefectScenarioBuild map, InjectedCollisionDefect defect, float radius)
        {
            Vector3 best = default; float score = float.MaxValue;
            foreach (var floor in map.coverageCells.Values.Where(p => Mathf.Abs(p.y - defect.center.y) < .2f))
                for (int step = 0; step < 10; step++)
                {
                    var point = Vector3.Lerp(floor, defect.center, step / 10f);
                    float distance = Vector3.Distance(point, defect.center);
                    if (distance < 1.8f || distance > 5.5f || !SafeFootprint(point, radius + .3f)) continue;
                    Vector3 eye = point + Vector3.up * 1.68f;
                    if (!map.geometry.Cast(new Ray(eye, (defect.center - eye).normalized), 6, out var hit)
                        || hit.surface.id != defect.surfaceId || !hit.IsInspectableFloor) continue;
                    float desired = defect.kind == CollisionDefectKind.FullCollider ? 5f : 2.5f;
                    float candidateScore = Mathf.Abs(distance - desired);
                    if (candidateScore >= score) continue;
                    best = point + Vector3.up * .08f; score = candidateScore;
                }
            if (score == float.MaxValue) throw new InvalidOperationException("No safe nearby approach for " + defect.kind);
            return best;
        }

        private void OnDrawGizmosSelected()
        {
            if (protocol == null || protocol.scenarios.Count == 0 || string.IsNullOrEmpty(protocol.scenarios[0].frozenManifest)) return;
            var manifest = JsonUtility.FromJson<DefectScenarioManifest>(protocol.scenarios[0].frozenManifest);
            foreach (var defect in manifest.defects)
            {
                Gizmos.color = defect.kind == CollisionDefectKind.FullCollider ? Color.red : new Color(1, .65f, .1f);
                float width = defect.kind == CollisionDefectKind.FullCollider ? 8 : defect.width;
                Gizmos.DrawWireCube(defect.center + Vector3.up * .04f, new Vector3(width, .08f, width));
            }
        }

        private void OnDestroy()
        {
            build?.Dispose();
            foreach (var mesh in healthyMeshes) if (mesh != null) Destroy(mesh);
            if (guideRoot != null) Destroy(guideRoot);
            foreach (var material in guideMaterials) if (material != null) Destroy(material);
            if (guideMaterial != null) Destroy(guideMaterial);
            if (Application.isPlaying) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        }
    }
}
