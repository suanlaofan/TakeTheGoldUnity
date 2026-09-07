using System;
using System.Collections.Generic;
using System.IO;
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
    /// Routes synthetic press/release samples through the installed XRI UIInputModule,
    /// EventSystem raycasters and real scene Buttons. Does not call Button.onClick directly.
    /// Economy paths use a temporary profile; original preferences and save-cache snapshots
    /// are restored in finally, within the same synchronous editor frame.
    /// </summary>
    public static class StacklineEndScreenValidation
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.NonPublic;
        private static object Read(object target, string name) => target.GetType().GetField(name, Fields).GetValue(target);
        private static void Write(object target, string name, object value) => target.GetType().GetField(name, Fields).SetValue(target, value);
        private static void Call(object target, string name) => target.GetType().GetMethod(name, Fields).Invoke(target, null);

        [Serializable] private sealed class Report
        {
            public string utc, status, error;
            public int assertions;
            public bool preferencesRestored;
            public List<string> checks = new List<string>();
            public string scope = "Editor Play Mode: synthetic mouse/tracked-ray press and release through the actual XRI UIInputModule, raycasters and already rendered live Buttons. Not physical input or PICO device acceptance. Temporary profile restored in one synchronous frame; countdown timing is covered separately.";
        }

        private static StacklineClassicController Controller() => Object.FindAnyObjectByType<StacklineClassicController>();

        // Call CheckReviveAction on a later rendered frame. Activating a Canvas group and
        // clicking it synchronously can leave Graphic.depth == -1 until the renderer runs.
        public static string PrepareRevive(int displayedLives)
        {
            var controller = Controller();
            if (!Application.isPlaying || EditorApplication.isPaused || controller == null ||
                controller.gameObject.scene.path != "Assets/Scenes/StacklineVR.unity")
                return "NOT_STARTED: require unpaused StacklineVR.";
            controller.RestartRun();
            Call(controller, "FailCurrentBlock");
            controller.StopAllCoroutines();
            Write(controller, "state", StacklineClassicController.GameState.Revive);
            var hud = (StacklineHud)Read(controller, "hud");
            // Only the displayed test availability changes here. The player's actual profile
            // and resource values remain untouched between native Editor calls.
            hud.ShowRevive(controller.Height, displayedLives, controller.Gems);
            return "PREPARED at frame=" + Time.frameCount + "; wait for a rendered frame before clicking.";
        }

        public static string CheckReviveAction(string action, bool tracked)
        {
            var controller = Controller();
            if (!Application.isPlaying || EditorApplication.isPaused || controller == null ||
                controller.gameObject.scene.path != "Assets/Scenes/StacklineVR.unity" ||
                controller.State != StacklineClassicController.GameState.Revive)
                return "NOT_STARTED: require the prepared Revive screen.";
            var hud = (StacklineHud)Read(controller, "hud");
            var canvas = (Canvas)Read(hud, "canvas");
            var screenRay = canvas.GetComponent<GraphicRaycaster>();
            var trackedRay = canvas.GetComponent<TrackedDeviceGraphicRaycaster>();
            var es = EventSystem.current;
            var module = es != null ? es.GetComponent<XRUIInputModule>() : null;
            string buttonName = action == "Disabled Life" ? "Use Life" : action;
            var button = ((GameObject)Read(hud, "reviveGroup")).transform.Find(buttonName + " Button").GetComponent<Button>();
            if (module == null || button.targetGraphic.depth < 0 || !button.gameObject.activeInHierarchy)
                return "NOT_STARTED: wait for the Revive canvas to render.";

            // No raw profile data is written into the report.
            string[] keys = { StacklineProfileStore.ProfileKey, StacklineProfileStore.LanguageKey };
            bool[] existed = Array.ConvertAll(keys, PlayerPrefs.HasKey);
            string[] values = Array.ConvertAll(keys, key => PlayerPrefs.GetString(key, ""));
            object savedProfile = Read(controller, "profile");
            int savedGems = controller.Gems, savedLives = controller.Lives, savedStars = controller.Stars;
            var cacheProfile = typeof(StacklineProfileStore).GetField("lastSavedProfile", StaticFields);
            var cacheJson = typeof(StacklineProfileStore).GetField("lastSavedJson", StaticFields);
            var cached = (StacklineProfileData)cacheProfile.GetValue(null);
            var savedCache = cached == null ? null : JsonUtility.FromJson<StacklineProfileData>(JsonUtility.ToJson(cached));
            object savedCacheJson = cacheJson.GetValue(null);
            bool savedEnabled = controller.enabled, savedRayEnabled = screenRay != null && screenRay.enabled;
            GameObject savedSelection = es.currentSelectedGameObject;
            int frame = Time.frameCount;
            var report = new Report { utc = DateTime.UtcNow.ToString("O"), status = "RUNNING" };
            var probe = new StacklineProfileData { gems = 2, lives = 2, language = StacklineProfileStore.LoadLanguageCode() };
            void Assert(bool ok, string message)
            {
                if (!ok) throw new InvalidOperationException(message);
                report.checks.Add(message); report.assertions++;
            }
            try
            {
                controller.enabled = false;
                Assert(screenRay != null && screenRay.enabled, "World-space canvas accepts mouse/touch rays");
                Assert(trackedRay != null && trackedRay.enabled, "World-space canvas retains tracked-device rays");
                Assert(module.enableMouseInput && module.enableTouchInput && module.enableXRInput,
                    "Single XRUIInputModule enables mouse, touch and XR inputs");
                Assert(canvas.worldCamera != null, "UI raycasters have the tracked camera");
                Write(controller, "profile", probe);
                Write(controller, "gems", 2); Write(controller, "stars", 0);

                Write(controller, "lives", action == "Use Life" ? 2 : 0);
                Assert((action != "Disabled Life") == button.IsInteractable(), "Button availability matches the test case");
                if (!tracked && action == "End Run")
                {
                    screenRay.enabled = false;
                    Assert(Click(module, button, false) == 0 && controller.State == StacklineClassicController.GameState.Revive,
                        "Pre-fix screen-raycaster configuration reproduces the missed click");
                    screenRay.enabled = true;
                }
                int calls = Click(module, button, tracked);
                Assert(calls == (action == "Disabled Life" ? 0 : 1), "One press/release delivers the expected number of Button callbacks");
                if (action == "Disabled Life")
                    Assert(controller.State == StacklineClassicController.GameState.Revive && controller.Lives == 0,
                        "Zero lives keeps revive disabled without consuming resources");
                else if (action == "Rescue")
                    Assert(controller.State == StacklineClassicController.GameState.Rescue, "Rescue button starts the countdown");
                else if (action == "Use Life")
                {
                    Assert(controller.State == StacklineClassicController.GameState.Resolving && controller.Lives == 1,
                        "Paid revive resumes play and consumes exactly one test life");
                    var persisted = JsonUtility.FromJson<StacklineProfileData>(PlayerPrefs.GetString(StacklineProfileStore.ProfileKey));
                    Assert(persisted.lives == 1, "Paid revive persists exactly one remaining test life");
                }
                else if (action == "End Run")
                {
                    Assert(controller.State == StacklineClassicController.GameState.GameOver && probe.runCount == 1,
                        "End Run finishes and records the test run exactly once");
                    Assert(!((GameObject)Read(hud, "reviveGroup")).activeSelf && ((GameObject)Read(hud, "menuGroup")).activeSelf,
                        "Finished-run menu replaces the revive overlay");
                }
                else throw new ArgumentException("Unknown action: " + action);
                Assert(Time.frameCount == frame, "Temporary test profile is confined to one synchronous frame");
                report.status = "PASS";
            }
            catch (Exception ex) { report.status = "FAIL"; report.error = ex.ToString(); }
            finally
            {
                controller.StopAllCoroutines();
                Write(controller, "profile", savedProfile);
                Write(controller, "gems", savedGems); Write(controller, "lives", savedLives); Write(controller, "stars", savedStars);
                bool changed = false;
                for (int i = 0; i < keys.Length; i++)
                {
                    if (PlayerPrefs.HasKey(keys[i]) == existed[i] && PlayerPrefs.GetString(keys[i], "") == values[i]) continue;
                    if (existed[i]) PlayerPrefs.SetString(keys[i], values[i]); else PlayerPrefs.DeleteKey(keys[i]);
                    changed = true;
                }
                if (changed) PlayerPrefs.Save();
                cacheProfile.SetValue(null, savedCache); cacheJson.SetValue(null, savedCacheJson);
                if (screenRay != null) screenRay.enabled = savedRayEnabled;
                hud.RefreshPersistentState();
                controller.enabled = savedEnabled;
                es.SetSelectedGameObject(savedSelection != null && savedSelection.activeInHierarchy ? savedSelection : null);
                report.preferencesRestored = true;
                for (int i = 0; i < keys.Length; i++)
                    report.preferencesRestored &= PlayerPrefs.HasKey(keys[i]) == existed[i] && PlayerPrefs.GetString(keys[i], "") == values[i];
                if (!report.preferencesRestored) { report.status = "FAIL"; report.error += " Preference restoration failed."; }
            }
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "outputs/pico/review/end-screen-fix-20260907/" + (tracked ? "tracked-" : "mouse-") + action.Replace(" ", "-") + ".json"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            string result = report.status + ": assertions=" + report.assertions + "; preferencesRestored=" + report.preferencesRestored + "; report=" + path;
            if (report.status == "PASS") Debug.Log("STACKLINE_END_SCREEN_PASS " + result);
            else Debug.LogError("STACKLINE_END_SCREEN_FAIL " + result + "\n" + report.error);
            return result;
        }

        public static string CheckFinishedMenu(bool tracked)
        {
            var controller = Controller();
            if (controller == null || controller.State != StacklineClassicController.GameState.GameOver)
                return "NOT_STARTED: require the finished-run menu after End Run.";
            var hud = (StacklineHud)Read(controller, "hud");
            var button = ((GameObject)Read(hud, "menuGroup")).transform.Find("Settings Button").GetComponent<Button>();
            if (button.targetGraphic.depth < 0) return "NOT_STARTED: wait for the finished menu to render.";
            var module = EventSystem.current.GetComponent<XRUIInputModule>();
            bool enabled = controller.enabled;
            try
            {
                controller.enabled = false;
                if (Click(module, button, tracked) != 1 || Read(hud, "currentPanel").ToString() != "Settings")
                    throw new InvalidOperationException("Finished-menu Settings click failed.");
                Call(controller, "HandlePrimaryInput");
                if (controller.State != StacklineClassicController.GameState.GameOver)
                    throw new InvalidOperationException("Modal failed to capture gameplay input.");
                hud.ClosePanel();
                controller.BeginFromMenu();
                if (controller.State != StacklineClassicController.GameState.Playing || controller.Height != 0)
                    throw new InvalidOperationException("Restart after finished menu failed.");
                return "PASS: finished-menu Settings click; modal input capture; restart at height 0.";
            }
            finally { hud.ClosePanel(); controller.ReturnToMenu(); controller.enabled = enabled; }
        }

        private static int Click(XRUIInputModule module, Button button, bool tracked)
        {
            int clicks = 0;
            UnityEngine.Events.UnityAction count = () => clicks++;
            button.onClick.AddListener(count);
            try
            {
                var camera = button.GetComponentInParent<Canvas>().worldCamera;
                if (tracked)
                {
                    var model = new TrackedDeviceModel(-61002)
                    {
                        position = camera.transform.position,
                        orientation = Quaternion.LookRotation(button.transform.position - camera.transform.position),
                        raycastLayerMask = ~0,
                        raycastPoints = new List<Vector3> { camera.transform.position,
                            button.transform.position + (button.transform.position - camera.transform.position).normalized }
                    };
                    var process = typeof(UIInputModule).GetMethod("ProcessTrackedDevice", Fields);
                    model.select = true;
                    object[] args = { model, true };
                    process.Invoke(module, args);
                    model = (TrackedDeviceModel)args[0];
                    model.select = false; args[0] = model;
                    process.Invoke(module, args);
                    model = (TrackedDeviceModel)args[0];
                    model.raycastPoints.Clear(); model.raycastPoints.Add(camera.transform.position);
                    model.raycastPoints.Add(camera.transform.position - camera.transform.forward);
                    args[0] = model; process.Invoke(module, args);
                }
                else
                {
                    // XRI's PointerModel is internal; reflection is confined to this Editor-only
                    // harness. Down/up still traverse its production pointer event dispatcher.
                    Type type = typeof(UIInputModule).Assembly.GetType("UnityEngine.XR.Interaction.Toolkit.UI.PointerModel", true);
                    object model = Activator.CreateInstance(type, new object[] { -61001 });
                    var position = type.GetProperty("position");
                    var left = type.GetProperty("leftButton");
                    var process = typeof(UIInputModule).GetMethod("ProcessPointerState", Fields);
                    position.SetValue(model, (Vector2)camera.WorldToScreenPoint(button.transform.position));
                    var mouse = (MouseButtonModel)left.GetValue(model);
                    mouse.isDown = true; left.SetValue(model, mouse);
                    object[] args = { model }; process.Invoke(module, args);
                    model = args[0]; mouse = (MouseButtonModel)left.GetValue(model);
                    mouse.isDown = false; left.SetValue(model, mouse);
                    args[0] = model; process.Invoke(module, args);
                    model = args[0]; position.SetValue(model, new Vector2(-1f, -1f));
                    args[0] = model; process.Invoke(module, args);
                }
            }
            finally { button.onClick.RemoveListener(count); }
            return clicks;
        }
    }
}
