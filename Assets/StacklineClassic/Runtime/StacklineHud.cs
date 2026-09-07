using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace Wukong.StacklineClassic
{
    [DisallowMultipleComponent]
    public sealed class StacklineHud : MonoBehaviour
    {
        private enum MenuPanel
        {
            None,
            Settings,
            Challenges,
            Themes,
            Leaderboard,
            AdBonus,
            LifeShop,
        }

        private sealed class AmbientParticle
        {
            public RectTransform Rect;
            public Vector2 Position;
            public Vector2 Velocity;
            public float Spin;
        }

        private StacklineClassicController controller;
        private Canvas canvas;
        private Canvas ambientCanvas;
        private RectTransform safeAreaRoot;
        private GameObject menuGroup;
        private GameObject gameGroup;
        private GameObject reviveGroup;
        private GameObject rescueGroup;
        private GameObject panelRoot;
        private RectTransform panelContent;
        private ScrollRect panelScroll;
        private Text panelTitle;
        private Text menuTitleText;
        private Text menuScoreText;
        private Text menuStatusText;
        private Text gameScoreText;
        private Text gemText;
        private Text lifeText;
        private Text starText;
        private Text gemBonusText;
        private Text perfectText;
        private Text toastText;
        private Text reviveHeightText;
        private Text reviveResourcesText;
        private Text rescueText;
        private Button lifeButton;
        private readonly List<AmbientParticle> ambientParticles = new List<AmbientParticle>();
        private Font uiFont;
        private bool ownsFont;
        private MenuPanel currentPanel;
        private MenuPanel builtPanel;
        private StacklineLanguage builtPanelLanguage;
        private Color builtPanelAccent;
        private string builtPanelState;
        private bool panelRefreshPending;
        private int lastPanelActionFrame = -1;
        private Rect previousSafeArea;
        private Rect previousPixelRect;
        private bool hasAppliedResponsiveLayout;
        private bool landscapeLayout;
        private StacklineLanguage language;
        private int lastMenuDisplayScore;
        private int lastMenuBest;
        private int lastReviveLives;
        private int lastPerfectCombo;
        private bool lastMenuIsNewRecord;
        private bool lastMenuHasCompletedRun;
        private string lastToastMessage = string.Empty;
        private float gemBonusTimer;
        private float perfectTimer;
        private float toastTimer;
        private Color accentColor = new Color(0.93f, 0.68f, 0.20f, 1f);
        private const float HeadLockedDistanceMeters = 2.6f;
        private bool xrWorldSpace;
        private Camera uiCamera;
        private int displayedGems = -1;
        private int displayedLives = -1;
        private int displayedStars = -1;

        public bool CapturesPrimaryInput => currentPanel != MenuPanel.None || IsPointerOverMenuButton();

        private bool IsPointerOverMenuButton()
        {
            if (menuGroup == null || !menuGroup.activeInHierarchy || canvas == null)
                return false;

            Vector2 pointerPosition;
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                pointerPosition = Mouse.current.position.ReadValue();
            else if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                pointerPosition = Touchscreen.current.primaryTouch.position.ReadValue();
            else
                return false;

            Button[] buttons = menuGroup.GetComponentsInChildren<Button>(false);
            foreach (Button button in buttons)
            {
                RectTransform rect = button != null ? button.transform as RectTransform : null;
                if (button != null && button.interactable && rect != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(rect, pointerPosition, canvas.worldCamera))
                    return true;
            }
            return false;
        }

        public void Configure(StacklineClassicController newController, Camera gameplayCamera, bool useXrWorldSpace = false)
        {
            controller = newController;
            xrWorldSpace = useXrWorldSpace;
            uiCamera = gameplayCamera;
            language = StacklineLocalization.FromCode(StacklineProfileStore.LoadLanguageCode());
            EnsureCanvas(gameplayCamera);
            BuildIfNeeded();
            RefreshPersistentState();
        }

        private void Start()
        {
            // Attach after the tracked camera is active. As a child, the HUD also receives
            // PICO's late/before-render HMD pose updates instead of only following its startup pose.
            if (xrWorldSpace)
                AttachHeadLockedCanvas(uiCamera);
        }

        public void ShowMenu(int displayScore, int best, int gems, int lives, int stars, bool isNewRecord, bool hasCompletedRun)
        {
            lastMenuDisplayScore = displayScore;
            lastMenuBest = best;
            lastMenuIsNewRecord = isNewRecord;
            lastMenuHasCompletedRun = hasCompletedRun;
            ClosePanel();
            SetOnly(menuGroup);
            menuScoreText.text = displayScore.ToString();
            UpdateMenuStatusText();
            SetResources(gems, lives, stars);
        }

        public void ShowPlaying(int height, int gems, int lives, int stars)
        {
            ClosePanel();
            SetOnly(gameGroup);
            gameScoreText.text = height <= 0 ? string.Empty : height.ToString();
            SetResources(gems, lives, stars);
        }

        public void ShowRevive(int height, int lives, int gems)
        {
            lastReviveLives = lives;
            ClosePanel();
            SetOnly(reviveGroup);
            reviveHeightText.text = height.ToString();
            reviveResourcesText.text = "\u25c6  " + gems + "     \u2665  " + lives;
            lifeButton.interactable = lives > 0;
            UpdateLifeButtonLabel();
            SetResources(gems, lives, controller != null ? controller.Stars : 0);
        }

        public void ShowRescue(int seconds)
        {
            ClosePanel();
            SetOnly(rescueGroup);
            rescueText.text = Mathf.Max(0, seconds).ToString();
        }

        public void ShowGameOver(int height, int best, int gems, int lives, int stars, bool isNewRecord)
        {
            ShowMenu(height, best, gems, lives, stars, isNewRecord, true);
        }

        public void ShowGemBonus(int amount)
        {
            gemBonusText.text = "+" + Mathf.Max(1, amount);
            gemBonusTimer = 1.2f;
        }

        public void ShowPerfect(int combo)
        {
            lastPerfectCombo = combo;
            perfectText.text = FormatPerfect(combo);
            perfectTimer = 0.8f;
        }

        public void ShowToast(string message)
        {
            lastToastMessage = message ?? string.Empty;
            toastText.text = StacklineLocalization.LocalizeMessage(lastToastMessage, language);
            toastText.gameObject.SetActive(true);
            toastTimer = 1.8f;
        }

        public void SetTheme(Color accent)
        {
            accentColor = accent;
            if (perfectText != null)
                perfectText.color = Color.Lerp(Color.white, accentColor, 0.18f);
        }

        public void RefreshPersistentState()
        {
            if (controller == null || gemText == null)
                return;
            SetResources(controller.Gems, controller.Lives, controller.Stars);
            ApplyStaticLocalization();
            if (currentPanel != MenuPanel.None)
                RebuildCurrentPanel();
        }

        private void Update()
        {
            UpdateSafeArea();
            UpdateAmbientParticles();
            TickTransientText(ref gemBonusTimer, gemBonusText);
            TickTransientText(ref perfectTimer, perfectText);
            TickTransientText(ref toastTimer, toastText, true);
        }

        private void LateUpdate()
        {
            if (!panelRefreshPending)
                return;
            panelRefreshPending = false;
            RefreshPanelContent();
        }

        private void OnDestroy()
        {
            if (ownsFont && uiFont != null)
                Destroy(uiFont);
        }

        private void EnsureCanvas(Camera gameplayCamera)
        {
            canvas = GetComponentInChildren<Canvas>(true);
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("Stackline HUD Canvas");
                // Keep the screen HUD out of the user-positioned/scaled gameplay hierarchy.
                // Screen-space coordinates should never inherit the arena's 3D transform.
                canvasObject.layer = 5;
                canvasObject.transform.SetParent(null, false);
                canvas = canvasObject.AddComponent<Canvas>();
                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            canvas.worldCamera = gameplayCamera;
            if (xrWorldSpace)
                ConfigureWorldSpaceCanvas(gameplayCamera);
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.planeDistance = 0.5f;
            }
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720f, 1560f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            scaler.referencePixelsPerUnit = 100f;
        }

        private void ConfigureWorldSpaceCanvas(Camera gameplayCamera)
        {
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = gameplayCamera;
            canvas.planeDistance = 1f;
            canvas.overrideSorting = false;

            RectTransform canvasRect = canvas.transform as RectTransform;
            if (canvasRect != null)
            {
                // 3.2 m x 1.8 m at 2.6 m applies the requested 2x PICO HUD enlargement while leaving the
                // scenery and gold table visible around it. The explicit rect prevents
                // CanvasScaler's screen-space defaults from collapsing all anchors together.
                canvasRect.sizeDelta = new Vector2(1280f, 720f);
                // PICO revision: make the entire HUD twice as large in physical space.
                canvasRect.localScale = Vector3.one * 0.0025f;
            }

            GraphicRaycaster screenRaycaster = canvas.GetComponent<GraphicRaycaster>();
            if (screenRaycaster == null)
                screenRaycaster = canvas.gameObject.AddComponent<GraphicRaycaster>();
            // World-space UI still needs this raycaster for mouse/touch PointerEventData.
            // TrackedDeviceGraphicRaycaster only handles tracked-device events. XRI's
            // UIInputModule excludes screen-space hits while processing a controller ray,
            // so both can stay enabled without delivering a second controller click.
            screenRaycaster.enabled = true;
            TrackedDeviceGraphicRaycaster xrRaycaster = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
            if (xrRaycaster == null)
                xrRaycaster = canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();

            // The user explicitly requested a 180-degree Y flip. Keep the raycaster double-sided
            // so a PICO controller ray can still activate menu Buttons on the visible face.
            xrRaycaster.ignoreReversedGraphics = false;

            AttachHeadLockedCanvas(gameplayCamera);
        }

        private void AttachHeadLockedCanvas(Camera gameplayCamera)
        {
            if (canvas == null || gameplayCamera == null)
                return;

            Transform canvasTransform = canvas.transform;
            Transform hmdTransform = gameplayCamera.transform;
            if (canvasTransform.parent != hmdTransform)
                canvasTransform.SetParent(hmdTransform, false);

            // Keep the panel in the HMD's local forward direction. The previous world-space
            // orientation was LookRotation(-camera.forward, camera.up) followed by the user-
            // requested 180-degree Y correction; together those resolve to the HMD's rotation.
            // Identity here preserves that corrected visible face while making the UI head-locked.
            canvasTransform.localPosition = Vector3.forward * HeadLockedDistanceMeters;
            canvasTransform.localRotation = Quaternion.identity;
        }

        private void BuildIfNeeded()
        {
            if (menuGroup != null)
                return;

            uiFont = CreateUiFont();
            GameObject safeObject = CreateRectObject(canvas.transform, "Safe Area", Vector2.zero, Vector2.one);
            safeAreaRoot = safeObject.GetComponent<RectTransform>();

            GameObject particleLayer = CreateRectObject(safeAreaRoot, "Ambient Particles", Vector2.zero, Vector2.one);
            // Isolate the moving decoration from the static text/button canvas batches.
            // Inherit sorting and the head-locked transform; decorative graphics have no raycaster.
            ambientCanvas = particleLayer.AddComponent<Canvas>();
            ambientCanvas.overrideSorting = false;
            BuildAmbientParticles(particleLayer.transform);

            GameObject resourceLayer = CreateRectObject(safeAreaRoot, "Resources", Vector2.zero, Vector2.one);
            BuildResources(resourceLayer.transform);

            menuGroup = CreateGroup(safeAreaRoot, "Menu");
            gameGroup = CreateGroup(safeAreaRoot, "Game");
            reviveGroup = CreateGroup(safeAreaRoot, "Revive");
            rescueGroup = CreateGroup(safeAreaRoot, "Rescue");

            BuildMenu(menuGroup.transform);
            BuildGame(gameGroup.transform);
            BuildRevive(reviveGroup.transform);
            BuildRescue(rescueGroup.transform);
            BuildModal(canvas.transform);

            toastText = CreatePointText(safeAreaRoot, "Toast", string.Empty, 22, new Vector2(0.5f, 0.18f),
                new Vector2(520f, 58f), TextAnchor.MiddleCenter, Color.white);
            toastText.gameObject.SetActive(false);
            SetOnly(menuGroup);
            ApplyStaticLocalization();
            UpdateSafeArea(true);
        }

        private void BuildResources(Transform parent)
        {
            gemText = CreateResourceRow(parent, "Diamonds", "\u25c6", new Vector2(0.92f, 0.94f));
            lifeText = CreateResourceRow(parent, "Lives", "\u2665", new Vector2(0.92f, 0.89f));
            starText = CreateResourceRow(parent, "Stars", "\u2605", new Vector2(0.92f, 0.84f));
            gemBonusText = CreatePointText(parent, "Diamond Bonus", string.Empty, 22, new Vector2(0.79f, 0.94f),
                new Vector2(90f, 54f), TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.72f));
        }

        private Text CreateResourceRow(Transform parent, string name, string glyph, Vector2 anchor)
        {
            GameObject row = CreatePointObject(parent, name, anchor, new Vector2(128f, 58f));
            // Keep the counters visually grouped without turning their empty space into an
            // input blocker.  The small black-glass chip is based on the Holymolly mobile
            // component treatment and makes the three rows legible over the busy 3D scene.
            Image surface = row.AddComponent<Image>();
            surface.color = new Color(0.015f, 0.018f, 0.022f, 0.66f);
            surface.raycastTarget = false;
            Outline surfaceOutline = row.AddComponent<Outline>();
            surfaceOutline.effectColor = new Color(1f, 0.73f, 0.28f, 0.30f);
            surfaceOutline.effectDistance = new Vector2(1f, -1f);
            Text number = CreatePointText(row.transform, "Value", "0", 30, new Vector2(0.35f, 0.5f),
                new Vector2(70f, 54f), TextAnchor.MiddleRight, Color.white);
            Color iconColor = name == "Lives" ? new Color(0.94f, 0.38f, 0.34f, 1f) : new Color(1f, 0.79f, 0.31f, 1f);
            CreatePointText(row.transform, "Icon", glyph, 31, new Vector2(0.78f, 0.5f),
                new Vector2(48f, 54f), TextAnchor.MiddleCenter, iconColor);
            return number;
        }

        private void BuildMenu(Transform parent)
        {
            menuTitleText = CreatePointText(parent, "Game Title", L(StacklineText.GameTitle), 40,
                new Vector2(0.5f, 0.88f), new Vector2(520f, 66f), TextAnchor.MiddleCenter,
                new Color(1f, 0.82f, 0.38f, 1f));
            Shadow titleShadow = menuTitleText.gameObject.AddComponent<Shadow>();
            titleShadow.effectColor = new Color(0f, 0f, 0f, 0.76f);
            titleShadow.effectDistance = new Vector2(0f, -2f);
            menuScoreText = CreatePointText(parent, "Menu Score", "0", 104, new Vector2(0.5f, 0.76f),
                new Vector2(360f, 142f), TextAnchor.MiddleCenter, Color.white);
            menuStatusText = CreatePointText(parent, "Menu Status", L(StacklineText.TapToStart), 34, new Vector2(0.5f, 0.63f),
                new Vector2(460f, 68f), TextAnchor.MiddleCenter, Color.white);

            CreateIconButton(parent, "Settings", "\u2699", L(StacklineText.Settings), new Vector2(0.08f, 0.93f), new Vector2(84f, 84f),
                () => OpenPanel(MenuPanel.Settings));
            CreateIconButton(parent, "Challenges", "\u25ce", string.Empty, new Vector2(0.89f, 0.53f), new Vector2(84f, 84f),
                () => OpenPanel(MenuPanel.Challenges));

            CreateIconButton(parent, "Leaderboard", "\u2582\u2585\u2588", L(StacklineText.Leaderboard), new Vector2(0.34f, 0.105f), new Vector2(132f, 108f),
                () => OpenPanel(MenuPanel.Leaderboard));
            CreateIconButton(parent, "Life Shop", "\u2665", L(StacklineText.Lives), new Vector2(0.66f, 0.105f), new Vector2(132f, 108f),
                () => OpenPanel(MenuPanel.LifeShop));
        }

        private void BuildGame(Transform parent)
        {
            gameScoreText = CreatePointText(parent, "Height", string.Empty, 104, new Vector2(0.5f, 0.82f),
                new Vector2(340f, 150f), TextAnchor.MiddleCenter, Color.white);
            perfectText = CreatePointText(parent, "Perfect", string.Empty, 25, new Vector2(0.5f, 0.72f),
                new Vector2(420f, 62f), TextAnchor.MiddleCenter, Color.white);
        }

        private void BuildRevive(Transform parent)
        {
            Image shade = parent.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.48f);
            CreatePointText(parent, "Revive Title", L(StacklineText.ContinuePrompt), 38, new Vector2(0.5f, 0.67f),
                new Vector2(480f, 78f), TextAnchor.MiddleCenter, Color.white);
            reviveHeightText = CreatePointText(parent, "Revive Height", "0", 88, new Vector2(0.5f, 0.56f),
                new Vector2(300f, 120f), TextAnchor.MiddleCenter, Color.white);
            reviveResourcesText = CreatePointText(parent, "Revive Resources", string.Empty, 22, new Vector2(0.5f, 0.47f),
                new Vector2(460f, 56f), TextAnchor.MiddleCenter, Color.white);
            CreateCommandButton(parent, "Rescue", L(StacklineText.ThreeSecondRescue), new Vector2(0.5f, 0.36f), new Vector2(360f, 82f),
                controller.RequestRescue, new Color(0.46f, 0.31f, 0.08f, 0.96f));
            lifeButton = CreateCommandButton(parent, "Use Life", L(StacklineText.UseLife), new Vector2(0.5f, 0.28f), new Vector2(320f, 74f),
                controller.ReviveWithLife, new Color(0.42f, 0.16f, 0.22f, 0.94f));
            CreateCommandButton(parent, "End Run", L(StacklineText.EndRun), new Vector2(0.5f, 0.20f), new Vector2(250f, 66f),
                controller.FinishGame, new Color(0.06f, 0.08f, 0.09f, 0.88f));
        }

        private void BuildRescue(Transform parent)
        {
            Image shade = parent.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.42f);
            rescueText = CreatePointText(parent, "Rescue Count", "3", 112, new Vector2(0.5f, 0.55f),
                new Vector2(300f, 150f), TextAnchor.MiddleCenter, Color.white);
            CreatePointText(parent, "Rescue Label", L(StacklineText.Rescue), 24, new Vector2(0.5f, 0.46f),
                new Vector2(300f, 60f), TextAnchor.MiddleCenter, Color.white);
        }

        private void BuildModal(Transform canvasTransform)
        {
            panelRoot = CreateGroup(canvasTransform, "Modal");
            Image dimmer = panelRoot.AddComponent<Image>();
            dimmer.color = new Color(0f, 0f, 0f, 0.62f);
            dimmer.raycastTarget = true;

            GameObject frame = CreateRectObject(panelRoot.transform, "Panel", new Vector2(0.12f, 0.09f), new Vector2(0.88f, 0.91f));
            Image frameImage = frame.AddComponent<Image>();
            frameImage.color = new Color(0.035f, 0.032f, 0.026f, 0.94f);
            Outline frameOutline = frame.AddComponent<Outline>();
            frameOutline.effectColor = new Color(1f, 0.72f, 0.26f, 0.34f);
            frameOutline.effectDistance = new Vector2(1.5f, -1.5f);
            panelTitle = CreateStretchText(frame.transform, "Title", string.Empty, 32, new Vector2(0.08f, 0.86f),
                new Vector2(0.84f, 0.98f), TextAnchor.MiddleLeft, Color.white);
            CreateIconButton(frame.transform, "Close", "X", string.Empty, new Vector2(0.92f, 0.92f), new Vector2(72f, 72f), ClosePanel);

            GameObject viewport = CreateRectObject(frame.transform, "Viewport", new Vector2(0.06f, 0.06f), new Vector2(0.94f, 0.85f));
            Image viewportImage = viewport.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;
            viewport.AddComponent<RectMask2D>();
            panelScroll = viewport.AddComponent<ScrollRect>();
            panelScroll.horizontal = false;
            panelScroll.vertical = true;
            panelScroll.movementType = ScrollRect.MovementType.Clamped;
            panelScroll.scrollSensitivity = 32f;

            GameObject content = CreateRectObject(viewport.transform, "Content", new Vector2(0f, 1f), new Vector2(1f, 1f));
            panelContent = content.GetComponent<RectTransform>();
            panelContent.anchorMin = new Vector2(0f, 1f);
            panelContent.anchorMax = new Vector2(1f, 1f);
            panelContent.pivot = new Vector2(0.5f, 1f);
            panelContent.anchoredPosition = Vector2.zero;
            panelContent.sizeDelta = new Vector2(0f, 900f);
            panelScroll.viewport = viewport.GetComponent<RectTransform>();
            panelScroll.content = panelContent;
            panelRoot.SetActive(false);
        }

        private void OpenPanel(MenuPanel panel)
        {
            if (controller == null)
                return;
            currentPanel = panel;
            panelRoot.transform.SetAsLastSibling();
            panelRoot.SetActive(true);
            controller.SetMenuInteractionBlocked(true);
            RebuildCurrentPanel();
        }

        public void ClosePanel()
        {
            currentPanel = MenuPanel.None;
            panelRefreshPending = false;
            if (panelRoot != null)
                panelRoot.SetActive(false);
            if (controller != null)
                controller.SetMenuInteractionBlocked(false);
        }

        private void RebuildCurrentPanel()
        {
            // Controller transactions and their button callbacks can both request a refresh.
            // Apply their final state once, after this frame's input has been processed.
            panelRefreshPending = currentPanel != MenuPanel.None;
        }

        private void InvokePanelAction(Action callback)
        {
            // A modal stays alive until LateUpdate refreshes it. Coalesce duplicate pointer
            // releases in that frame so a purchase or toggle cannot execute twice.
            if (callback == null || currentPanel == MenuPanel.None || lastPanelActionFrame == Time.frameCount)
                return;
            lastPanelActionFrame = Time.frameCount;
            callback();
        }

        private void RefreshPanelContent()
        {
            if (panelContent == null || controller == null || currentPanel == MenuPanel.None)
                return;

            string state = GetPanelState();
            if (builtPanel == currentPanel && builtPanelLanguage == language &&
                builtPanelAccent.Equals(accentColor) && builtPanelState == state)
            {
                ResetPanelScroll();
                return;
            }

            for (int index = panelContent.childCount - 1; index >= 0; index--)
            {
                GameObject child = panelContent.GetChild(index).gameObject;
                child.SetActive(false);
                Destroy(child);
            }

            panelContent.anchoredPosition = Vector2.zero;

            switch (currentPanel)
            {
                case MenuPanel.Settings:
                    BuildSettingsPanel();
                    break;
                case MenuPanel.Challenges:
                    BuildChallengesPanel();
                    break;
                case MenuPanel.Themes:
                    BuildThemesPanel();
                    break;
                case MenuPanel.Leaderboard:
                    BuildLeaderboardPanel();
                    break;
                case MenuPanel.AdBonus:
                    BuildAdPanel();
                    break;
                case MenuPanel.LifeShop:
                    BuildLifePanel();
                    break;
            }

            builtPanel = currentPanel;
            builtPanelLanguage = language;
            builtPanelAccent = accentColor;
            builtPanelState = state;
            Canvas.ForceUpdateCanvases();
            ResetPanelScroll();
        }

        private void ResetPanelScroll()
        {
            if (panelScroll != null)
            {
                panelScroll.StopMovement();
                panelScroll.verticalNormalizedPosition = 1f;
            }
        }

        private string GetPanelState()
        {
            // Only capture data displayed by the open panel. This runs on explicit refreshes,
            // never every frame, and lets an unchanged panel survive close/reopen intact.
            StringBuilder state = new StringBuilder(64);
            switch (currentPanel)
            {
                case MenuPanel.Settings:
                    state.Append(controller.SoundEnabled).Append('|').Append(controller.HapticsEnabled);
                    break;
                case MenuPanel.Challenges:
                    for (int index = 0; index < controller.ChallengeCount; index++)
                        state.Append(controller.GetChallengeLabel(index)).Append('|')
                            .Append(controller.IsChallengeClaimed(index)).Append('|')
                            .Append(controller.CanClaimChallenge(index)).Append(';');
                    break;
                case MenuPanel.Themes:
                    state.Append(controller.SelectedTheme).Append('|');
                    for (int index = 0; index < controller.ThemeCount; index++)
                        state.Append(controller.GetThemeName(index)).Append('|')
                            .Append(controller.GetThemeCost(index)).Append('|')
                            .Append(controller.IsThemeUnlocked(index)).Append(';');
                    break;
                case MenuPanel.Leaderboard:
                    foreach (string line in controller.GetLeaderboardLines())
                        state.Append(line).Append('\n');
                    break;
                case MenuPanel.AdBonus:
                    state.Append(controller.AdFree).Append('|').Append(controller.DailyBonusClaimed);
                    break;
                case MenuPanel.LifeShop:
                    state.Append(controller.Gems).Append('|').Append(controller.Lives);
                    break;
            }
            return state.ToString();
        }

        private void BuildSettingsPanel()
        {
            panelTitle.text = L(StacklineText.Settings);
            SetPanelContentHeight(820f);
            CreatePanelText(L(StacklineText.Sound), 22, -18f, 54f, TextAnchor.MiddleLeft);
            CreatePanelToggleButton(controller.SoundEnabled ? L(StacklineText.On) : L(StacklineText.Off), -84f, 70f, () =>
            {
                controller.ToggleSound();
                RebuildCurrentPanel();
            }, controller.SoundEnabled);
            CreatePanelText(L(StacklineText.Haptics), 22, -184f, 54f, TextAnchor.MiddleLeft);
            CreatePanelToggleButton(controller.HapticsEnabled ? L(StacklineText.On) : L(StacklineText.Off), -250f, 70f, () =>
            {
                controller.ToggleHaptics();
                RebuildCurrentPanel();
            }, controller.HapticsEnabled);
            CreatePanelText(L(StacklineText.Language), 22, -350f, 54f, TextAnchor.MiddleLeft);
            CreatePanelChoiceButton(L(StacklineText.Chinese), -416f, 0.08f, 0.48f,
                () => SetLanguage(StacklineLanguage.Chinese), language == StacklineLanguage.Chinese);
            CreatePanelChoiceButton(L(StacklineText.English), -416f, 0.52f, 0.92f,
                () => SetLanguage(StacklineLanguage.English), language == StacklineLanguage.English);
            CreatePanelText(L(StacklineText.RulesDescription), 18, -540f, 132f, TextAnchor.UpperLeft);
        }

        private void BuildChallengesPanel()
        {
            panelTitle.text = L(StacklineText.Challenges);
            SetPanelContentHeight(860f);
            float y = -18f;
            for (int index = 0; index < controller.ChallengeCount; index++)
            {
                int captured = index;
                CreatePanelText(StacklineLocalization.LocalizeChallenge(controller.GetChallengeLabel(index), language),
                    21, y, 54f, TextAnchor.MiddleLeft);
                bool claimed = controller.IsChallengeClaimed(index);
                bool canClaim = controller.CanClaimChallenge(index);
                string label = claimed ? L(StacklineText.Claimed) : canClaim ? L(StacklineText.ClaimStar) : L(StacklineText.Locked);
                CreatePanelButton(label, y - 62f, 68f, () =>
                {
                    ShowToast(controller.ClaimChallenge(captured));
                    RefreshPersistentState();
                }, canClaim && !claimed);
                y -= 178f;
            }
        }

        private void BuildThemesPanel()
        {
            panelTitle.text = L(StacklineText.Skins);
            int count = controller.ThemeCount;
            int rows = Mathf.CeilToInt(count / 2f);
            SetPanelContentHeight(Mathf.Max(820f, rows * 205f + 40f));
            for (int index = 0; index < count; index++)
            {
                int captured = index;
                int row = index / 2;
                int column = index % 2;
                float x = column == 0 ? -0.255f : 0.255f;
                float y = -28f - row * 200f;
                GameObject swatch = CreatePanelSwatch(controller.GetThemePreviewColor(index), x, y);
                Button button = swatch.AddComponent<Button>();
                button.targetGraphic = swatch.GetComponent<Image>();
                button.onClick.AddListener(() => InvokePanelAction(() =>
                {
                    ShowToast(controller.SelectOrUnlockTheme(captured));
                    RefreshPersistentState();
                }));
                string suffix;
                if (controller.SelectedTheme == index)
                    suffix = "  " + L(StacklineText.Selected);
                else if (controller.IsThemeUnlocked(index))
                    suffix = "  " + L(StacklineText.Owned);
                else
                    suffix = "  \u25c6" + controller.GetThemeCost(index);
                string themeName = StacklineLocalization.LocalizeTheme(controller.GetThemeName(index), language);
                CreatePointText(swatch.transform, "Name", themeName + suffix, 17,
                    new Vector2(0.5f, 0.13f), new Vector2(210f, 42f), TextAnchor.MiddleCenter, Color.white);
            }
        }

        private void BuildLeaderboardPanel()
        {
            panelTitle.text = L(StacklineText.Leaderboard);
            string[] lines = controller.GetLeaderboardLines();
            SetPanelContentHeight(Mathf.Max(740f, lines.Length * 78f + 70f));
            for (int index = 0; index < lines.Length; index++)
            {
                Color color = lines[index].Contains("YOU") ? accentColor : Color.white;
                string localizedLine = StacklineLocalization.LocalizeLeaderboard(lines[index], language);
                Text row = CreatePanelText(localizedLine, 22, -18f - index * 76f, 58f, TextAnchor.MiddleLeft);
                row.color = color;
            }
        }

        private void BuildAdPanel()
        {
            panelTitle.text = L(StacklineText.AdBonus);
            SetPanelContentHeight(700f);
            CreatePanelText(controller.AdFree ? L(StacklineText.AdFreeSummary) : L(StacklineText.OfflineDemoSummary),
                24, -24f, 110f, TextAnchor.MiddleCenter);
            CreatePanelButton(controller.AdFree ? L(StacklineText.Active) : L(StacklineText.UnlockDouble), -170f, 82f, () =>
            {
                ShowToast(controller.ActivateAdFree());
                RefreshPersistentState();
            }, !controller.AdFree);
            CreatePanelButton(controller.DailyBonusClaimed ? L(StacklineText.DailyClaimed) : L(StacklineText.ClaimDaily), -282f, 82f, () =>
            {
                ShowToast(controller.ClaimDailyBonus());
                RefreshPersistentState();
            }, !controller.DailyBonusClaimed);
            CreatePanelText(L(StacklineText.AdDisclaimer), 17, -416f, 120f, TextAnchor.UpperLeft);
        }

        private void BuildLifePanel()
        {
            panelTitle.text = L(StacklineText.Lives);
            SetPanelContentHeight(700f);
            CreatePanelText("\u25c6  " + controller.Gems + "        \u2665  " + controller.Lives, 30, -22f, 80f,
                TextAnchor.MiddleCenter);
            CreatePanelButton(L(StacklineText.OneLife), -140f, 82f, () =>
            {
                ShowToast(controller.BuyLives(1, 3));
                RefreshPersistentState();
            }, controller.Gems >= 3);
            CreatePanelButton(L(StacklineText.ThreeLives), -250f, 82f, () =>
            {
                ShowToast(controller.BuyLives(3, 8));
                RefreshPersistentState();
            }, controller.Gems >= 8);
            CreatePanelText(L(StacklineText.LifeDescription), 18, -380f, 90f, TextAnchor.UpperLeft);
        }

        private void SetLanguage(StacklineLanguage newLanguage)
        {
            if (language == newLanguage)
                return;

            language = newLanguage;
            StacklineProfileStore.SaveLanguageCode(StacklineLocalization.ToCode(language));
            ApplyStaticLocalization();
            if (currentPanel != MenuPanel.None)
                RebuildCurrentPanel();
        }

        private void ApplyStaticLocalization()
        {
            if (menuTitleText != null)
                menuTitleText.text = L(StacklineText.GameTitle);
            SetChildText(menuGroup != null ? menuGroup.transform : null, "Settings Button/Label", L(StacklineText.Settings));
            SetChildText(menuGroup != null ? menuGroup.transform : null, "Leaderboard Button/Label", L(StacklineText.Leaderboard));
            SetChildText(menuGroup != null ? menuGroup.transform : null, "Life Shop Button/Label", L(StacklineText.Lives));

            SetChildText(reviveGroup != null ? reviveGroup.transform : null, "Revive Title", L(StacklineText.ContinuePrompt));
            SetChildText(reviveGroup != null ? reviveGroup.transform : null, "Rescue Button/Label", L(StacklineText.ThreeSecondRescue));
            SetChildText(reviveGroup != null ? reviveGroup.transform : null, "End Run Button/Label", L(StacklineText.EndRun));
            SetChildText(rescueGroup != null ? rescueGroup.transform : null, "Rescue Label", L(StacklineText.Rescue));

            UpdateMenuStatusText();
            UpdateLifeButtonLabel();
            if (perfectText != null && perfectTimer > 0f)
                perfectText.text = FormatPerfect(lastPerfectCombo);
            if (toastText != null && toastText.gameObject.activeSelf && !string.IsNullOrEmpty(lastToastMessage))
                toastText.text = StacklineLocalization.LocalizeMessage(lastToastMessage, language);
        }

        private void UpdateMenuStatusText()
        {
            if (menuStatusText == null)
                return;

            if (lastMenuIsNewRecord)
                menuStatusText.text = L(StacklineText.NewRecord);
            else if (lastMenuHasCompletedRun)
                menuStatusText.text = lastMenuDisplayScore >= lastMenuBest && lastMenuBest > 0
                    ? L(StacklineText.Best)
                    : L(StacklineText.TapToStart);
            else
                menuStatusText.text = lastMenuBest > 0
                    ? L(StacklineText.Best) + "  " + lastMenuBest
                    : L(StacklineText.TapToStart);
        }

        private void UpdateLifeButtonLabel()
        {
            if (lifeButton == null)
                return;
            Text label = lifeButton.GetComponentInChildren<Text>(true);
            if (label != null)
                label.text = lastReviveLives > 0 ? L(StacklineText.UseLife) : L(StacklineText.NoLives);
        }

        private string FormatPerfect(int combo)
        {
            string label = L(StacklineText.Perfect);
            return combo >= 2 ? label + "  x" + combo : label;
        }

        private string L(StacklineText text)
        {
            return StacklineLocalization.Get(language, text);
        }

        private void SetPanelContentHeight(float height)
        {
            if (panelContent == null)
                return;
            panelContent.anchorMin = new Vector2(0f, 1f);
            panelContent.anchorMax = new Vector2(1f, 1f);
            panelContent.pivot = new Vector2(0.5f, 1f);
            panelContent.anchoredPosition = Vector2.zero;
            panelContent.sizeDelta = new Vector2(0f, Mathf.Max(1f, height));
        }

        private static void SetChildText(Transform root, string path, string value)
        {
            if (root == null)
                return;
            Transform child = root.Find(path);
            if (child == null)
                return;
            Text text = child.GetComponent<Text>();
            if (text != null)
                text.text = value;
        }

        private GameObject CreatePanelSwatch(Color color, float normalizedX, float y)
        {
            GameObject swatch = new GameObject("Theme Swatch");
            swatch.layer = panelContent.gameObject.layer;
            swatch.transform.SetParent(panelContent, false);
            RectTransform rect = swatch.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f + normalizedX, 1f);
            rect.anchorMax = rect.anchorMin;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(230f, 164f);
            Image image = swatch.AddComponent<Image>();
            image.color = color;
            return swatch;
        }

        private Text CreatePanelText(string value, int size, float y, float height, TextAnchor alignment)
        {
            Text text = CreateStretchText(panelContent, value, value, size, new Vector2(0.04f, 1f), new Vector2(0.96f, 1f),
                alignment, Color.white);
            RectTransform rect = text.rectTransform;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(0f, height);
            return text;
        }

        private Button CreatePanelButton(string label, float y, float height, Action callback, bool interactable)
        {
            return CreatePanelButton(label, y, height, callback, interactable, interactable);
        }

        private Button CreatePanelToggleButton(string label, float y, float height, Action callback, bool selected)
        {
            return CreatePanelButton(label, y, height, callback, true, selected);
        }

        private Button CreatePanelButton(string label, float y, float height, Action callback, bool interactable,
            bool highlighted)
        {
            GameObject buttonObject = new GameObject(label + " Button");
            buttonObject.layer = panelContent.gameObject.layer;
            buttonObject.transform.SetParent(panelContent, false);
            RectTransform rect = buttonObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.08f, 1f);
            rect.anchorMax = new Vector2(0.92f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(0f, height);
            Image image = buttonObject.AddComponent<Image>();
            image.color = highlighted ? new Color(accentColor.r * 0.46f, accentColor.g * 0.46f, accentColor.b * 0.46f, 0.96f)
                : new Color(0.12f, 0.14f, 0.15f, 0.9f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.interactable = interactable;
            button.onClick.AddListener(() => InvokePanelAction(callback));
            CreatePointText(buttonObject.transform, "Label", label, 20, new Vector2(0.5f, 0.5f),
                new Vector2(430f, height), TextAnchor.MiddleCenter, Color.white);
            return button;
        }

        private Button CreatePanelChoiceButton(string label, float y, float anchorMinX, float anchorMaxX,
            Action callback, bool selected)
        {
            GameObject buttonObject = new GameObject(label + " Language Button");
            buttonObject.layer = panelContent.gameObject.layer;
            buttonObject.transform.SetParent(panelContent, false);
            RectTransform rect = buttonObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorMinX, 1f);
            rect.anchorMax = new Vector2(anchorMaxX, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, y);
            rect.sizeDelta = new Vector2(0f, 72f);
            Image image = buttonObject.AddComponent<Image>();
            image.color = selected
                ? new Color(accentColor.r * 0.72f, accentColor.g * 0.72f, accentColor.b * 0.72f, 0.98f)
                : new Color(0.12f, 0.14f, 0.15f, 0.94f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.interactable = !selected;
            button.onClick.AddListener(() => InvokePanelAction(callback));
            CreatePointText(buttonObject.transform, "Label", selected ? label + "  \u2713" : label, 19,
                new Vector2(0.5f, 0.5f), new Vector2(210f, 72f), TextAnchor.MiddleCenter, Color.white);
            return button;
        }

        private void BuildAmbientParticles(Transform parent)
        {
            System.Random random = new System.Random(7319);
            for (int index = 0; index < 9; index++)
            {
                GameObject particle = new GameObject("Ambient Square " + index);
                particle.layer = parent.gameObject.layer;
                particle.transform.SetParent(parent, false);
                RectTransform rect = particle.AddComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.zero;
                rect.pivot = new Vector2(0.5f, 0.5f);
                float size = Mathf.Lerp(4f, 11f, (float)random.NextDouble());
                rect.sizeDelta = new Vector2(size, size);
                Image image = particle.AddComponent<Image>();
                image.color = new Color(1f, 0.78f, 0.28f, Mathf.Lerp(0.20f, 0.64f, (float)random.NextDouble()));
                image.raycastTarget = false;
                AmbientParticle entry = new AmbientParticle
                {
                    Rect = rect,
                    Position = new Vector2((float)random.NextDouble(), (float)random.NextDouble()),
                    Velocity = new Vector2(Mathf.Lerp(-0.012f, 0.012f, (float)random.NextDouble()),
                        Mathf.Lerp(0.012f, 0.034f, (float)random.NextDouble())),
                    Spin = Mathf.Lerp(-24f, 24f, (float)random.NextDouble()),
                };
                ambientParticles.Add(entry);
            }
        }

        private void UpdateAmbientParticles()
        {
            if (safeAreaRoot == null || canvas == null || !canvas.isActiveAndEnabled ||
                ambientCanvas == null || !ambientCanvas.isActiveAndEnabled)
                return;
            Vector2 size = safeAreaRoot.rect.size;
            if (size.x <= 1f || size.y <= 1f)
                return;
            float delta = Time.unscaledDeltaTime;
            foreach (AmbientParticle particle in ambientParticles)
            {
                particle.Position += particle.Velocity * delta;
                if (particle.Position.y > 1.04f)
                    particle.Position.y = -0.04f;
                if (particle.Position.x > 1.04f)
                    particle.Position.x = -0.04f;
                else if (particle.Position.x < -0.04f)
                    particle.Position.x = 1.04f;
                particle.Rect.anchoredPosition = new Vector2(particle.Position.x * size.x, particle.Position.y * size.y);
                particle.Rect.Rotate(0f, 0f, particle.Spin * delta);
            }
        }

        private void SetResources(int gems, int lives, int stars)
        {
            SetResourceValue(gemText, Mathf.Max(0, gems), ref displayedGems);
            SetResourceValue(lifeText, Mathf.Max(0, lives), ref displayedLives);
            SetResourceValue(starText, Mathf.Max(0, stars), ref displayedStars);
        }

        private static void SetResourceValue(Text text, int value, ref int displayed)
        {
            if (text == null || displayed == value)
                return;
            displayed = value;
            text.text = value.ToString();
        }

        private void SetOnly(GameObject visible)
        {
            if (menuGroup != null) menuGroup.SetActive(visible == menuGroup);
            if (gameGroup != null) gameGroup.SetActive(visible == gameGroup);
            if (reviveGroup != null) reviveGroup.SetActive(visible == reviveGroup);
            if (rescueGroup != null) rescueGroup.SetActive(visible == rescueGroup);
        }

        private void UpdateSafeArea(bool force = false)
        {
            if (safeAreaRoot == null || canvas == null)
                return;
            if (xrWorldSpace)
            {
                if (force || safeAreaRoot.anchorMin != Vector2.zero || safeAreaRoot.anchorMax != Vector2.one ||
                    safeAreaRoot.offsetMin != Vector2.zero || safeAreaRoot.offsetMax != Vector2.zero)
                {
                    safeAreaRoot.anchorMin = Vector2.zero;
                    safeAreaRoot.anchorMax = Vector2.one;
                    safeAreaRoot.offsetMin = Vector2.zero;
                    safeAreaRoot.offsetMax = Vector2.zero;
                }
                ApplyResponsiveLayout(true);
                return;
            }
            Rect safe = Screen.safeArea;
            Rect pixel = canvas.pixelRect;
            if (!force && safe == previousSafeArea && pixel == previousPixelRect)
                return;
            previousSafeArea = safe;
            previousPixelRect = pixel;
            if (pixel.width <= 0f || pixel.height <= 0f)
                return;
            Vector2 min = new Vector2((safe.xMin - pixel.xMin) / pixel.width, (safe.yMin - pixel.yMin) / pixel.height);
            Vector2 max = new Vector2((safe.xMax - pixel.xMin) / pixel.width, (safe.yMax - pixel.yMin) / pixel.height);
            safeAreaRoot.anchorMin = new Vector2(Mathf.Clamp01(min.x), Mathf.Clamp01(min.y));
            safeAreaRoot.anchorMax = new Vector2(Mathf.Clamp01(max.x), Mathf.Clamp01(max.y));
            safeAreaRoot.offsetMin = Vector2.zero;
            safeAreaRoot.offsetMax = Vector2.zero;
            ApplyResponsiveLayout(pixel.width >= pixel.height);
        }

        private void ApplyResponsiveLayout(bool landscape)
        {
            if (hasAppliedResponsiveLayout && landscapeLayout == landscape)
                return;

            hasAppliedResponsiveLayout = true;
            landscapeLayout = landscape;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
            scaler.referenceResolution = landscape ? new Vector2(1280f, 720f) : new Vector2(720f, 1560f);
            scaler.matchWidthOrHeight = 0.5f;

            // Treat the current desktop Game view as a first-class horizontal layout instead
            // of simply scaling the tall mobile arrangement down.  All vertical bands are
            // explicitly separated: resources at the upper right, score in the upper middle,
            // gameplay feedback in the centre, and a compact two-button dock at the bottom.
            // This also leaves enough room for the longer English labels and Chinese text.
            ApplyResourceLayout(landscape);
            ApplyMenuLayout(landscape);
            ApplyGameplayLayout(landscape);
            ApplyReviveLayout(landscape);

            SetStretchAnchors(canvas.transform, "Modal/Panel",
                landscape ? new Vector2(0.18f, 0.08f) : new Vector2(0.12f, 0.09f),
                landscape ? new Vector2(0.82f, 0.92f) : new Vector2(0.88f, 0.91f));
        }

        private void ApplyResourceLayout(bool landscape)
        {
            Vector2 rowSize = landscape ? new Vector2(120f, 38f) : new Vector2(132f, 52f);
            int valueSize = landscape ? 23 : 27;
            int iconSize = landscape ? 23 : 27;
            SetResourceRowLayout("Resources/Diamonds", landscape ? new Vector2(0.93f, 0.93f) : new Vector2(0.91f, 0.93f),
                rowSize, valueSize, iconSize);
            SetResourceRowLayout("Resources/Lives", landscape ? new Vector2(0.93f, 0.872f) : new Vector2(0.91f, 0.875f),
                rowSize, valueSize, iconSize);
            SetResourceRowLayout("Resources/Stars", landscape ? new Vector2(0.93f, 0.814f) : new Vector2(0.91f, 0.82f),
                rowSize, valueSize, iconSize);
            SetPointLayout(safeAreaRoot, "Resources/Diamond Bonus",
                landscape ? new Vector2(0.81f, 0.93f) : new Vector2(0.77f, 0.93f),
                landscape ? new Vector2(96f, 34f) : new Vector2(106f, 46f));
            SetTextLayout(safeAreaRoot, "Resources/Diamond Bonus", landscape ? 18 : 21);
        }

        private void ApplyMenuLayout(bool landscape)
        {
            if (landscape)
            {
                SetPointLayout(safeAreaRoot, "Menu/Game Title", new Vector2(0.5f, 0.905f), new Vector2(430f, 50f));
                SetTextLayout(safeAreaRoot, "Menu/Game Title", 34);
                SetPointLayout(safeAreaRoot, "Menu/Menu Score", new Vector2(0.5f, 0.79f), new Vector2(300f, 90f));
                SetTextLayout(safeAreaRoot, "Menu/Menu Score", 76);
                SetPointLayout(safeAreaRoot, "Menu/Menu Status", new Vector2(0.5f, 0.685f), new Vector2(390f, 46f));
                SetTextLayout(safeAreaRoot, "Menu/Menu Status", 25);

                SetIconButtonLayout("Menu/Settings Button", new Vector2(0.065f, 0.91f), new Vector2(68f, 68f), true, 27, 12);
                SetIconButtonLayout("Menu/Challenges Button", new Vector2(0.938f, 0.56f), new Vector2(68f, 68f), false, 29, 0);
                SetIconButtonLayout("Menu/Leaderboard Button", new Vector2(0.34f, 0.125f), new Vector2(210f, 76f), true, 23, 13);
                SetIconButtonLayout("Menu/Life Shop Button", new Vector2(0.66f, 0.125f), new Vector2(210f, 76f), true, 26, 13);
                SetPointLayout(safeAreaRoot, "Toast", new Vector2(0.5f, 0.22f), new Vector2(420f, 44f));
                SetTextLayout(safeAreaRoot, "Toast", 20);
                return;
            }

            SetPointLayout(safeAreaRoot, "Menu/Game Title", new Vector2(0.5f, 0.875f), new Vector2(520f, 66f));
            SetTextLayout(safeAreaRoot, "Menu/Game Title", 40);
            SetPointLayout(safeAreaRoot, "Menu/Menu Score", new Vector2(0.5f, 0.745f), new Vector2(360f, 128f));
            SetTextLayout(safeAreaRoot, "Menu/Menu Score", 100);
            SetPointLayout(safeAreaRoot, "Menu/Menu Status", new Vector2(0.5f, 0.635f), new Vector2(470f, 60f));
            SetTextLayout(safeAreaRoot, "Menu/Menu Status", 30);

            SetIconButtonLayout("Menu/Settings Button", new Vector2(0.085f, 0.925f), new Vector2(78f, 78f), true, 31, 14);
            SetIconButtonLayout("Menu/Challenges Button", new Vector2(0.91f, 0.53f), new Vector2(78f, 78f), false, 32, 0);
            SetIconButtonLayout("Menu/Leaderboard Button", new Vector2(0.34f, 0.10f), new Vector2(190f, 96f), true, 27, 16);
            SetIconButtonLayout("Menu/Life Shop Button", new Vector2(0.66f, 0.10f), new Vector2(190f, 96f), true, 30, 16);
            SetPointLayout(safeAreaRoot, "Toast", new Vector2(0.5f, 0.18f), new Vector2(520f, 58f));
            SetTextLayout(safeAreaRoot, "Toast", 22);
        }

        private void ApplyGameplayLayout(bool landscape)
        {
            SetPointLayout(safeAreaRoot, "Game/Height", landscape ? new Vector2(0.5f, 0.84f) : new Vector2(0.5f, 0.82f),
                landscape ? new Vector2(280f, 98f) : new Vector2(340f, 150f));
            SetTextLayout(safeAreaRoot, "Game/Height", landscape ? 78 : 104);
            SetPointLayout(safeAreaRoot, "Game/Perfect", landscape ? new Vector2(0.5f, 0.72f) : new Vector2(0.5f, 0.72f),
                landscape ? new Vector2(380f, 42f) : new Vector2(420f, 62f));
            SetTextLayout(safeAreaRoot, "Game/Perfect", landscape ? 22 : 25);
        }

        private void ApplyReviveLayout(bool landscape)
        {
            if (landscape)
            {
                SetPointLayout(safeAreaRoot, "Revive/Revive Title", new Vector2(0.5f, 0.72f), new Vector2(480f, 58f));
                SetTextLayout(safeAreaRoot, "Revive/Revive Title", 34);
                SetPointLayout(safeAreaRoot, "Revive/Revive Height", new Vector2(0.5f, 0.60f), new Vector2(260f, 86f));
                SetTextLayout(safeAreaRoot, "Revive/Revive Height", 72);
                SetPointLayout(safeAreaRoot, "Revive/Revive Resources", new Vector2(0.5f, 0.50f), new Vector2(400f, 42f));
                SetTextLayout(safeAreaRoot, "Revive/Revive Resources", 18);
                SetCommandButtonLayout("Revive/Rescue Button", new Vector2(0.5f, 0.385f), new Vector2(330f, 62f), 18);
                SetCommandButtonLayout("Revive/Use Life Button", new Vector2(0.5f, 0.275f), new Vector2(290f, 56f), 17);
                SetCommandButtonLayout("Revive/End Run Button", new Vector2(0.5f, 0.175f), new Vector2(230f, 50f), 16);
                SetPointLayout(safeAreaRoot, "Rescue/Rescue Count", new Vector2(0.5f, 0.59f), new Vector2(250f, 104f));
                SetTextLayout(safeAreaRoot, "Rescue/Rescue Count", 80);
                SetPointLayout(safeAreaRoot, "Rescue/Rescue Label", new Vector2(0.5f, 0.425f), new Vector2(280f, 44f));
                SetTextLayout(safeAreaRoot, "Rescue/Rescue Label", 21);
                return;
            }

            SetPointLayout(safeAreaRoot, "Revive/Revive Title", new Vector2(0.5f, 0.67f), new Vector2(480f, 78f));
            SetTextLayout(safeAreaRoot, "Revive/Revive Title", 38);
            SetPointLayout(safeAreaRoot, "Revive/Revive Height", new Vector2(0.5f, 0.56f), new Vector2(300f, 120f));
            SetTextLayout(safeAreaRoot, "Revive/Revive Height", 88);
            SetPointLayout(safeAreaRoot, "Revive/Revive Resources", new Vector2(0.5f, 0.47f), new Vector2(460f, 56f));
            SetTextLayout(safeAreaRoot, "Revive/Revive Resources", 22);
            SetCommandButtonLayout("Revive/Rescue Button", new Vector2(0.5f, 0.36f), new Vector2(360f, 82f), 20);
            SetCommandButtonLayout("Revive/Use Life Button", new Vector2(0.5f, 0.28f), new Vector2(320f, 74f), 20);
            SetCommandButtonLayout("Revive/End Run Button", new Vector2(0.5f, 0.20f), new Vector2(250f, 66f), 20);
            SetPointLayout(safeAreaRoot, "Rescue/Rescue Count", new Vector2(0.5f, 0.55f), new Vector2(300f, 150f));
            SetTextLayout(safeAreaRoot, "Rescue/Rescue Count", 112);
            SetPointLayout(safeAreaRoot, "Rescue/Rescue Label", new Vector2(0.5f, 0.46f), new Vector2(300f, 60f));
            SetTextLayout(safeAreaRoot, "Rescue/Rescue Label", 24);
        }

        private void SetResourceRowLayout(string path, Vector2 anchor, Vector2 rowSize, int valueSize, int iconSize)
        {
            SetPointLayout(safeAreaRoot, path, anchor, rowSize);
            SetPointLayout(safeAreaRoot, path + "/Value", new Vector2(0.35f, 0.5f),
                new Vector2(rowSize.x * 0.56f, rowSize.y));
            SetPointLayout(safeAreaRoot, path + "/Icon", new Vector2(0.79f, 0.5f),
                new Vector2(rowSize.x * 0.30f, rowSize.y));
            SetTextLayout(safeAreaRoot, path + "/Value", valueSize);
            SetTextLayout(safeAreaRoot, path + "/Icon", iconSize);
        }

        private void SetIconButtonLayout(string path, Vector2 anchor, Vector2 dimensions, bool hasLabel,
            int iconSize, int labelSize)
        {
            SetPointLayout(safeAreaRoot, path, anchor, dimensions);
            float iconY = hasLabel ? 0.64f : 0.5f;
            SetPointLayout(safeAreaRoot, path + "/Icon", new Vector2(0.5f, iconY),
                new Vector2(dimensions.x, hasLabel ? dimensions.y * 0.56f : dimensions.y));
            SetTextLayout(safeAreaRoot, path + "/Icon", iconSize);
            if (!hasLabel)
                return;
            SetPointLayout(safeAreaRoot, path + "/Label", new Vector2(0.5f, 0.18f),
                new Vector2(dimensions.x * 0.92f, dimensions.y * 0.28f));
            SetTextLayout(safeAreaRoot, path + "/Label", labelSize);
        }

        private void SetCommandButtonLayout(string path, Vector2 anchor, Vector2 dimensions, int labelSize)
        {
            SetPointLayout(safeAreaRoot, path, anchor, dimensions);
            SetPointLayout(safeAreaRoot, path + "/Label", new Vector2(0.5f, 0.5f), dimensions);
            SetTextLayout(safeAreaRoot, path + "/Label", labelSize);
        }

        private static void SetPointAnchor(Transform root, string path, Vector2 anchor)
        {
            RectTransform rect = root.Find(path) as RectTransform;
            if (rect == null)
                return;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
        }

        private static void SetPointLayout(Transform root, string path, Vector2 anchor, Vector2 dimensions)
        {
            RectTransform rect = root.Find(path) as RectTransform;
            if (rect == null)
                return;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = dimensions;
        }

        private static void SetTextLayout(Transform root, string path, int fontSize)
        {
            Text text = root.Find(path)?.GetComponent<Text>();
            if (text == null)
                return;
            text.fontSize = fontSize;
            text.resizeTextMinSize = Mathf.Max(10, fontSize / 2);
            text.resizeTextMaxSize = fontSize;
        }

        private static void SetStretchAnchors(Transform root, string path, Vector2 anchorMin, Vector2 anchorMax)
        {
            RectTransform rect = root.Find(path) as RectTransform;
            if (rect == null)
                return;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void TickTransientText(ref float timer, Text text, bool hideObject = false)
        {
            if (timer <= 0f || text == null)
                return;
            timer -= Time.unscaledDeltaTime;
            if (timer > 0f)
                return;
            text.text = string.Empty;
            if (hideObject)
                text.gameObject.SetActive(false);
        }

        private Font CreateUiFont()
        {
            string[] candidates = { "PingFang SC", "Microsoft YaHei", "Noto Sans CJK SC", "Arial" };
            Font dynamicFont = Font.CreateDynamicFontFromOSFont(candidates, 48);
            if (dynamicFont != null)
            {
                ownsFont = true;
                return dynamicFont;
            }
            ownsFont = false;
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        private GameObject CreateGroup(Transform parent, string name)
        {
            return CreateRectObject(parent, name, Vector2.zero, Vector2.one);
        }

        private static GameObject CreateRectObject(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.layer = parent.gameObject.layer;
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = gameObject.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return gameObject;
        }

        private static GameObject CreatePointObject(Transform parent, string name, Vector2 anchor, Vector2 size)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.layer = parent.gameObject.layer;
            gameObject.transform.SetParent(parent, false);
            RectTransform rect = gameObject.AddComponent<RectTransform>();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            return gameObject;
        }

        private Text CreatePointText(Transform parent, string name, string value, int size, Vector2 anchor,
            Vector2 dimensions, TextAnchor alignment, Color color)
        {
            GameObject textObject = CreatePointObject(parent, name, anchor, dimensions);
            return ConfigureText(textObject, value, size, alignment, color);
        }

        private Text CreateStretchText(Transform parent, string name, string value, int size, Vector2 anchorMin,
            Vector2 anchorMax, TextAnchor alignment, Color color)
        {
            GameObject textObject = CreateRectObject(parent, name, anchorMin, anchorMax);
            return ConfigureText(textObject, value, size, alignment, color);
        }

        private Text ConfigureText(GameObject gameObject, string value, int size, TextAnchor alignment, Color color)
        {
            Text text = gameObject.AddComponent<Text>();
            text.font = uiFont;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(10, size / 2);
            text.resizeTextMaxSize = size;
            text.raycastTarget = false;
            return text;
        }

        private Button CreateIconButton(Transform parent, string name, string glyph, string label, Vector2 anchor,
            Vector2 dimensions, Action callback)
        {
            GameObject buttonObject = CreatePointObject(parent, name + " Button", anchor, dimensions);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.001f);
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => callback?.Invoke());
            float iconY = string.IsNullOrEmpty(label) ? 0.5f : 0.63f;
            int iconSize = glyph.Contains("\n") ? 24 : 39;
            CreatePointText(buttonObject.transform, "Icon", glyph, iconSize, new Vector2(0.5f, iconY),
                new Vector2(dimensions.x, string.IsNullOrEmpty(label) ? dimensions.y : dimensions.y * 0.58f),
                TextAnchor.MiddleCenter, Color.white);
            if (!string.IsNullOrEmpty(label))
                CreatePointText(buttonObject.transform, "Label", label, 17, new Vector2(0.5f, 0.17f),
                    new Vector2(dimensions.x, 38f), TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.82f));
            return button;
        }

        private Button CreateCommandButton(Transform parent, string name, string label, Vector2 anchor, Vector2 dimensions,
            Action callback, Color background)
        {
            GameObject buttonObject = CreatePointObject(parent, name + " Button", anchor, dimensions);
            Image image = buttonObject.AddComponent<Image>();
            image.color = background;
            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(() => callback?.Invoke());
            CreatePointText(buttonObject.transform, "Label", label, 20, new Vector2(0.5f, 0.5f), dimensions,
                TextAnchor.MiddleCenter, Color.white);
            return button;
        }
    }
}
