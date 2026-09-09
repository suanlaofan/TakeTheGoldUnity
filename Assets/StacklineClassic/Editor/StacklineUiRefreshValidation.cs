using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Wukong.StacklineClassic;
using Object = UnityEngine.Object;

namespace Wukong.EditorTools
{
    /// <summary>
    /// Staged native UI checks. Prepare, CheckStart and CheckGameplay must run on separate
    /// rendered frames. Starts a one-floor disposable run; never finishes a run, spends,
    /// rewards, changes settings or invokes a profile save. The existing XRI input harness
    /// supplies real pointer/tracked-ray down/up dispatch rather than Button.onClick.Invoke.
    /// </summary>
    [InitializeOnLoad]
    public static class StacklineUiRefreshValidation
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly MethodInfo ClickMethod = typeof(StacklineEndScreenValidation)
            .GetMethod("Click", BindingFlags.Static | BindingFlags.NonPublic);
        private static StacklineClassicController controller;
        private static StacklineHud hud;
        private static Canvas canvas;
        private static bool savedEnabled, preparedCompleted;
        private static bool running;
        private static int preparedFrame, startedFrame;
        private static string savedProfile;
        private static int savedGems, savedLives, savedStars;
        private static GameObject savedSelection;
        private static Preference[] preferences;
        private static Report report;

        [Serializable] private sealed class Report
        {
            public string status, phase, error, input;
            public int assertions, buttonCallbacks, heightAfterStart, heightAfterPlacement;
            public bool completedMenu, preferencesUnchanged, profileUnchanged;
            public List<string> checks = new List<string>();
            public string scope = "Editor native XRI mouse/tracked-ray dispatch; one disposable floor; no economy/save API. Game HUD pass-through uses actual EventSystem rays plus controller input. No physical PICO input, device GPU/FPS or visual acceptance.";
        }
        private sealed class Preference
        {
            public string key, value;
            public bool exists, integer;
            public int number;
        }

        static StacklineUiRefreshValidation()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Abort;
            EditorApplication.quitting += Abort;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Abort();
            };
        }

        private static object Read(object target, string name) => target.GetType().GetField(name, Fields).GetValue(target);
        private static void Write(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
        private static void Call(object target, string name) => target.GetType().GetMethod(name, Fields).Invoke(target, null);

        public static string Prepare(bool completedMenu)
        {
            if (running) return "NOT_STARTED: finish or Abort the current staged check first.";
            controller = Object.FindAnyObjectByType<StacklineClassicController>();
            if (!Application.isPlaying || EditorApplication.isPaused || controller == null ||
                controller.gameObject.scene.path != "Assets/Scenes/StacklineVR.unity" ||
                controller.State != StacklineClassicController.GameState.Menu)
                return "NOT_STARTED: require unpaused generated StacklineVR at Menu.";
            hud = (StacklineHud)Read(controller, "hud");
            if (hud == null || Read(hud, "currentPanel").ToString() != "None")
                return "NOT_STARTED: close the modal before preparing.";
            canvas = (Canvas)Read(hud, "canvas");
            savedEnabled = controller.enabled;
            savedProfile = JsonUtility.ToJson(Read(controller, "profile"));
            savedGems = controller.Gems; savedLives = controller.Lives; savedStars = controller.Stars;
            savedSelection = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            preferences = CapturePreferences();
            report = new Report { status = "RUNNING", phase = "Prepared", completedMenu = completedMenu };
            running = true;
            try
            {
                controller.enabled = false;
                controller.ReturnToMenu();
                preparedCompleted = completedMenu;
                if (completedMenu)
                {
                    // Show a completed-run fixture without calling FinishGame or changing profile data.
                    Write(controller, "state", StacklineClassicController.GameState.GameOver);
                    hud.ShowGameOver(7, 12, savedGems, savedLives, savedStars, false);
                }
                preparedFrame = Time.frameCount;
                return "PREPARED: " + (completedMenu ? "completed" : "initial") + " menu; frame=" + preparedFrame;
            }
            catch (Exception ex) { Fail(ex); return JsonUtility.ToJson(report); }
        }

        public static string CheckStart(bool tracked)
        {
            if (!running || report.phase != "Prepared") return "NOT_STARTED: call Prepare first.";
            if (Time.frameCount <= preparedFrame) return "NOT_STARTED: wait for a rendered frame.";
            var menu = (GameObject)Read(hud, "menuGroup");
            var button = menu.transform.Find("Begin Button")?.GetComponent<Button>();
            if (button == null || button.targetGraphic.depth < 0)
                return "NOT_STARTED: Begin Button has not rendered.";
            try
            {
                var module = EventSystem.current != null ? EventSystem.current.GetComponent<XRUIInputModule>() : null;
                Check(module != null && module.enableMouseInput && module.enableTouchInput && module.enableXRInput,
                    "One live XRUIInputModule supports mouse, touch and tracked input");
                Check(canvas.GetComponent<GraphicRaycaster>()?.enabled == true &&
                    canvas.GetComponent<TrackedDeviceGraphicRaycaster>()?.enabled == true,
                    "Both screen and tracked raycasters remain enabled");
                Check(canvas.worldCamera != null && ClickMethod != null, "Camera and native input dispatcher are available");
                CheckArtAndDecor();
                var language = (StacklineLanguage)Read(hud, "language");
                string expected = StacklineLocalization.Get(language, preparedCompleted ? StacklineText.PlayAgain : StacklineText.StartRun);
                Check(button.GetComponentInChildren<Text>().text == expected, "Begin CTA caption matches initial/completed menu state");
                Check(menu.transform.Find("Game Title").GetComponent<Text>().text ==
                    StacklineLocalization.Get(language, preparedCompleted ? StacklineText.RunComplete : StacklineText.GameTitle),
                    "Menu title distinguishes a completed run");
                report.input = tracked ? "tracked" : "mouse";
                report.buttonCallbacks = (int)ClickMethod.Invoke(null, new object[] { module, button, tracked });
                Check(report.buttonCallbacks == 1, "One real press/release delivers exactly one Begin callback through the card");
                report.heightAfterStart = controller.Height;
                Check(controller.State == StacklineClassicController.GameState.Playing && controller.Height == 0,
                    "Initial/completed CTA starts Playing at height zero");
                Check(((IList)Read(controller, "stack")).Count == 1 && Read(controller, "movingBlock") != null,
                    "Start creates exactly one foundation and one moving block");
                Call(controller, "HandlePrimaryInput");
                Call(controller, "HandlePrimaryInput");
                Check(controller.Height == 0 && ((IList)Read(controller, "stack")).Count == 1,
                    "Same-frame fallback input cannot also place the first block");
                CheckPreferences();
                startedFrame = Time.frameCount;
                report.phase = "Awaiting gameplay frame";
            }
            catch (Exception ex) { Fail(ex); }
            return JsonUtility.ToJson(report);
        }

        public static string CheckGameplay()
        {
            if (!running || report.phase != "Awaiting gameplay frame") return "NOT_STARTED: a successful CheckStart is required.";
            if (Time.frameCount <= startedFrame) return "NOT_STARTED: wait for the Game HUD to render.";
            try
            {
                var game = (GameObject)Read(hud, "gameGroup");
                var safe = (RectTransform)Read(hud, "safeAreaRoot");
                Check(game.activeInHierarchy && !hud.CapturesPrimaryInput && !(bool)Read(controller, "menuInteractionBlocked"),
                    "Game HUD does not capture placement input");
                foreach (var graphic in safe.GetComponentsInChildren<Graphic>(false))
                    Check(!graphic.raycastTarget, "Active gameplay graphic passes rays: " + graphic.name);
                CheckNoUiHit(game.transform.Find("Score Plaque"));
                CheckNoUiHit(safe.Find("Resources/Diamonds"));
                CheckNoUiHit(safe.Find("Resources/Lives"));
                CheckNoUiHit(safe.Find("Resources/Stars"));
                object moving = Read(controller, "movingBlock");
                IList stack = (IList)Read(controller, "stack");
                var obj = (GameObject)Read(moving, "Object");
                Vector3 position = obj.transform.localPosition;
                Vector3 support = (Vector3)Read(stack[stack.Count - 1], "Position");
                if ((bool)Read(controller, "moveOnX")) position.x = support.x; else position.z = support.z;
                obj.transform.localPosition = position;
                Call(controller, "HandlePrimaryInput");
                Call(controller, "HandlePrimaryInput");
                report.heightAfterPlacement = controller.Height;
                Check(controller.Height == 1 && stack.Count == 2 &&
                    controller.State == StacklineClassicController.GameState.Resolving,
                    "Gameplay input places exactly one first floor through the noninteractive HUD");
                CheckPreferences();
                report.status = "PASS"; report.phase = "Complete";
            }
            catch (Exception ex) { report.status = "FAIL"; report.error = Describe(ex); }
            finally { Restore(); }
            return JsonUtility.ToJson(report);
        }

        private static void CheckArtAndDecor()
        {
            var temple = Resources.Load<Sprite>("StacklineUI/TemplePanel");
            var score = Resources.Load<Sprite>("StacklineUI/ScorePlaque");
            Check(temple != null && score != null, "Both Resources UI sprites are loadable");
            Check((Sprite)Read(hud, "templePanel") == temple && (Sprite)Read(hud, "scorePlaque") == score,
                "HUD has loaded both authored Resources sprites");
            var safe = (Transform)Read(hud, "safeAreaRoot");
            string[] cards = { "Menu/Menu Card", "Game/Score Plaque", "Revive/Revive Card", "Rescue/Rescue Card" };
            foreach (string path in cards)
            {
                Transform card = safe.Find(path);
                Check(card != null, "Authored card exists: " + path);
                Check(card.GetComponent<Image>()?.sprite == (path.StartsWith("Game/") ? score : temple),
                    "Authored sprite is assigned: " + path);
                Check(card.Find("Reading Surface")?.GetComponent<Image>() != null, "Reading surface exists: " + path);
                foreach (var graphic in card.GetComponentsInChildren<Graphic>(true))
                    Check(!graphic.raycastTarget, "Card decoration cannot intercept input: " + path + "/" + graphic.name);
            }
            foreach (var graphic in safe.Find("Resources").GetComponentsInChildren<Graphic>(true))
                Check(!graphic.raycastTarget, "Resource display cannot intercept input: " + graphic.name);
            var modal = (GameObject)Read(hud, "panelRoot");
            foreach (var graphic in canvas.GetComponentsInChildren<Graphic>(true))
            {
                if (!graphic.raycastTarget) continue;
                var button = graphic.GetComponent<Button>();
                bool inputSurface = button != null && button.targetGraphic == graphic;
                inputSurface |= graphic.gameObject == modal;
                inputSurface |= graphic.transform == modal.transform.Find("Panel/Viewport");
                Check(inputSurface, "Raycast target is an intentional button/modal/scroll surface: " + graphic.name);
            }
        }

        private static void CheckNoUiHit(Transform target)
        {
            var point = new PointerEventData(EventSystem.current)
            { position = canvas.worldCamera.WorldToScreenPoint(target.position) };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(point, hits);
            foreach (var hit in hits)
                Check(hit.gameObject.GetComponent<Graphic>() == null,
                    "Actual EventSystem ray passes visible gameplay HUD at " + target.name);
            Check(true, "Visible gameplay HUD ray checked at " + target.name);
        }

        private static Preference[] CapturePreferences()
        {
            string[] keys = { StacklineProfileStore.ProfileKey, StacklineProfileStore.LanguageKey,
                "Wukong.StacklineClassic.Best", "Wukong.StacklineClassic.Gems", "Wukong.StacklineClassic.Lives" };
            var snapshot = new Preference[keys.Length];
            for (int i = 0; i < keys.Length; i++) snapshot[i] = new Preference { key = keys[i], exists = PlayerPrefs.HasKey(keys[i]),
                integer = i >= 2, number = i >= 2 ? PlayerPrefs.GetInt(keys[i], 0) : 0,
                value = i < 2 ? PlayerPrefs.GetString(keys[i], "") : null };
            return snapshot;
        }

        private static void CheckPreferences()
        {
            report.preferencesUnchanged = true;
            foreach (var p in preferences)
                report.preferencesUnchanged &= PlayerPrefs.HasKey(p.key) == p.exists &&
                    (!p.exists || (p.integer ? PlayerPrefs.GetInt(p.key, 0) == p.number : PlayerPrefs.GetString(p.key, "") == p.value));
            report.profileUnchanged = JsonUtility.ToJson(Read(controller, "profile")) == savedProfile &&
                controller.Gems == savedGems && controller.Lives == savedLives && controller.Stars == savedStars;
            Check(report.preferencesUnchanged && report.profileUnchanged, "Preferences, profile and all resource balances remain unchanged");
        }

        private static void Check(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
            report.assertions++; report.checks.Add(message);
        }
        private static string Describe(Exception ex) => (ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex).ToString();
        private static void Fail(Exception ex) { report.status = "FAIL"; report.error = Describe(ex); Restore(); }
        public static string Status() => report == null ? "IDLE" : JsonUtility.ToJson(report);
        public static void Abort()
        {
            if (!running) return;
            report.status = "ABORTED"; report.phase = "Aborted"; Restore();
        }
        private static void Restore()
        {
            if (!running) return;
            running = false;
            if (controller != null && Application.isPlaying)
            {
                controller.StopAllCoroutines();
                // These checks never call persistence paths. A failed invariant is reported;
                // restoration does not write or conceal a unexpected persistent mutation.
                controller.ReturnToMenu();
                controller.enabled = savedEnabled;
                if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(
                    savedSelection != null && savedSelection.activeInHierarchy ? savedSelection : null);
            }
        }
    }
}
