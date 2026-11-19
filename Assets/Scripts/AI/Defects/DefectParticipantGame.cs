using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace TesisQA.AI
{
    // Presentation for the participant-only scene. The frozen geometry and scorer
    // are shared with the researcher scene; unfound target positions stay hidden.
    public sealed partial class DefectExperimentSession
    {
        [Serializable]
        private sealed class ParticipantSettings
        {
            public string googleFormUrl = "https://forms.gle/D6iTTSbCAvPstjng9";
            public int scenarioOrder;
        }

        [Header("Participant first-person controls")]
        [Range(.01f, 1f)] public float mouseSensitivity = .12f;
        private bool participantPaused;
        private System.Diagnostics.Stopwatch pauseClock;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool suppressNextMouseDelta;
        private Vector3 participantSafePosition;
        private Quaternion participantSafeRotation;
        private double participantRecoveryUntil;
        private bool participantJumpQueued;
        private bool participantSmoke;
        private double smokeTransition;
        private int smokeObservedCompleted = -1;

        public bool ParticipantFinished => finished;
        public int ParticipantCompletedTrials => completed.Count;
        public string ParticipantResultsText => BuildParticipantResults();
        public bool ParticipantPaused => participantPaused;
        public bool ParticipantStarted => campaignStarted;
        public bool ParticipantRespawning => pendingRespawn;
        public string ParticipantError => error;
        public int ParticipantTrialNumber => ordinal + 1;
        public float ParticipantRemainingSeconds => Mathf.Max(0, (smokeDurationOverride > 0 ? smokeDurationOverride : protocol != null ? protocol.durationSeconds : 150f) - WallSeconds);
        public float ParticipantTotalSeconds => completed.Sum(t => t.wallSeconds);
        public string ParticipantFeedback => Time.realtimeSinceStartupAsDouble < feedbackUntil ? feedback : "";
        public System.Collections.Generic.IReadOnlyList<DefectTrialReport> ParticipantTrials => completed;
        public System.Collections.Generic.IReadOnlyList<DefectFinding> ParticipantFindings => current?.findings;
        public System.Collections.Generic.IReadOnlyList<DefectMarkAttempt> ParticipantMarkAttempts => current?.markAttempts;
        public int ParticipantTargetCount => current?.manifest?.defects.Count ?? 0;
        public int ParticipantMarkedCount => current?.findings.Count ?? 0;
        public int ParticipantFoundTargetCount => CountFoundParticipantTargets(current);
        public bool ParticipantAllTargetsFound => ParticipantTargetCount > 0 && ParticipantFoundTargetCount == ParticipantTargetCount;

        private static int CountFoundParticipantTargets(DefectTrialReport trial) => trial?.manifest == null ? 0
            : trial.manifest.defects.Count(defect => trial.findings.Any(finding => finding.truePositive
                && finding.matchedDefectId == defect.id));

        private void InitializeParticipantGame()
        {
            // A dedicated participant build cannot enter AI mode through CLI args.
            mode = DefectExperimentMode.Manual;
            participant = "anonymous";
            autoStart = false;
            automaticTimeScale = 1f;
            var settingsPath = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "ParticipantSettings.json");
            if (File.Exists(settingsPath))
            {
                try
                {
                    var settings = JsonUtility.FromJson<ParticipantSettings>(File.ReadAllText(settingsPath));
                    if (settings != null)
                    {
                        if (ValidParticipantFormUrl(settings.googleFormUrl)) googleFormUrl = settings.googleFormUrl;
                        participantOrder = Mathf.Clamp(settings.scenarioOrder, 0, 9);
                    }
                }
                catch (Exception exception) { Debug.LogWarning("Participant settings ignored: " + exception.Message); }
            }
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            cameraPitch = 15f;
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
            participantSmoke = Application.isBatchMode && Environment.GetCommandLineArgs().Contains("-participantSmoke");
            if (participantSmoke)
            {
                participant = "SOFTWARE_CHECK_NOT_A_PARTICIPANT";
                smokeDurationOverride = 2f;
            }
        }

        private void UpdateParticipantSmoke()
        {
            if (!participantSmoke || current == null) return;
            if (!campaignStarted) { StartParticipantGame(); return; }
            if (participantPaused) return;
            if (Running)
            {
                // Editor checks queue real keyboard F. A standalone batch smoke
                // run exercises the same aim/report API at known test locations.
                if (!Application.isEditor)
                {
                    int wanted = ordinal < 2 ? ParticipantTargetCount : ordinal == 2 ? 1 : 0;
                    if (ParticipantFoundTargetCount < wanted)
                    {
                        var target = current.manifest.defects[ParticipantFoundTargetCount];
                        agent.Motor.ResetMotor(target.center + Vector3.up * .08f, Quaternion.identity);
                        ApplyParticipantLook(new Vector2(0, (cameraPitch - 85) / mouseSensitivity));
                        ReportAim();
                    }
                }
                return;
            }
            if (pendingRespawn) return;
            if (smokeObservedCompleted != completed.Count)
            {
                smokeObservedCompleted = completed.Count;
                smokeTransition = Time.realtimeSinceStartupAsDouble;
            }
            if (Time.realtimeSinceStartupAsDouble - smokeTransition < .2) return;
            if (Mathf.Abs(WallSeconds - completed.Last().wallSeconds) > .001f)
                throw new InvalidOperationException("Intermission clock did not stop.");
            AdvanceParticipantTrial();
        }

        private void ValidateParticipantSmoke()
        {
            if (completed.Count != 5
                || completed.Take(2).Any(t => t.status != "complete" || t.finishReason != "all_defects_found"
                    || CountFoundParticipantTargets(t) != t.manifest.defects.Count)
                || completed.Skip(2).Any(t => t.status != "failed" || t.finishReason != "time_limit"
                    || CountFoundParticipantTargets(t) >= t.manifest.defects.Count)
                || completed.Any(t => t.wallSeconds <= 0 || t.wallSeconds > 10)
                || mode != DefectExperimentMode.Manual || Time.timeScale != 1f
                || !ValidParticipantFormUrl(googleFormUrl)
                || ValidParticipantFormUrl("https://example.com/forms/")
                || ValidParticipantFormUrl("javascript:alert(1)"))
                throw new InvalidOperationException("Participant five-trial workflow validation failed.");
            if (File.ReadAllLines(Path.Combine(outputDirectory, "Waktu_Trial.csv")).Length != 6)
                throw new InvalidOperationException("Participant timing export does not contain five trials.");
            File.WriteAllText(Path.Combine(outputDirectory, "participant-checks.txt"),
                "PASS: five trials; first two finish automatically only after all targets; remaining three timeouts are failed attempts; intermission clock stopped; immediate final results; five-row time/status export; Google Form URL allowlist; time scale 1.\nSoftware check only; not human participant data.\n");
        }

        public static bool ValidParticipantFormUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https"
                && (uri.Host.Equals("forms.gle", StringComparison.OrdinalIgnoreCase)
                    || uri.Host.Equals("docs.google.com", StringComparison.OrdinalIgnoreCase)
                       && uri.AbsolutePath.StartsWith("/forms/", StringComparison.Ordinal));
        }

        public void StartParticipantGame()
        {
            if (!participantPresentation) return;
            StartCampaign();
            suppressNextMouseDelta = true;
            RefreshParticipantCursor();
        }

        public void CompleteParticipantTrial()
        {
            if (!participantPresentation || !Running || current == null) return;
            if (!ParticipantAllTargetsFound)
            { Feedback("Temukan seluruh anomali terlebih dahulu. Trial selesai otomatis saat target lengkap."); return; }
            current.finishReason = "all_defects_found";
            FinishTrial("complete");
        }

        private bool FinishParticipantTimeoutIfNeeded()
        {
            float limit = smokeDurationOverride > 0 ? smokeDurationOverride : protocol.durationSeconds;
            if (!participantPresentation || !Running || participantPaused || WallSeconds < limit) return false;
            current.finishReason = "time_limit";
            FinishTrial("failed");
            return true;
        }

        public void AdvanceParticipantTrial()
        {
            if (!participantPresentation || Running || pendingRespawn || finished || !campaignStarted) return;
            NextTrial();
            suppressNextMouseDelta = true;
            RefreshParticipantCursor();
        }

        private void Start()
        {
            if (participantPresentation && FindAnyObjectByType<ParticipantCanvasUI>() == null)
                ParticipantCanvasUI.Create(this);
        }

        public Vector2 ReadParticipantMovement()
        {
            if (!Running || participantPaused || Time.realtimeSinceStartupAsDouble < participantRecoveryUntil) return Vector2.zero;
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return Vector2.zero;
            return Vector2.ClampMagnitude(new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0)), 1f);
#else
            return Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
#endif
        }

        public bool ConsumeParticipantJump()
        {
            bool requested = participantJumpQueued && participantPresentation && Running && !participantPaused
                && !pendingRespawn && Time.realtimeSinceStartupAsDouble >= participantRecoveryUntil;
            participantJumpQueued = false;
            return requested;
        }

        private void RecordParticipantMark(DefectVisualHit hit, string reason, bool accepted)
        {
            current.markAttempts.Add(new DefectMarkAttempt { surfaceId = hit.surface?.id, position = hit.point,
                reason = reason, accepted = accepted, simulationSeconds = current.simulationSeconds, wallSeconds = WallSeconds });
        }

        private void ReportParticipantAnomaly(DefectVisualHit hit)
        {
            if (FinishParticipantTimeoutIfNeeded()) return;
            // This gate uses the visible mesh and physical support, with no access
            // to injection labels. A healthy floor never creates a green marker.
            if (DefectGeometry.CollisionPresent(hit))
            {
                RecordParticipantMark(hit, hit.IsInspectableWall ? "supported_wall" : "supported_floor", false);
                Feedback(hit.IsInspectableWall ? "Tembok ini memiliki collider. Lokasi tidak ditandai."
                    : "Lantai ini memiliki collider. Lokasi tidak ditandai.");
                return;
            }
            if (current.findings.Exists(finding => finding.surfaceId == hit.surface.id))
            {
                current.duplicates++;
                RecordParticipantMark(hit, "already_marked", false);
                Feedback("Anomali ini sudah ditandai.");
                return;
            }
            RecordParticipantMark(hit, hit.IsInspectableWall ? "missing_wall_collision" : "missing_collision", true);
            AddFinding(hit, "manual_validated_anomaly");
            Feedback("Anomali collider berhasil ditandai.");
            if (ParticipantAllTargetsFound) CompleteParticipantTrial();
        }

        // Bounds are presentation feedback AFTER an accepted F report. They are
        // never used to accept a report, steer the player, or expose other targets.
        public Vector3[] GetParticipantHighlightOutline(DefectFinding finding)
        {
            if (build == null || current == null || finding == null || !current.findings.Contains(finding)) return null;
            var surface = build.geometry.surfaces.FirstOrDefault(item => item.id == finding.surfaceId);
            if (surface?.renderer == null) return null;
            var bounds = surface.renderer.bounds;
            if (finding.surfaceKind == "wall")
            {
                Vector3 normal = finding.normal.normalized;
                Vector3 offset = normal * .045f;
                if (Mathf.Abs(normal.x) > Mathf.Abs(normal.z))
                    return new[] {
                        new Vector3(finding.position.x, bounds.min.y, bounds.min.z) + offset,
                        new Vector3(finding.position.x, bounds.min.y, bounds.max.z) + offset,
                        new Vector3(finding.position.x, bounds.max.y, bounds.max.z) + offset,
                        new Vector3(finding.position.x, bounds.max.y, bounds.min.z) + offset };
                return new[] {
                    new Vector3(bounds.min.x, bounds.min.y, finding.position.z) + offset,
                    new Vector3(bounds.max.x, bounds.min.y, finding.position.z) + offset,
                    new Vector3(bounds.max.x, bounds.max.y, finding.position.z) + offset,
                    new Vector3(bounds.min.x, bounds.max.y, finding.position.z) + offset };
            }
            var defect = build.manifest.defects.FirstOrDefault(item => item.id == finding.matchedDefectId
                && item.surfaceId == finding.surfaceId);
            bool hole = defect != null && defect.kind == CollisionDefectKind.PartialHole;
            Vector3 center = hole ? defect.center : new Vector3(bounds.center.x,
                defect != null ? defect.center.y : surface.tile.position.y, bounds.center.z);
            float halfX = hole ? defect.width / 2 : bounds.extents.x;
            float halfZ = hole ? defect.width / 2 : bounds.extents.z;
            center += Vector3.up * .045f;
            return new[] {
                center + new Vector3(-halfX, 0, -halfZ), center + new Vector3(halfX, 0, -halfZ),
                center + new Vector3(halfX, 0, halfZ), center + new Vector3(-halfX, 0, halfZ) };
        }

        private void InitializeParticipantRecovery(Vector3 spawn, Quaternion rotation)
        {
            participantSafePosition = spawn;
            participantSafeRotation = rotation;
            participantRecoveryUntil = 0;
            participantJumpQueued = false;
        }

        private bool TryParticipantSafeFloor(Vector3 feet, out Vector3 safeFeet)
        {
            float radius = agent.GetComponent<CharacterController>().radius + .45f;
            return CollisionRecoveryPoint.TryGet(build, generator, feet, radius, out safeFeet);
        }

        private void RememberParticipantSafePosition()
        {
            if (!TryParticipantSafeFloor(agent.transform.position, out var position)) return;
            participantSafePosition = position;
            participantSafeRotation = Quaternion.Euler(0, agent.transform.eulerAngles.y, 0);
        }

        private void RecoverParticipantFromFall()
        {
            if (!TryParticipantSafeFloor(participantSafePosition, out var safe))
            {
                float radius = agent.GetComponent<CharacterController>().radius + .45f;
                if (!CollisionRecoveryPoint.TryNearestOnFloor(build, generator, participantSafePosition, radius, out safe))
                    throw new InvalidOperationException("No healthy recovery tile remains on the previous floor.");
            }
            participantSafePosition = safe;
            participantJumpQueued = false;
            agent.Motor.ResetMotor(safe, participantSafeRotation);
            previousPosition = safe;
            lastSafeHeight = safe.y;
            // A short input grace period prevents a held W from immediately
            // walking off the same edge, while looking and F remain available.
            participantRecoveryUntil = Time.realtimeSinceStartupAsDouble + .35;
        }

        public void ApplyParticipantLook(Vector2 delta)
        {
            if (!Running || participantPaused || !participantPresentation) return;
            agent.transform.Rotate(0, delta.x * mouseSensitivity, 0, Space.World);
            cameraPitch = Mathf.Clamp(cameraPitch - delta.y * mouseSensitivity, -85, 85);
            // F reports the exact view shown after this frame's mouse movement.
            viewCamera.transform.SetPositionAndRotation(agent.transform.position + Vector3.up * 1.6f,
                Quaternion.Euler(cameraPitch, agent.transform.eulerAngles.y, 0));
        }

        public void SetParticipantPaused(bool paused)
        {
            participantJumpQueued = false;
            if (agent != null && agent.Motor != null) agent.Motor.ClearParticipantJumpBuffer();
            if (!participantPresentation || !Running || participantPaused == paused) return;
            if (paused)
            {
                participantPaused = true;
                trialClock?.Stop();
                pauseClock = System.Diagnostics.Stopwatch.StartNew();
                if (current != null) current.pauseCount++;
            }
            else
            {
                EndParticipantPause();
                trialClock?.Start();
                suppressNextMouseDelta = true;
            }
            RefreshParticipantCursor();
        }

        private void EndParticipantPause()
        {
            if (pauseClock != null)
            {
                pauseClock.Stop();
                if (current != null) current.pausedWallSeconds += (float)pauseClock.Elapsed.TotalSeconds;
                pauseClock = null;
            }
            participantPaused = false;
        }

        public void RefreshParticipantCursor()
        {
            if (!participantPresentation || Application.isBatchMode) return;
            bool capture = Running && !participantPaused && !finished && string.IsNullOrEmpty(error);
            Cursor.lockState = capture ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !capture;
        }

        private void RestoreParticipantCursor()
        {
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
        }

        private void OnApplicationFocus(bool focused)
        {
            if (participantPresentation && !focused && Running) SetParticipantPaused(true);
        }

        private void UpdateParticipantControls()
        {
            if (!Running) { participantJumpQueued = false; RefreshParticipantCursor(); return; }
            if (FinishParticipantTimeoutIfNeeded()) return;
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetParticipantPaused(!participantPaused);
            if (participantPaused) return;
            if (!Application.isBatchMode && Cursor.lockState != CursorLockMode.Locked)
            { SetParticipantPaused(true); return; }
            Vector2 delta = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
            if (suppressNextMouseDelta) { suppressNextMouseDelta = false; delta = Vector2.zero; }
            ApplyParticipantLook(delta);
            if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame
                && Time.realtimeSinceStartupAsDouble >= participantRecoveryUntil) participantJumpQueued = true;
            if (Keyboard.current != null && Keyboard.current.fKey.wasPressedThisFrame) ReportAim();
#else
            if (Input.GetKeyDown(KeyCode.Space) && !participantPaused
                && Time.realtimeSinceStartupAsDouble >= participantRecoveryUntil) participantJumpQueued = true;
#endif
        }

        public void CopyParticipantTrialTime(int trialIndex)
        {
            if (trialIndex < 0 || trialIndex >= ParticipantCompletedTrials) return;
            var trial = ParticipantTrials[trialIndex];
            GUIUtility.systemCopyBuffer = trial.status == "complete" ? Clock(trial.wallSeconds) : "-";
            Feedback("Waktu trial " + (trialIndex + 1) + " disalin. Tempel dengan Ctrl+V di formulir.");
        }

        public void OpenParticipantForm()
        {
            if (finished && ValidParticipantFormUrl(googleFormUrl)) Application.OpenURL(googleFormUrl);
        }

        public static void QuitParticipantGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private string BuildParticipantResults()
        {
            var text = new StringBuilder("HASIL LIMA TRIAL\n");
            foreach (var trial in completed)
                text.Append("Trial ").Append(trial.order).Append(": ")
                    .Append(Clock(trial.wallSeconds)).Append(" (")
                    .Append(trial.wallSeconds.ToString("0.00", CultureInfo.InvariantCulture)).Append(" detik) - ")
                    .Append(trial.status == "complete" ? "BERHASIL" : "GAGAL (waktu habis)")
                    .Append("; defect ").Append(CountFoundParticipantTargets(trial)).Append('/')
                    .Append(trial.manifest.defects.Count).Append('\n');
            text.Append("Total waktu bermain: ").Append(Clock(completed.Sum(t => t.wallSeconds))).Append('\n');
            return text.ToString();
        }

        private void WriteParticipantResults()
        {
            if (!ownsOutput) return;
            File.WriteAllText(Path.Combine(outputDirectory, "Waktu_Trial.txt"), BuildParticipantResults());
            var text = new StringBuilder("trial,elapsed_seconds,finish_reason,reports,paused_seconds,pause_count,status,found_targets,target_count\n");
            foreach (var trial in completed)
                text.Append(trial.order).Append(',').Append(trial.wallSeconds.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(trial.finishReason).Append(',').Append(trial.findings.Count)
                    .Append(',').Append(trial.pausedWallSeconds.ToString("R", CultureInfo.InvariantCulture))
                    .Append(',').Append(trial.pauseCount).Append(',').Append(trial.status)
                    .Append(',').Append(CountFoundParticipantTargets(trial)).Append(',').Append(trial.manifest.defects.Count).Append('\n');
            File.WriteAllText(Path.Combine(outputDirectory, "Waktu_Trial.csv"), text.ToString());
        }

        public static string Clock(float seconds)
        {
            var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return ((int)span.TotalMinutes).ToString("00") + ":" + span.Seconds.ToString("00")
                + "." + (span.Milliseconds / 10).ToString("00");
        }

    }
}
