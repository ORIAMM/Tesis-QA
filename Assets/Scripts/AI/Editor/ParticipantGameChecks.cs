using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace TesisQA.AI.Editor
{
    [InitializeOnLoad]
    public static class ParticipantGameChecks
    {
        private const string Active = "ParticipantCanvas.Checks.Active", Output = "ParticipantCanvas.Checks.Output";
        private static bool checkedControls, checkedPause;
        private static double pausedAt;
        private static float remainingAtPause;
        private static Vector3 positionAtPause;
        private static Keyboard reportKeyboard;
        private static bool waitingForReport;
        private static int reportsBefore;
        private static double reportQueuedAt;
        private static float pitchBeforeReport;
        private static DefectTrialReport queuedReport;
        private static bool checkedDuplicate;
        private static bool linearChecks;
        private static bool checkedRecovery;

        static ParticipantGameChecks()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Changed;
        }

        public static void Run()
        {
            if (!Application.isBatchMode || !Application.dataPath.Contains(".ai-eval"))
                throw new InvalidOperationException("Use isolated batch Editor for software checks.");
            string[] args = Environment.GetCommandLineArgs(); string output = null;
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "-defectOutput") output = Path.GetFullPath(args[i + 1]);
            if (output == null || Directory.Exists(output)) throw new InvalidOperationException("Use a new check output directory.");
            if (args.Contains("-linearTrials")) LinearCollisionSetup.Prepare();
            ParticipantGameBuild.PrepareCanvas();
            string layout = ValidateLayout();
            ValidateMotor();
            string jump = ParticipantJumpChecks.Validate();
            EditorSceneManager.OpenScene(ParticipantGameBuild.ScenePath);
            var session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
            session.outputDirectory = output;
            SessionState.SetString(Output, output); SessionState.SetBool(Active, true);
            SessionState.SetString(Output + ".Layout", layout);
            SessionState.SetString(Output + ".Jump", jump);
            EditorApplication.EnterPlaymode();
        }

        private static string ValidateLayout()
        {
            var ui = UnityEngine.Object.FindAnyObjectByType<ParticipantCanvasUI>();
            var canvas = ui.GetComponent<Canvas>(); var scaler = ui.GetComponent<CanvasScaler>();
            Check(canvas.renderMode == RenderMode.ScreenSpaceOverlay, "Canvas is not screen-space overlay.");
            Check(scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize && scaler.referenceResolution == new Vector2(1920, 1080), "Canvas scaling configuration differs.");
            Check(UnityEngine.Object.FindAnyObjectByType<UnityEngine.InputSystem.UI.InputSystemUIInputModule>() != null, "UI has no Input System event module.");
            scaler.enabled = false; canvas.renderMode = RenderMode.WorldSpace;
            foreach (Transform page in ui.transform) page.gameObject.SetActive(true);
            var report = new StringBuilder("PASS: real Canvas, CanvasScaler 1920x1080, Input System UI module.\n");
            var sizes = new[] { new Vector2(1280, 720), new Vector2(1920, 1080), new Vector2(1024, 768), new Vector2(2560, 1080), new Vector2(720, 1280) };
            foreach (var size in sizes)
            foreach (bool showGuide in new[] { false, true })
            {
                float scale = Mathf.Sqrt(size.x / 1920f * size.y / 1080f);
                var root = (RectTransform)ui.transform; root.sizeDelta = size / scale;
                ui.ShowGuide(showGuide);
                for (int step = 0; step < 4; step++)
                {
                    Canvas.ForceUpdateCanvases(); ui.RefreshLayout();
                    foreach (Transform page in ui.transform) LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)page);
                }
                foreach (var button in ui.GetComponentsInChildren<Button>())
                {
                    var rect = (RectTransform)button.transform;
                    Check(rect.rect.height >= 46, "Button too short at " + size + ": " + button.name + " " + rect.rect);
                    CheckInside(rect, root, size);
                }
                var copyButtons = ui.GetComponentsInChildren<Button>().Where(button => button.name.StartsWith("Copy trial ")).ToArray();
                Check(copyButtons.Length == 5, "Result table must contain five per-trial Copy buttons.");
                var firstCopy = (RectTransform)copyButtons[0].transform;
                foreach (var button in copyButtons)
                {
                    var rect = (RectTransform)button.transform;
                    Check(Mathf.Abs(rect.rect.width - firstCopy.rect.width) < .1f
                        && Mathf.Abs(rect.anchoredPosition.x - firstCopy.anchoredPosition.x) < .1f,
                        "Copy buttons are not aligned at " + size);
                    var time = (RectTransform)rect.parent.GetChild(1);
                    Check(rect.anchoredPosition.x > time.anchoredPosition.x, "Copy button is not to the right of its trial time.");
                }
                foreach (var label in ui.GetComponentsInChildren<Text>())
                {
                    var rect = (RectTransform)label.transform;
                    Check(rect.rect.height >= 20, "Text collapsed at " + size + ": " + label.name + " " + rect.rect);
                    CheckInside(rect, (RectTransform)rect.parent, size);
                }
                foreach (var grid in ui.GetComponentsInChildren<GridLayoutGroup>())
                    foreach (RectTransform cell in grid.transform) CheckInside(cell, (RectTransform)grid.transform, size);
                if (showGuide)
                {
                    var images = ui.transform.Find("Main menu/Defect type examples").GetComponentsInChildren<Image>();
                    Check(images.Count(item => item.sprite != null) == 2, "Guide did not include both reference screenshots.");
                }
                report.AppendLine(size.x + "x" + size.y + (showGuide ? " guide" : " menu")
                    + ": buttons inside viewport; labels/cells inside containers; no collapsed controls.");
            }
            return report.ToString();
        }

        private static void CheckInside(RectTransform child, RectTransform parent, Vector2 size)
        {
            var corners = new Vector3[4]; child.GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var local = parent.InverseTransformPoint(corner); var bounds = parent.rect;
                Check(local.x >= bounds.xMin - 1 && local.x <= bounds.xMax + 1 && local.y >= bounds.yMin - 1 && local.y <= bounds.yMax + 1,
                    "UI overflow at " + size + ": " + child.name + " in " + parent.name + " " + local + " bounds=" + bounds);
            }
        }

        private static void ValidateMotor()
        {
            var probe = new GameObject("Temporary movement validation", typeof(CharacterController), typeof(AICharacterMotor));
            try
            {
                var motor = probe.GetComponent<AICharacterMotor>();
                var origin = new Vector3(10000, 0, 10000);
                motor.ResetMotor(origin, Quaternion.identity); motor.MovePlanar(Vector2.right, .1f);
                Vector3 offset = probe.transform.position - origin;
                Check(Mathf.Abs(offset.x - .4f) < .01f && Mathf.Abs(offset.z) < .01f && Mathf.Abs(probe.transform.eulerAngles.y) < .01f, "D must strafe without turning.");
                motor.ResetMotor(origin, Quaternion.identity); motor.MovePlanar(Vector2.one, .1f);
                offset = probe.transform.position - origin;
                Check(Mathf.Abs(new Vector2(offset.x, offset.z).magnitude - .4f) < .01f, "Diagonal movement is faster.");
                motor.ResetMotor(origin, Quaternion.Euler(0, 90, 0)); motor.MovePlanar(Vector2.up, .1f);
                offset = probe.transform.position - origin;
                Check(Mathf.Abs(offset.x - .4f) < .01f && Mathf.Abs(offset.z) < .01f, "W does not follow camera/body yaw.");
                motor.ResetMotor(origin, Quaternion.identity); motor.Move(1, 1, .1f);
                Check(Mathf.Abs(probe.transform.eulerAngles.y - 18) < .01f, "Original policy forward/turn motor changed.");
            }
            finally { UnityEngine.Object.DestroyImmediate(probe); }
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(Active, false) || !EditorApplication.isPlaying) return;
            var session = UnityEngine.Object.FindAnyObjectByType<DefectExperimentSession>();
            if (session == null || !session.ParticipantStarted) return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                if (!checkedControls && session.Running)
                {
                    linearChecks = session.protocol.fixedScenarioOrder;
                    typeof(DefectExperimentSession).GetField("participantSmoke", flags).SetValue(session, false);
                    session.smokeDurationOverride = 10;
                    ValidateInputAndLook(session);
                    pitchBeforeReport = Mathf.DeltaAngle(0, session.viewCamera.transform.eulerAngles.x);
                    session.ApplyParticipantLook(new Vector2(0, (pitchBeforeReport - 85) / session.mouseSensitivity));
                    session.ReportAim();
                    Check(session.ParticipantMarkedCount == 0 && session.ParticipantMarkAttempts.Last().reason == "supported_floor",
                        "Healthy floor was accepted.");
                    session.CompleteParticipantTrial();
                    Check(session.Running && session.ParticipantCompletedTrials == 0, "Incomplete trial was manually completed.");
                    var canvas = UnityEngine.Object.FindAnyObjectByType<ParticipantCanvasUI>(); canvas.Refresh();
                    Check(canvas.WorldHighlightCount == 0, "Unfound/healthy defect received a highlight.");
                    Check(!canvas.GetComponentsInChildren<Button>(true).Any(button => button.gameObject.activeSelf
                        && button.GetComponentInChildren<Text>().text == "Selesai trial"), "Manual finish button still exists.");
                    ValidateRecovery(session); checkedRecovery = true; checkedControls = true;
                }
                if (!checkedControls) return;
                if (!checkedPause)
                {
                    if (pausedAt == 0)
                    {
                        session.SetParticipantPaused(true);
                        remainingAtPause = session.ParticipantRemainingSeconds;
                        positionAtPause = session.agent.transform.position;
                        pausedAt = EditorApplication.timeSinceStartup;
                    }
                    else if (EditorApplication.timeSinceStartup - pausedAt >= .2)
                    {
                        Check(Mathf.Abs(session.ParticipantRemainingSeconds - remainingAtPause) < .001f, "Pause did not stop timer.");
                        Check(Vector3.Distance(positionAtPause, session.agent.transform.position) < .001f, "Player moved while paused.");
                        Check(session.ReadParticipantMovement() == Vector2.zero, "Pause allowed movement.");
                        session.SetParticipantPaused(false); checkedPause = true;
                        session.smokeDurationOverride = 2;
                        typeof(DefectExperimentSession).GetField("participantSmoke", flags).SetValue(session, true);
                    }
                    return;
                }
                if (waitingForReport)
                {
                    if (queuedReport.findings.Count > reportsBefore)
                    {
                        InputSystem.RemoveDevice(reportKeyboard); reportKeyboard = null; waitingForReport = false;
                        Check(queuedReport.findings.Last().truePositive, "F did not match the injected target.");
                        int found = queuedReport.manifest.defects.Count(defect => queuedReport.findings.Any(f => f.truePositive && f.matchedDefectId == defect.id));
                        if (found == queuedReport.manifest.defects.Count)
                        {
                            Check(session.ParticipantTrials.Contains(queuedReport) && queuedReport.status == "complete"
                                && queuedReport.finishReason == "all_defects_found", "Last F did not finish trial automatically.");
                            float lastHit = queuedReport.findings.Where(f => f.truePositive).Max(f => f.wallSeconds);
                            Check(queuedReport.wallSeconds >= lastHit && queuedReport.wallSeconds - lastHit < .2f,
                                "Automatic finish did not stop the timer at the final finding.");
                        }
                        else Check(session.Running && !session.ParticipantTrials.Contains(queuedReport), "Partial findings ended the trial.");
                    }
                    else Check(EditorApplication.timeSinceStartup - reportQueuedAt < 1.5, "Queued F was not processed: " + session.ParticipantFeedback);
                }
                if (session.Running && !waitingForReport)
                {
                    var canvas = UnityEngine.Object.FindAnyObjectByType<ParticipantCanvasUI>(); canvas.Refresh();
                    if (session.ParticipantTrialNumber > 1 && session.ParticipantMarkedCount == 0)
                        Check(canvas.WorldHighlightCount == 0, "Previous trial highlights leaked.");
                    if (session.ParticipantTrialNumber == 2 && session.ParticipantFoundTargetCount == 1 && !checkedDuplicate)
                    {
                        int before = session.ParticipantMarkedCount;
                        session.ReportAim();
                        Check(session.ParticipantMarkedCount == before && session.ParticipantMarkAttempts.Last().reason == "already_marked",
                            "Duplicate F was not suppressed.");
                        session.CompleteParticipantTrial();
                        Check(session.Running && !session.ParticipantAllTargetsFound, "Duplicate or manual finish completed a partial trial.");
                        var accepted = session.ParticipantFindings.Last();
                        Check(session.GetParticipantHighlightOutline(accepted)?.Length == 4, "Accepted finding has no boundary.");
                        Check(session.GetParticipantHighlightOutline(new DefectFinding { surfaceId = accepted.surfaceId,
                            matchedDefectId = accepted.matchedDefectId }) == null, "Unaccepted finding exposed boundary metadata.");
                        ValidateRecovery(session); canvas.Refresh();
                        Check(canvas.WorldHighlightCount == before, "Recovery lost the existing highlight.");
                        checkedDuplicate = true;
                    }
                    int wanted = session.ParticipantTrialNumber <= 2 ? session.ParticipantTargetCount
                        : session.ParticipantTrialNumber == 3 ? 1 : 0;
                    if (session.ParticipantFoundTargetCount < wanted)
                    {
                        queuedReport = (DefectTrialReport)typeof(DefectExperimentSession).GetField("current", flags).GetValue(session);
                        var target = queuedReport.manifest.defects[session.ParticipantFoundTargetCount];
                        session.agent.Motor.ResetMotor(target.center + Vector3.up * .08f, Quaternion.identity);
                        float pitch = Mathf.DeltaAngle(0, session.viewCamera.transform.eulerAngles.x);
                        session.ApplyParticipantLook(new Vector2(0, (pitch - 85) / session.mouseSensitivity));
                        reportsBefore = queuedReport.findings.Count;
                        reportKeyboard = InputSystem.AddDevice<Keyboard>();
                        InputSystem.QueueStateEvent(reportKeyboard, new KeyboardState(Key.F));
                        waitingForReport = true; reportQueuedAt = EditorApplication.timeSinceStartup;
                    }
                }
                if (!session.ParticipantFinished) return;
                Check(checkedControls && checkedPause && checkedRecovery && checkedDuplicate, "Required software checks were skipped.");
                var ui = UnityEngine.Object.FindAnyObjectByType<ParticipantCanvasUI>(); ui.Refresh();
                Check(ui.transform.Find("Five trial results").gameObject.activeSelf, "Final results did not activate.");
                Check(session.ParticipantTrials.Count == 5 && session.ParticipantTrials.Take(2).All(t => t.status == "complete")
                    && session.ParticipantTrials.Skip(2).All(t => t.status == "failed" && t.finishReason == "time_limit"),
                    "Success/timeout outcomes were not distinguished.");
                Check(session.ParticipantTrials.Any(t => t.pauseCount > 0 && t.pausedWallSeconds >= .19f), "Pause was not logged.");
                int[] expected = session.protocol.protocolId == CollisionSeedSearch.StudyProtocolId
                    ? new[] { 1, 2, 2, 1, 2 } : new[] { 1, 2, 3, 4, 5 };
                for (int i = 0; i < 5; i++)
                {
                    Check(session.ParticipantTrials[i].manifest.defects.Count == expected[i], "Frozen target count differs.");
                    if (session.protocol.protocolId == CollisionSeedSearch.StudyProtocolId)
                        Check(session.protocol.scenarios[i].gridSize == new Vector3Int(i < 3 ? 6 : 12, 2, i < 3 ? 6 : 12), "Map dimensions changed.");
                }
                string output = SessionState.GetString(Output, "");
                ValidateTrialCopyButtons(ui, session);
                File.WriteAllText(Path.Combine(output, "per-trial-copy-checks.txt"),
                    "PASS: five Copy buttons copy formatted time for successful trials and a single hyphen for failed trials; invalid indices preserve clipboard; old combined copy button absent. Software check only.\n");
                if (Environment.GetCommandLineArgs().Contains("-captureResults"))
                    ParticipantMenuPreview.CaptureResults(output + "-preview");
                File.WriteAllText(Path.Combine(output, "auto-completion-checks.txt"),
                    "PASS: incomplete manual finish blocked; last unique target automatically stops trial and timer; repeated F cannot complete a partial trial; one- and two-target successes; partial/zero-target timeouts fail; final five-row result distinguishes success/failure. Software check only.\n");
                File.WriteAllText(Path.Combine(output, "anomaly-recovery-checks.txt"),
                    "PASS: frozen targets " + string.Join(",", expected) + "; healthy F rejected; real F accepted; duplicate suppressed; void recovery restores upper-floor checkpoint without clearing findings/timer.\n");
                File.WriteAllText(Path.Combine(output, "highlight-checks.txt"),
                    "PASS: healthy/unfound floor has no outline; accepted F creates boundary; duplicate suppressed; boundary survives recovery; next trial resets highlights.\n");
                File.WriteAllText(Path.Combine(output, "canvas-layout-checks.txt"), SessionState.GetString(Output + ".Layout", ""));
                File.WriteAllText(Path.Combine(output, "jump-checks.txt"), SessionState.GetString(Output + ".Jump", "")
                    + "PASS: Space queued once; paused Space ignored. Software check only.\n");
                File.WriteAllText(Path.Combine(output, "controls-checks.txt"),
                    "PASS: WASD/mouse/Space, normalized movement, pause stops movement/timer, logged pause, automatic trial completion and final Canvas results. Original AI motor unchanged. Software check only.\n");
                EditorApplication.isPlaying = false;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception); EditorApplication.isPlaying = false;
            }
        }

        private static void ValidateTrialCopyButtons(ParticipantCanvasUI ui, DefectExperimentSession session)
        {
            var buttons = ui.transform.Find("Five trial results").GetComponentsInChildren<Button>();
            Check(buttons.Count(button => button.GetComponentInChildren<Text>().text == "Copy") == 5,
                "Final results must contain exactly five Copy buttons.");
            Check(!buttons.Any(button => button.GetComponentInChildren<Text>().text == "Salin waktu lima trial"),
                "Old combined copy button is still present.");
            string previous = GUIUtility.systemCopyBuffer;
            try
            {
                for (int i = 0; i < 5; i++)
                {
                    var button = buttons.Single(item => item.name == "Copy trial " + (i + 1));
                    Check(button.interactable, "Completed trial Copy button is disabled.");
                    button.onClick.Invoke();
                    var trial = session.ParticipantTrials[i];
                    string expected = trial.status == "complete" ? DefectExperimentSession.Clock(trial.wallSeconds) : "-";
                    Check(GUIUtility.systemCopyBuffer == expected,
                        "Copy button must copy successful trial time or '-' for a failed trial: " + (i + 1));
                    Check(session.ParticipantFeedback.StartsWith("Waktu trial " + (i + 1) + " disalin."),
                        "Copy feedback identifies the wrong trial.");
                }
                string lastTime = GUIUtility.systemCopyBuffer;
                session.CopyParticipantTrialTime(-1);
                session.CopyParticipantTrialTime(5);
                Check(GUIUtility.systemCopyBuffer == lastTime, "Invalid trial index changed clipboard.");
            }
            finally { GUIUtility.systemCopyBuffer = previous; }
            ui.Refresh();
        }

        private static void ValidateRecovery(DefectExperimentSession session)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var build = (DefectScenarioBuild)typeof(DefectExperimentSession).GetField("build", flags).GetValue(session);
            var remember = typeof(DefectExperimentSession).GetMethod("RememberParticipantSafePosition", flags);
            var positionField = typeof(DefectExperimentSession).GetField("participantSafePosition", flags);
            bool found = false;
            foreach (var floor in build.coverageCells.Values.Where(point => point.y > session.generator.CellSize.y * .5f))
            {
                session.agent.Motor.ResetMotor(floor + Vector3.up * .08f, Quaternion.identity);
                remember.Invoke(session, null);
                if (((Vector3)positionField.GetValue(session)).y <= session.generator.CellSize.y * .5f) continue;
                found = true; break;
            }
            Check(found, "No safe upper-floor recovery point found.");
            Vector3 safe = (Vector3)positionField.GetValue(session);
            int marks = session.ParticipantMarkedCount;
            int falls = session.ParticipantTrials.Count; // Campaign count must not change on recovery.
            float remaining = session.ParticipantRemainingSeconds;
            session.agent.Motor.ResetMotor(safe + Vector3.down * 1000, Quaternion.identity);
            session.ObserveMovement();
            Check(session.ParticipantRespawning, "Void fall did not schedule recovery.");
            typeof(DefectExperimentSession).GetMethod("Update", flags).Invoke(session, null);
            Check(session.Running && Vector3.Distance(session.agent.transform.position, safe) < .02f, "Recovery returned to the wrong floor/position.");
            Check(session.ParticipantMarkedCount == marks && session.ParticipantTrials.Count == falls, "Recovery cleared findings or advanced the trial.");
            Check(session.ParticipantRemainingSeconds <= remaining && remaining - session.ParticipantRemainingSeconds < 1,
                "Recovery reset or stopped the trial timer.");
        }

        private static void ValidateInputAndLook(DefectExperimentSession session)
        {
            // Batch Editor has no focused Game view. Route synthetic input to the
            // player state and process it in the same Dynamic phase as Update().
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            var keyboard = InputSystem.AddDevice<Keyboard>();
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.D)); InputSystem.Update();
                Vector2 movement = session.ReadParticipantMovement();
                Check(Mathf.Abs(movement.x - .7071068f) < .001f && Mathf.Abs(movement.y - .7071068f) < .001f, "W+D mapping failed.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A)); InputSystem.Update();
                Check(session.ReadParticipantMovement() == Vector2.left, "A mapping failed.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S)); InputSystem.Update();
                Check(session.ReadParticipantMovement() == Vector2.down, "S mapping failed.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); InputSystem.Update();
                typeof(DefectExperimentSession).GetMethod("UpdateParticipantControls", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(session, null);
                Check(session.ConsumeParticipantJump() && !session.ConsumeParticipantJump(), "Space was not queued exactly once for the physics tick.");
                session.SetParticipantPaused(true);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space)); InputSystem.Update();
                typeof(DefectExperimentSession).GetMethod("UpdateParticipantControls", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(session, null);
                Check(!session.ConsumeParticipantJump(), "Space was accepted while paused.");
                session.SetParticipantPaused(false);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
            }
            finally { InputSystem.RemoveDevice(keyboard); }
            float yaw = session.agent.transform.eulerAngles.y;
            float pitch = Mathf.DeltaAngle(0, session.viewCamera.transform.eulerAngles.x);
            session.ApplyParticipantLook(new Vector2(100, 0));
            Check(Mathf.Abs(Mathf.DeltaAngle(yaw, session.agent.transform.eulerAngles.y) - 100 * session.mouseSensitivity) < .01f, "Mouse yaw failed.");
            session.ApplyParticipantLook(new Vector2(0, 100000));
            Check(Mathf.Abs(Mathf.DeltaAngle(0, session.viewCamera.transform.eulerAngles.x) + 85) < .01f, "Upward pitch limit failed.");
            session.ApplyParticipantLook(new Vector2(0, -100000));
            Check(Mathf.Abs(Mathf.DeltaAngle(0, session.viewCamera.transform.eulerAngles.x) - 85) < .01f, "Downward pitch limit failed.");
            session.ApplyParticipantLook(new Vector2(-100, (85 - pitch) / session.mouseSensitivity));
        }

        private static void Changed(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Active, false)) return;
            SessionState.SetBool(Active, false);
            bool passed = File.Exists(Path.Combine(SessionState.GetString(Output, ""), "controls-checks.txt"));
            EditorApplication.delayCall += () => EditorApplication.Exit(passed ? 0 : 1);
        }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
