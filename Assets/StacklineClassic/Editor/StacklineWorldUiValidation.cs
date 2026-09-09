using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Wukong.StacklineClassic;
using Object = UnityEngine.Object;

namespace Wukong.EditorTools
{
    /// <summary>
    /// Read-only-to-profile, multi-frame UI fixture. Start at the generated VR scene Menu,
    /// then poll Status. Only HUD display methods run; no game, purchase or save action runs.
    /// Tests world pose after real rendered frames, while camera pose drivers/input are
    /// temporarily disabled. Abort, timeout, reload and Play Mode exit all restore them.
    /// </summary>
    [InitializeOnLoad]
    public static class StacklineWorldUiValidation
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly string[] Stages = { "Menu", "Playing", "Results", "Revive", "Rescue",
            "Settings", "Challenges", "Themes", "Leaderboard", "AdBonus", "LifeShop" };
        private static readonly string[] PrefKeys = { StacklineProfileStore.ProfileKey, StacklineProfileStore.LanguageKey,
            "Wukong.StacklineClassic.Best", "Wukong.StacklineClassic.Gems", "Wukong.StacklineClassic.Lives" };
        private static StacklineClassicController controller;
        private static StacklineHud hud;
        private static Canvas canvas;
        private static Camera camera;
        private static Report report;
        private static bool running, savedControllerEnabled, savedInteractionBlocked;
        private static int stage, frame;
        private static double deadline;
        private static Vector3 savedCameraPosition, savedCameraScale, worldPosition, worldScale, targetCameraPosition;
        private static Quaternion savedCameraRotation, worldRotation, targetCameraRotation;
        private static string savedProfile;
        private static int savedGems, savedLives, savedStars, savedHeight, savedDisplayScore, savedBest, savedReviveLives;
        private static bool savedNewRecord, savedCompleted;
        private static readonly List<EnabledState> disabled = new List<EnabledState>();
        private static readonly List<TextState> texts = new List<TextState>();
        private static readonly List<Preference> preferences = new List<Preference>();
        private static GameObject savedSelection;
        private static EventSystem savedEventSystem;
        private static bool savedLifeInteractable;

        [Serializable] private sealed class Report
        {
            public string status, phase, error, utc;
            public int assertions, stagesPassed, rootGraphics, sceneCanvases;
            public bool restored, preferencesUnchanged, profileUnchanged;
            public List<string> checks = new List<string>();
            public string scope = "Editor runtime structure and real-frame camera movement; all HUD states and six modals. No save/economy calls or physical PICO input acceptance.";
        }
        private sealed class EnabledState { public Behaviour component; public bool enabled; }
        private sealed class TextState { public Text component; public string text; }
        private sealed class Preference { public bool exists; public string value; public int number; }

        static StacklineWorldUiValidation()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Abort;
            EditorApplication.quitting += Abort;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode) Abort();
            };
        }

        private static object Read(object target, string name) => target.GetType().GetField(name, Fields).GetValue(target);
        private static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Fields).Invoke(target, args);

        public static string Start()
        {
            if (running) return Status();
            controller = Object.FindAnyObjectByType<StacklineClassicController>();
            if (!Application.isPlaying || EditorApplication.isPaused || controller == null ||
                controller.gameObject.scene.path != "Assets/Scenes/StacklineVR.unity" ||
                controller.State != StacklineClassicController.GameState.Menu)
                return "NOT_STARTED: require unpaused StacklineVR Play Mode at Menu.";
            hud = (StacklineHud)Read(controller, "hud");
            if (hud == null || !hud.enabled || Read(hud, "currentPanel").ToString() != "None")
                return "NOT_STARTED: require enabled HUD with closed modal.";
            canvas = (Canvas)Read(hud, "canvas");
            camera = (Camera)Read(controller, "gameplayCamera");
            if (canvas == null || camera == null || Time.frameCount < 2)
                return "NOT_STARTED: wait for HUD/camera initialization.";

            disabled.Clear(); texts.Clear(); preferences.Clear();
            savedControllerEnabled = controller.enabled;
            savedInteractionBlocked = (bool)Read(controller, "menuInteractionBlocked");
            savedCameraPosition = camera.transform.localPosition;
            savedCameraRotation = camera.transform.localRotation;
            savedCameraScale = camera.transform.localScale;
            savedProfile = JsonUtility.ToJson(Read(controller, "profile"));
            savedGems = controller.Gems; savedLives = controller.Lives; savedStars = controller.Stars; savedHeight = controller.Height;
            savedDisplayScore = (int)Read(hud, "lastMenuDisplayScore"); savedBest = (int)Read(hud, "lastMenuBest");
            savedReviveLives = (int)Read(hud, "lastReviveLives");
            savedNewRecord = (bool)Read(hud, "lastMenuIsNewRecord"); savedCompleted = (bool)Read(hud, "lastMenuHasCompletedRun");
            savedLifeInteractable = ((Button)Read(hud, "lifeButton")).interactable;
            savedEventSystem = EventSystem.current;
            savedSelection = savedEventSystem != null ? savedEventSystem.currentSelectedGameObject : null;
            foreach (var text in canvas.GetComponentsInChildren<Text>(true))
                texts.Add(new TextState { component = text, text = text.text });
            for (int i = 0; i < PrefKeys.Length; i++) preferences.Add(new Preference { exists = PlayerPrefs.HasKey(PrefKeys[i]),
                value = i < 2 ? PlayerPrefs.GetString(PrefKeys[i], "") : null, number = i >= 2 ? PlayerPrefs.GetInt(PrefKeys[i], 0) : 0 });

            report = new Report { status = "RUNNING", utc = DateTime.UtcNow.ToString("O") };
            running = true;
            deadline = EditorApplication.timeSinceStartup + 30;
            EditorApplication.update += Tick;
            try
            {
                controller.enabled = false;
                foreach (var input in Object.FindObjectsByType<BaseInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (input.gameObject.scene == controller.gameObject.scene) Disable(input);
                foreach (var behaviour in camera.GetComponents<Behaviour>())
                    if (behaviour != null && behaviour.GetType().Name == "TrackedPoseDriver") Disable(behaviour);
                worldPosition = canvas.transform.position; worldRotation = canvas.transform.rotation; worldScale = canvas.transform.lossyScale;
                Check(canvas.transform.parent == null, "HUD is an independent world root");
                Check((bool)Read(controller, "xrMode"), "Generated scene passes XR mode to HUD");
                Check(Vector3.Distance(worldScale, Vector3.one * 0.0025f) < 0.000001f, "Canvas retains its 0.0025 physical scale");
                Transform authoredAnchor = (Transform)Read(hud, "worldUiAnchor");
                Check(authoredAnchor != null && authoredAnchor.parent == null &&
                    Vector3.Distance(authoredAnchor.position, worldPosition) < 0.0001f &&
                    Quaternion.Angle(authoredAnchor.rotation, worldRotation) < 0.01f,
                    "Canvas uses the authored world anchor independently of current HMD pose");
                var card = (RectTransform)((RectTransform)Read(hud, "safeAreaRoot")).Find("Menu/Menu Card");
                Check(card != null && Mathf.Abs(card.anchorMin.x - 0.16f) < 0.0001f && Mathf.Abs(card.anchorMax.x - 0.16f) < 0.0001f,
                    "Left menu retains the requested 0.16 horizontal anchor");
                stage = 0;
                PrepareStage();
            }
            catch (Exception ex) { Fail(ex); }
            return Status();
        }

        private static void Disable(Behaviour component)
        {
            disabled.Add(new EnabledState { component = component, enabled = component.enabled });
            component.enabled = false;
        }

        private static void PrepareStage()
        {
            report.phase = Stages[stage];
            switch (stage)
            {
                case 0: hud.ShowMenu(0, savedBest, savedGems, savedLives, savedStars, false, false); break;
                case 1: hud.ShowPlaying(7, savedGems, savedLives, savedStars); break;
                case 2: hud.ShowGameOver(7, savedBest, savedGems, savedLives, savedStars, false); break;
                case 3: hud.ShowRevive(7, savedLives, savedGems); break;
                case 4: hud.ShowRescue(3); break;
                default:
                    hud.ShowMenu(0, savedBest, savedGems, savedLives, savedStars, false, false);
                    Type panelType = typeof(StacklineHud).GetNestedType("MenuPanel", BindingFlags.NonPublic);
                    Call(hud, "OpenPanel", Enum.Parse(panelType, Stages[stage]));
                    break;
            }
            // Alternating translation and yaw/pitch defeats both direct parenting and
            // scripts that copy the camera's world transform during Update/LateUpdate.
            float sign = stage % 2 == 0 ? 1f : -1f;
            targetCameraPosition = savedCameraPosition + new Vector3(sign * 0.45f, 0.12f, sign * 0.18f);
            targetCameraRotation = savedCameraRotation * Quaternion.Euler(sign * 8f, sign * 24f, 0f);
            camera.transform.localPosition = targetCameraPosition;
            camera.transform.localRotation = targetCameraRotation;
            frame = Time.frameCount;
        }

        private static void Tick()
        {
            if (!running) return;
            try
            {
                if (!Application.isPlaying || camera == null || hud == null || canvas == null)
                    throw new InvalidOperationException("Play Mode or required scene objects disappeared.");
                if (EditorApplication.timeSinceStartup > deadline)
                    throw new TimeoutException("World UI probe exceeded 30 seconds; restored camera/input.");
                if (EditorApplication.isPaused || Time.frameCount < frame + 2) return;
                Check(Vector3.Distance(camera.transform.localPosition, targetCameraPosition) < 0.001f &&
                    Quaternion.Angle(camera.transform.localRotation, targetCameraRotation) < 0.05f,
                    Stages[stage] + ": translated/rotated camera survives two real frames");
                Check(Vector3.Distance(canvas.transform.position, worldPosition) < 0.0001f &&
                    Quaternion.Angle(canvas.transform.rotation, worldRotation) < 0.01f &&
                    Vector3.Distance(canvas.transform.lossyScale, worldScale) < 0.000001f,
                    Stages[stage] + ": root world pose stays fixed across Update/LateUpdate");
                CheckStructure();
                CheckProfile();
                report.stagesPassed++;
                stage++;
                if (stage == Stages.Length)
                {
                    report.status = "PASS"; report.phase = "Complete";
                    Restore();
                }
                else PrepareStage();
            }
            catch (Exception ex) { Fail(ex); }
        }

        private static void CheckStructure()
        {
            Check(canvas.renderMode == RenderMode.WorldSpace && canvas.rootCanvas == canvas && canvas.worldCamera == camera,
                Stages[stage] + ": HUD root uses WorldSpace and the tracked raycast camera");
            int count = 0;
            foreach (var candidate in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene != controller.gameObject.scene) continue;
                count++;
                Check(candidate.rootCanvas == canvas && candidate.renderMode == RenderMode.WorldSpace,
                    Stages[stage] + ": active/inactive Canvas inherits the world root: " + candidate.name);
                for (Transform parent = candidate.transform; parent != null; parent = parent.parent)
                    if (parent.GetComponent<Camera>() != null)
                        throw new InvalidOperationException("Canvas has a camera ancestor: " + candidate.name);
            }
            report.sceneCanvases = count;
            string[] groups = { "menuGroup", "gameGroup", "reviveGroup", "rescueGroup", "panelRoot" };
            foreach (string field in groups)
            {
                var group = (GameObject)Read(hud, field);
                Check(group != null && group.transform.IsChildOf(canvas.transform), "Required UI group belongs to world root: " + field);
            }
            string active = stage == 1 ? "gameGroup" : stage == 3 ? "reviveGroup" : stage == 4 ? "rescueGroup" : "menuGroup";
            Check(((GameObject)Read(hud, active)).activeInHierarchy, Stages[stage] + ": expected HUD group is active");
            if (stage >= 5)
                Check(((GameObject)Read(hud, "panelRoot")).activeInHierarchy &&
                    ((RectTransform)Read(hud, "panelContent")).childCount > 0 && Read(hud, "currentPanel").ToString() == Stages[stage],
                    Stages[stage] + ": modal content has been built by runtime LateUpdate");
            int graphics = 0;
            foreach (var graphic in Object.FindObjectsByType<Graphic>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (graphic.gameObject.scene != controller.gameObject.scene) continue;
                graphics++;
                // Inactive Graphic.canvas may be uncached. Resolve its ancestors including
                // inactive groups instead of mistaking a null cached canvas for ScreenSpace.
                Canvas owner = graphic.GetComponentInParent<Canvas>(true);
                if (!graphic.transform.IsChildOf(canvas.transform) || owner == null || owner.rootCanvas != canvas)
                    throw new InvalidOperationException("UI Graphic escaped the world root: " + graphic.name);
            }
            report.rootGraphics = graphics;
            Check(graphics > 0, Stages[stage] + ": all active/inactive scene graphics stay in the world UI hierarchy");
        }

        private static void CheckProfile()
        {
            report.preferencesUnchanged = true;
            for (int i = 0; i < PrefKeys.Length; i++)
                report.preferencesUnchanged &= PlayerPrefs.HasKey(PrefKeys[i]) == preferences[i].exists &&
                    (!preferences[i].exists || (i < 2 ? PlayerPrefs.GetString(PrefKeys[i], "") == preferences[i].value :
                        PlayerPrefs.GetInt(PrefKeys[i], 0) == preferences[i].number));
            report.profileUnchanged = JsonUtility.ToJson(Read(controller, "profile")) == savedProfile &&
                controller.Gems == savedGems && controller.Lives == savedLives && controller.Stars == savedStars &&
                controller.Height == savedHeight && controller.State == StacklineClassicController.GameState.Menu;
            Check(report.preferencesUnchanged && report.profileUnchanged, "Player profile, preferences and game state are unchanged");
        }

        private static void Check(bool ok, string name)
        {
            if (!ok) throw new InvalidOperationException(name);
            report.assertions++; report.checks.Add(name);
        }
        private static void Fail(Exception ex)
        {
            report.status = "FAIL";
            report.error = (ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex).ToString();
            Restore();
        }
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
            EditorApplication.update -= Tick;
            bool restoredWithoutError = true;
            try
            {
                if (camera != null)
                {
                    camera.transform.localPosition = savedCameraPosition;
                    camera.transform.localRotation = savedCameraRotation;
                    camera.transform.localScale = savedCameraScale;
                }
                if (hud != null)
                {
                    hud.ClosePanel();
                    hud.ShowMenu(savedDisplayScore, savedBest, savedGems, savedLives, savedStars, savedNewRecord, savedCompleted);
                    typeof(StacklineHud).GetField("lastReviveLives", Fields).SetValue(hud, savedReviveLives);
                    ((Button)Read(hud, "lifeButton")).interactable = savedLifeInteractable;
                    foreach (var item in texts) if (item.component != null) item.component.text = item.text;
                }
                if (controller != null)
                {
                    controller.SetMenuInteractionBlocked(savedInteractionBlocked);
                    CheckProfile();
                }
            }
            catch (Exception ex)
            {
                restoredWithoutError = false;
                report.status = "FAIL";
                report.error = (report.error ?? "") + "\nRestore: " + ex;
            }
            finally
            {
                foreach (var item in disabled) if (item.component != null) item.component.enabled = item.enabled;
                if (controller != null) controller.enabled = savedControllerEnabled;
                if (savedEventSystem != null) savedEventSystem.SetSelectedGameObject(savedSelection != null && savedSelection.activeInHierarchy ? savedSelection : null);
                report.restored = restoredWithoutError;
                Debug.Log("STACKLINE_WORLD_UI_" + report.status + " " + JsonUtility.ToJson(report));
            }
        }
    }
}
