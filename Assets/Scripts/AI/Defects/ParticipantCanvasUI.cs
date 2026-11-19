using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace TesisQA.AI
{
    [DisallowMultipleComponent]
    public sealed class ParticipantCanvasUI : MonoBehaviour
    {
        public DefectExperimentSession session;
        [SerializeField] private GameObject menu, hud, pause, intermission, results, failure;
        [SerializeField] private Text menuHeading, menuDescription, trialLabel, remaining, feedback;
        [SerializeField] private Text intermissionTitle, intermissionTime, totalTime, finalFeedback;
        [SerializeField] private Text[] trialTimes;
        [SerializeField] private Button[] trialCopyButtons = new Button[0];
        [SerializeField] private Button startButton, guideButton, quitButton, hudMenuButton, resumeButton;
        [SerializeField] private Button finishButton, pauseQuitButton, nextButton, formButton, finalQuitButton, errorQuitButton;
        [SerializeField] private RectTransform markerRoot, instructionRect;
        [SerializeField] private RectTransform menuColumns, defectGuideColumns;
        [SerializeField] private Text defectClue;
        [SerializeField] private List<GridLayoutGroup> responsiveColumns = new List<GridLayoutGroup>();
        private readonly List<RectTransform> markers = new List<RectTransform>();
        private readonly ParticipantAnomalyHighlights worldHighlights = new ParticipantAnomalyHighlights();
        public int WorldHighlightCount => worldHighlights.Count;
        private bool guide;
        private static readonly Color Ink = new Color(.055f, .075f, .12f);
        private static readonly Color PanelColor = new Color(.09f, .12f, .18f);
        private static readonly Color Muted = new Color(.66f, .73f, .82f);
        private static readonly Color Accent = new Color(.35f, .94f, .76f);
        private static Font font;

        private void Start()
        {
            EnsureEventSystem();
            startButton.onClick.AddListener(session.StartParticipantGame);
            guideButton.onClick.AddListener(() => ShowGuide(!guide));
            quitButton.onClick.AddListener(DefectExperimentSession.QuitParticipantGame);
            hudMenuButton.onClick.AddListener(() => session.SetParticipantPaused(true));
            resumeButton.onClick.AddListener(() => session.SetParticipantPaused(false));
            if (finishButton != null) finishButton.gameObject.SetActive(false);
            pauseQuitButton.onClick.AddListener(DefectExperimentSession.QuitParticipantGame);
            nextButton.onClick.AddListener(session.AdvanceParticipantTrial);
            for (int i = 0; i < trialCopyButtons.Length; i++)
            {
                int trialIndex = i;
                trialCopyButtons[i].onClick.AddListener(() => session.CopyParticipantTrialTime(trialIndex));
            }
            formButton.onClick.AddListener(session.OpenParticipantForm);
            finalQuitButton.onClick.AddListener(DefectExperimentSession.QuitParticipantGame);
            errorQuitButton.onClick.AddListener(DefectExperimentSession.QuitParticipantGame);
            EnsureDefectClue();
            RefreshMenu();
            Refresh();
        }

        private void LateUpdate() { if (session != null) Refresh(); }
        private void OnDisable() { worldHighlights.Hide(); }
        private void OnDestroy() { worldHighlights.Dispose(); }

        private void RefreshMenu()
        {
            menuHeading.text = guide ? "Cara bermain" : "Theres Something Wrong with the Map";
            bool walls = session.protocol != null && session.protocol.inspectWalls;
            menuDescription.text = walls ? (guide
                ? "Collider menahan karakter pada lantai dan tembok. Cari lantai yang kehilangan pijakan atau tembok yang dapat ditembus. Bidik permukaannya dan tekan F."
                : "Jelajahi lima dunia dan temukan lantai yang kehilangan pijakan atau tembok yang dapat ditembus. Collider lantai bisa hilang sebagian atau seluruh satu tile kotak.") : guide
                ? "Collider adalah permukaan tak terlihat yang menahan karakter. Lantai bisa terlihat utuh, tetapi seluruh atau sebagian pijakannya hilang. Jelajahi dan uji lokasi yang kamu curigai."
                : "Jelajahi lima dunia dan temukan lantai yang terlihat utuh, tetapi tidak menahan pijakan karakter.";
            guideButton.GetComponentInChildren<Text>().text = guide ? "Kembali" : "Cara bermain";
            if (menuColumns != null) menuColumns.gameObject.SetActive(!guide);
            if (defectGuideColumns != null) defectGuideColumns.gameObject.SetActive(guide);
            var subtitle = menu.transform.Find("Subtitle").GetComponent<Text>();
            subtitle.text = guide ? "Garis pada gambar hanya ilustrasi; lokasi anomali trial tetap harus dicari."
                : "Lima trial • Waktu dicatat otomatis";
            foreach (var label in GetComponentsInChildren<Text>(true))
            {
                if (label.text.StartsWith("WASD bergerak •"))
                    label.text = "WASD bergerak • Space lompat • Mouse melihat • Bidik "
                        + (walls ? "lantai/tembok" : "lantai") + " dalam jarak 6 m";
                if (label.text.StartsWith("Arahkan + ke permukaan")) label.text = ParticipantMissionHelp;
            }
        }

        public void ShowGuide(bool value) { guide = value; RefreshMenu(); RefreshLayout(); }

        public void EnsureDefectClue()
        {
            if (defectClue != null || instructionRect == null) return;
            defectClue = Label(instructionRect.Find("Content"),
                "Clue: collider bisa hilang sebagian, atau seluruh satu tile kotak.", 22, 50, Accent, true);
        }

        public void Refresh()
        {
            bool error = !string.IsNullOrEmpty(session.ParticipantError);
            bool started = session.ParticipantStarted;
            bool done = session.ParticipantFinished;
            bool playing = started && !done && (session.Running || session.ParticipantRespawning) && !error;
            menu.SetActive(!started && !error);
            failure.SetActive(error);
            hud.SetActive(playing);
            pause.SetActive(playing && session.ParticipantPaused);
            intermission.SetActive(started && !done && !playing && !error);
            results.SetActive(done && !error);
            trialLabel.text = "T" + session.ParticipantTrialNumber + "/5 | ANOMALI " + session.ParticipantFoundTargetCount + "/" + session.ParticipantTargetCount;
            remaining.text = DefectExperimentSession.Clock(session.ParticipantRemainingSeconds);
            feedback.text = string.IsNullOrEmpty(session.ParticipantFeedback)
                ? "Temukan semua anomali. Trial selesai otomatis saat target lengkap." : session.ParticipantFeedback;
            if (session.ParticipantCompletedTrials > 0)
            {
                var last = session.ParticipantTrials[session.ParticipantCompletedTrials - 1];
                intermissionTitle.text = "Trial " + session.ParticipantCompletedTrials
                    + (last.status == "complete" ? " berhasil" : " gagal - waktu habis");
                intermissionTime.text = DefectExperimentSession.Clock(last.wallSeconds);
                nextButton.GetComponentInChildren<Text>().text = "Mulai trial " + (session.ParticipantCompletedTrials + 1);
            }
            for (int i = 0; i < trialTimes.Length; i++)
            {
                trialTimes[i].text = i < session.ParticipantCompletedTrials
                    ? DefectExperimentSession.Clock(session.ParticipantTrials[i].wallSeconds) + "\n"
                        + (session.ParticipantTrials[i].status == "complete" ? "BERHASIL" : "GAGAL") : "—";
                if (i < trialCopyButtons.Length) trialCopyButtons[i].interactable = i < session.ParticipantCompletedTrials;
            }
            totalTime.text = DefectExperimentSession.Clock(session.ParticipantTotalSeconds);
            finalFeedback.text = session.ParticipantFeedback;
            formButton.interactable = DefectExperimentSession.ValidParticipantFormUrl(session.googleFormUrl);
            RefreshLayout();
            UpdateMarkers(playing && !session.ParticipantPaused);
            session.RefreshParticipantCursor();
        }

        public void RefreshLayout()
        {
            float width = ((RectTransform)transform).rect.width;
            bool narrow = width < 1100;
            if (menuColumns != null)
            {
                float height = ((RectTransform)transform).rect.height;
                float top = narrow ? 310 : 258;
                menuHeading.rectTransform.anchoredPosition = new Vector2(30, -94);
                menuHeading.rectTransform.sizeDelta = new Vector2(-60, narrow ? 145 : 84);
                var subtitle = menu.transform.Find("Subtitle") as RectTransform;
                subtitle.anchoredPosition = new Vector2(30, narrow ? -246 : -194);
                menuColumns.anchoredPosition = new Vector2(30, -top);
                menuColumns.sizeDelta = new Vector2(Mathf.Min(1600, width - 60),
                    narrow ? Mathf.Min(1040, height - top - 190) : Mathf.Min(460, height - top - 190));
                if (defectGuideColumns != null)
                {
                    defectGuideColumns.anchoredPosition = new Vector2(30, -top);
                    defectGuideColumns.sizeDelta = new Vector2(Mathf.Min(1600, width - 60),
                        Mathf.Min(narrow ? 1120 : 600, height - top - 190));
                }
            }
            foreach (var grid in responsiveColumns)
            {
                var rect = ((RectTransform)grid.transform).rect;
                int columns = rect.width < 1100 ? 1 : 2;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = columns;
                var size = new Vector2((rect.width - (columns - 1) * grid.spacing.x) / columns,
                    (rect.height - (columns == 1 ? grid.spacing.y : 0)) / (columns == 1 ? 2 : 1));
                if (grid.cellSize != size) grid.cellSize = size;
            }
            // On narrow screens stack the timer above the instruction card.
            instructionRect.offsetMax = new Vector2(narrow ? -24 : -310, narrow ? -164 : -24);
            instructionRect.offsetMin = new Vector2(24, narrow ? -430 : -242);
        }

        private void UpdateMarkers(bool visible)
        {
            var findings = session.ParticipantFindings;
            worldHighlights.Sync(findings, visible, session.GetParticipantHighlightOutline);
            int count = visible && findings != null ? findings.Count : 0;
            while (markers.Count < count)
            {
                var marker = ImageRect("Reported location", markerRoot, Accent);
                marker.sizeDelta = new Vector2(10, 10);
                marker.anchorMin = marker.anchorMax = new Vector2(.5f, .5f);
                markers.Add(marker);
            }
            for (int i = 0; i < markers.Count; i++)
            {
                bool active = i < count;
                if (active)
                {
                    Vector3 screen = session.viewCamera.WorldToScreenPoint(findings[i].position + Vector3.up * .03f);
                    active = screen.z > 0 && screen.x >= 0 && screen.y >= 0 && screen.x <= Screen.width && screen.y <= Screen.height;
                    if (active && RectTransformUtility.ScreenPointToLocalPointInRectangle(markerRoot, screen, null, out var local))
                        markers[i].anchoredPosition = local;
                }
                markers[i].gameObject.SetActive(active);
            }
        }

        public static ParticipantCanvasUI Create(DefectExperimentSession owner)
        {
            var existing = FindAnyObjectByType<ParticipantCanvasUI>();
            if (existing != null) return existing;
            var root = new GameObject("Participant Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(ParticipantCanvasUI));
            var ui = root.GetComponent<ParticipantCanvasUI>();
            ui.session = owner;
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            EnsureEventSystem();
            ui.CreateMenu(root.transform);
            ui.CreateHud(root.transform);
            ui.CreateIntermission(root.transform);
            ui.CreateResults(root.transform);
            ui.CreatePause(root.transform);
            ui.CreateFailure(root.transform);
            ui.hud.SetActive(false); ui.intermission.SetActive(false); ui.results.SetActive(false);
            ui.pause.SetActive(false); ui.failure.SetActive(false);
            ui.EnsureDefectClue();
            Canvas.ForceUpdateCanvases();
            ui.RefreshLayout();
            return ui;
        }

        public void RebuildMainMenu()
        {
            if (Application.isPlaying) return;
            responsiveColumns.RemoveAll(grid => grid == null || (menu != null && grid.transform.IsChildOf(menu.transform)));
            if (menu != null) DestroyImmediate(menu);
            CreateMenu(transform);
            menu.transform.SetAsFirstSibling();
            RefreshMenu();
            Canvas.ForceUpdateCanvases();
            RefreshLayout();
        }

        public void RebuildTrialPause()
        {
            if (Application.isPlaying) return;
            if (pause != null) DestroyImmediate(pause);
            finishButton = null;
            CreatePause(transform);
            pause.transform.SetAsLastSibling();
            pause.SetActive(false);
        }

        public void RebuildResults()
        {
            if (Application.isPlaying) return;
            responsiveColumns.RemoveAll(grid => grid == null || (results != null && grid.transform.IsChildOf(results.transform)));
            if (results != null) DestroyImmediate(results);
            CreateResults(transform);
            results.SetActive(false);
        }

        private const string ParticipantMissionHelp = "Arahkan + ke permukaan yang dicurigai, lalu tekan F. Gunakan Space untuk melompati celah. Trial otomatis berhasil setelah semua anomali ditemukan. Batas waktu 150 detik; jika belum lengkap, trial gagal.";

        private void CreateMenu(Transform root)
        {
            var frame = ImageRect("Main menu", root, new Color32(13, 22, 27, 255));
            Stretch(frame); menu = frame.gameObject;
            var mint = new Color32(71, 234, 195, 255);
            TopMenuLabel(frame, "Brand", "COLLIDER QUEST / PENGUJIAN GEOMETRI", 20, 44, 32, mint);
            menuHeading = TopMenuLabel(frame, "Title", "Theres Something Wrong with the Map", 54, 94, 84, Color.white, true);
            TopMenuLabel(frame, "Subtitle", "Lima trial • Waktu dicatat otomatis", 25, 194, 44, Muted);
            menuColumns = Rect("Menu panels", frame);
            menuColumns.anchorMin = menuColumns.anchorMax = new Vector2(0, 1);
            menuColumns.pivot = new Vector2(0, 1);
            var columns = menuColumns.gameObject.AddComponent<GridLayoutGroup>();
            columns.spacing = new Vector2(24, 24);
            responsiveColumns.Add(columns);
            var intro = Card("Mission", columns.transform);
            intro.GetComponent<Image>().color = new Color32(24, 29, 45, 255);
            intro.GetComponent<VerticalLayoutGroup>().spacing = 32;
            Label(intro, "MISI KAMU", 21, 35, mint, true);
            menuDescription = Label(intro, "Jelajahi lima dunia dan temukan lantai yang terlihat utuh, tetapi tidak menahan pijakan karakter.", 29, 160, Color.white);
            Label(intro, ParticipantMissionHelp, 25, 135, Muted);
            var controls = Card("Controls", columns.transform);
            controls.GetComponent<Image>().color = new Color32(24, 29, 45, 255);
            controls.GetComponent<VerticalLayoutGroup>().spacing = 10;
            Label(controls, "KENDALI", 21, 35, mint, true);
            Label(controls, "W / S       Maju / mundur", 27, 44, Color.white);
            Label(controls, "A / D       Geser kiri / kanan", 27, 44, Color.white);
            Label(controls, "MOUSE   Arah pandangan", 27, 44, Color.white);
            Label(controls, "SPACE    Lompat melewati celah", 27, 44, Color.white);
            Label(controls, "F              Tandai lokasi bidikan", 27, 44, Color.white);
            Label(controls, "ESC         Menu trial / lepas kursor", 27, 44, Color.white);
            defectGuideColumns = Rect("Defect type examples", frame);
            defectGuideColumns.anchorMin = defectGuideColumns.anchorMax = new Vector2(0, 1);
            defectGuideColumns.pivot = new Vector2(0, 1);
            var examples = defectGuideColumns.gameObject.AddComponent<GridLayoutGroup>();
            examples.spacing = new Vector2(24, 24); responsiveColumns.Add(examples);
            DefectExample(examples.transform, "Sebagian tile", "1. COLLIDER HILANG SEBAGIAN",
                "PartialColliderExample", new Color(1, .75f, .15f),
                "Hanya sebagian pijakan dalam satu tile yang hilang. Bagian lain masih bisa diinjak. Pada contoh, kotak kuning membatasi bagian yang tidak menahan karakter.");
            DefectExample(examples.transform, "Seluruh tile", "2. COLLIDER HILANG SELURUH TILE",
                "FullTileColliderExample", new Color(1, .35f, .35f),
                "Pijakan hilang pada seluruh satu tile kotak. Lantai tetap terlihat utuh, tetapi karakter bisa jatuh saat memasukinya. Pada contoh, garis merah menunjukkan batas tile.");
            defectGuideColumns.gameObject.SetActive(false);
            var actions = Row(frame, 70);
            actions.name = "Menu actions";
            actions.anchorMin = new Vector2(0, 0); actions.anchorMax = new Vector2(1, 0);
            actions.pivot = Vector2.zero;
            actions.offsetMin = new Vector2(30, 60); actions.offsetMax = new Vector2(-30, 132);
            startButton = ActionButton(actions, "Mulai bermain");
            startButton.GetComponent<Image>().color = mint;
            guideButton = ActionButton(actions, "Cara bermain", false);
            quitButton = ActionButton(actions, "Keluar", false);
            foreach (var button in new[] { guideButton, quitButton })
            {
                button.GetComponent<Image>().color = new Color32(183, 198, 217, 255);
                button.GetComponentInChildren<Text>().color = Ink;
            }
            var footer = Label(frame, "Setelah lima trial, salin ringkasan waktu dan isi Google Form.", 22, 40, Muted);
            footer.name = "Menu footer";
            footer.rectTransform.anchorMin = Vector2.zero; footer.rectTransform.anchorMax = new Vector2(1, 0);
            footer.rectTransform.pivot = Vector2.zero;
            footer.rectTransform.offsetMin = new Vector2(30, 10); footer.rectTransform.offsetMax = new Vector2(-30, 48);
        }

        private static void DefectExample(Transform parent, string name, string title, string resource, Color color, string description)
        {
            var card = Card(name, parent);
            card.GetComponent<Image>().color = new Color32(24, 29, 45, 255);
            Label(card, title, 22, 40, color, true);
            var picture = Rect("Example image", card).gameObject.AddComponent<Image>();
            picture.sprite = Resources.Load<Sprite>("ParticipantDefectGuide/" + resource);
            picture.preserveAspect = true; picture.raycastTarget = false;
            var layout = picture.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 100; layout.preferredHeight = 270; layout.flexibleHeight = 1;
            Label(card, description, 25, 115, Color.white);
        }

        private static Text TopMenuLabel(Transform parent, string name, string value, int size, float top, float height, Color color, bool bold = false)
        {
            var label = Label(parent, value, size, height, color, bold);
            label.name = name;
            var rect = label.rectTransform;
            rect.anchorMin = new Vector2(0, 1); rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(30, -top);
            rect.sizeDelta = new Vector2(-60, height);
            return label;
        }

        private void CreateHud(Transform root)
        {
            var full = Rect("Trial HUD", root); Stretch(full); hud = full.gameObject;
            markerRoot = Rect("Reported location markers", full); Stretch(markerRoot);
            instructionRect = ImageRect("Instructions top left", full, new Color(.04f, .065f, .10f, .94f));
            instructionRect.anchorMin = instructionRect.anchorMax = new Vector2(0, 1);
            // Horizontal stretch keeps the card clear of the timer on every aspect ratio.
            instructionRect.anchorMin = new Vector2(0, 1); instructionRect.anchorMax = Vector2.one;
            var instructionContent = Padded(instructionRect, 20);
            Label(instructionContent, "Temukan anomali collider dan tandai menggunakan tombol F", 28, 65, Color.white, true);
            Label(instructionContent, "WASD bergerak • Space lompat • Mouse melihat • Bidik lantai dalam jarak 6 m", 22, 44, Muted);
            var status = ImageRect("Trial and timer top right", full, new Color(.04f, .065f, .10f, .94f));
            status.anchorMin = status.anchorMax = Vector2.one; status.pivot = Vector2.one;
            status.anchoredPosition = new Vector2(-24, -24); status.sizeDelta = new Vector2(260, 140);
            var statusContent = Padded(status, 18);
            trialLabel = Label(statusContent, "TRIAL 1 / 5", 22, 32, Accent, true);
            remaining = Label(statusContent, "02:30.00", 40, 58, Color.white, true);
            var crosshair = Rect("Crosshair", full); crosshair.anchorMin = crosshair.anchorMax = new Vector2(.5f, .5f); crosshair.sizeDelta = new Vector2(24, 24);
            var horizontal = ImageRect("Horizontal", crosshair, Color.white); horizontal.sizeDelta = new Vector2(22, 3);
            var vertical = ImageRect("Vertical", crosshair, Color.white); vertical.sizeDelta = new Vector2(3, 22);
            var bottom = ImageRect("Feedback bottom left", full, new Color(.04f, .065f, .10f, .94f));
            bottom.anchorMin = Vector2.zero; bottom.anchorMax = new Vector2(1, 0);
            bottom.offsetMin = new Vector2(24, 24); bottom.offsetMax = new Vector2(-340, 124);
            feedback = Label(Padded(bottom, 18), "Tekan Esc untuk membuka menu trial.", 25, 64, Color.white);
            var buttonHolder = Rect("Menu button bottom right", full);
            buttonHolder.anchorMin = buttonHolder.anchorMax = new Vector2(1, 0); buttonHolder.pivot = new Vector2(1, 0);
            buttonHolder.anchoredPosition = new Vector2(-24, 24); buttonHolder.sizeDelta = new Vector2(290, 82);
            hudMenuButton = ActionButton(buttonHolder, "Esc • Menu trial"); Stretch((RectTransform)hudMenuButton.transform);
        }

        private void CreateIntermission(Transform root)
        {
            var frame = Page("Between trials", root, out intermission);
            Label(frame, "COLLIDER QUEST", 20, 32, Accent);
            intermissionTitle = Label(frame, "Trial selesai", 54, 90, Color.white, true);
            var card = Card("Trial time", frame);
            card.GetComponent<LayoutElement>().flexibleHeight = 1;
            Label(card, "WAKTU BERMAIN TRIAL INI", 23, 45, Accent, true);
            intermissionTime = Label(card, "00:00.00", 70, 120, Color.white, true);
            Label(card, "Waktu berhenti di layar ini. Saat siap, lanjutkan ke dunia berikutnya.", 28, 100, Muted);
            var row = Row(frame, 72); nextButton = ActionButton(row, "Mulai trial berikutnya");
        }

        private void CreateResults(Transform root)
        {
            var frame = Page("Five trial results", root, out results);
            Label(frame, "COLLIDER QUEST / HASIL PENGUJIAN", 20, 32, Accent);
            Label(frame, "Lima trial selesai. Terima kasih!", 54, 84, Color.white, true);
            Label(frame, "Tekan Copy di sebelah waktu tiap trial, lalu tempel ke Google Form.", 26, 64, Muted);
            var columns = Columns(frame);
            var table = Card("Five trial time table", columns.transform);
            Label(table, "WAKTU TIAP TRIAL (MENIT:DETIK)", 22, 48, Accent, true);
            trialTimes = new Text[5];
            trialCopyButtons = new Button[5];
            for (int i = 0; i < 5; i++)
            {
                var row = Row(table, 65);
                row.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
                var nameLayout = Label(row, "Trial " + (i + 1), 28, 65, Color.white).GetComponent<LayoutElement>();
                nameLayout.minWidth = nameLayout.preferredWidth = 105;
                nameLayout.flexibleWidth = 0;
                trialTimes[i] = Label(row, "—", 35, 65, Accent, true);
                trialCopyButtons[i] = ActionButton(row, "Copy", false);
                trialCopyButtons[i].name = "Copy trial " + (i + 1);
                var copyLayout = trialCopyButtons[i].GetComponent<LayoutElement>();
                copyLayout.minWidth = copyLayout.preferredWidth = 130;
                copyLayout.flexibleWidth = 0;
                copyLayout.minHeight = 46;
                copyLayout.preferredHeight = 65;
            }
            var summary = Card("Summary and form", columns.transform);
            Label(summary, "TOTAL WAKTU BERMAIN", 23, 42, Accent, true);
            totalTime = Label(summary, "00:00.00", 56, 90, Color.white, true);
            Label(summary, "Menu, jeda, dan layar antartrial tidak dihitung. Hasil juga tersimpan otomatis.", 24, 94, Muted);
            formButton = ActionButton(summary, "Buka Google Form");
            finalQuitButton = ActionButton(summary, "Keluar dari game", false);
            finalFeedback = Label(frame, "", 23, 48, Accent);
        }

        private void CreatePause(Transform root)
        {
            var overlay = ImageRect("Pause menu", root, new Color(.02f, .035f, .06f, .94f)); Stretch(overlay); pause = overlay.gameObject;
            var box = ImageRect("Pause card", overlay, PanelColor);
            box.anchorMin = new Vector2(.15f, .15f); box.anchorMax = new Vector2(.85f, .85f); box.offsetMin = box.offsetMax = Vector2.zero;
            var content = Padded(box, 32); var group = content.GetComponent<VerticalLayoutGroup>(); group.childAlignment = TextAnchor.MiddleCenter;
            Label(content, "Menu trial", 48, 90, Color.white, true);
            Label(content, "Waktu dan gerakan dijeda. Temukan semua anomali untuk menyelesaikan trial secara otomatis. Batas waktu 150 detik; target belum lengkap berarti trial gagal.", 27, 110, Muted);
            resumeButton = ActionButton(content, "Lanjut bermain (Esc)");
            pauseQuitButton = ActionButton(content, "Keluar dari game", false);
        }

        private void CreateFailure(Transform root)
        {
            var frame = Page("Session error", root, out failure);
            Label(frame, "Sesi belum dapat dilanjutkan", 48, 100, Color.white, true);
            Label(frame, "Terjadi kendala saat memuat atau menyimpan permainan. Hubungi peneliti. Hasil yang sudah dicatat tersimpan otomatis.", 28, 180, Muted);
            errorQuitButton = ActionButton(frame, "Keluar");
        }

        public static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var system = new GameObject("Participant EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            system.AddComponent<InputSystemUIInputModule>().AssignDefaultActions();
#else
            system.AddComponent<StandaloneInputModule>();
#endif
        }

        private static RectTransform Rect(string name, Transform parent)
        {
            var value = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            value.SetParent(parent, false); return value;
        }
        private static void Stretch(RectTransform value)
        { value.anchorMin = Vector2.zero; value.anchorMax = Vector2.one; value.offsetMin = value.offsetMax = Vector2.zero; }
        private static RectTransform ImageRect(string name, Transform parent, Color color)
        { var value = Rect(name, parent); var image = value.gameObject.AddComponent<Image>(); image.color = color; image.raycastTarget = false; return value; }
        private static RectTransform Padded(RectTransform parent, int padding)
        {
            var content = Rect("Content", parent); Stretch(content);
            var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(padding, padding, padding, padding); group.spacing = 14;
            group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = true; group.childForceExpandHeight = false;
            return content;
        }
        private static RectTransform Page(string name, Transform parent, out GameObject page)
        {
            var background = ImageRect(name, parent, Ink); Stretch(background); page = background.gameObject;
            return Padded(background, 48);
        }
        private static RectTransform Card(string name, Transform parent)
        {
            var card = ImageRect(name, parent, PanelColor);
            card.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
            // The card itself owns its layout so grid/parent size drives its content.
            var group = card.gameObject.AddComponent<VerticalLayoutGroup>();
            group.padding = new RectOffset(28, 28, 28, 28); group.spacing = 14;
            group.childControlWidth = group.childControlHeight = true; group.childForceExpandWidth = true; group.childForceExpandHeight = false;
            return card;
        }
        private GridLayoutGroup Columns(Transform parent)
        {
            var rect = Rect("Responsive columns", parent);
            // Fixed layout preferences avoid a feedback loop between the grid's
            // calculated cell height and its parent's flexible-height allocation.
            var layout = rect.gameObject.AddComponent<LayoutElement>(); layout.flexibleHeight = 1;
            layout.minHeight = layout.preferredHeight = 460;
            layout.minWidth = layout.preferredWidth = 0; layout.flexibleWidth = 1;
            var grid = rect.gameObject.AddComponent<GridLayoutGroup>(); grid.spacing = new Vector2(24, 24);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount; grid.constraintCount = 2;
            responsiveColumns.Add(grid); return grid;
        }
        private static RectTransform Row(Transform parent, float height)
        {
            var row = Rect("Row", parent); var layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 20; layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
            row.gameObject.AddComponent<LayoutElement>().preferredHeight = height; return row;
        }
        private static Text Label(Transform parent, string value, int size, float height, Color color, bool bold = false)
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rect = Rect(value.Length > 40 ? value.Substring(0, 40) : value, parent);
            var text = rect.gameObject.AddComponent<Text>(); text.font = font; text.text = value;
            text.color = color; text.fontSize = size; text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            text.alignment = TextAnchor.MiddleLeft; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate; text.raycastTarget = false;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = Mathf.Min(20, size); text.resizeTextMaxSize = size;
            var layout = rect.gameObject.AddComponent<LayoutElement>(); layout.preferredHeight = height;
            layout.flexibleWidth = 1; return text;
        }
        private static Button ActionButton(Transform parent, string title, bool primary = true)
        {
            var rect = ImageRect(title, parent, primary ? Accent : new Color(.72f, .78f, .86f));
            rect.GetComponent<Image>().raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            var colors = button.colors; colors.highlightedColor = new Color(.85f, 1, .95f); colors.pressedColor = new Color(.65f, .78f, .75f); button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var layout = rect.gameObject.AddComponent<LayoutElement>(); layout.preferredHeight = 70; layout.flexibleWidth = 1;
            var label = Label(rect, title, 27, 60, Ink, true); Stretch((RectTransform)label.transform);
            ((RectTransform)label.transform).offsetMin = new Vector2(14, 6); ((RectTransform)label.transform).offsetMax = new Vector2(-14, -6);
            label.alignment = TextAnchor.MiddleCenter; return button;
        }
    }
}
