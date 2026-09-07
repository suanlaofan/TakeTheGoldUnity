namespace Wukong.EditorTools
{
    /// <summary>
    /// Editor-only HUD probes with short public entry points for MCP hosts whose snippet
    /// compiler is CodeDom/C# 6. Unity compiles the local-function implementations normally.
    /// No consumption, reward, setting-toggle, or save API is invoked by either probe.
    /// </summary>
    public static class StacklineHudPerformanceValidation
    {
        public static object CheckMenuCache()
        {
            if (!UnityEngine.Application.isPlaying || UnityEditor.EditorApplication.isPaused)
                return new { status = "NOT_STARTED", reason = "Use unpaused Play Mode at the StacklineVR Menu." };
            var hud = UnityEngine.Object.FindAnyObjectByType<Wukong.StacklineClassic.StacklineHud>();
            if (hud == null) return new { status = "NOT_STARTED", reason = "HUD missing." };
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var ht = hud.GetType();
            object Read(string name) { return ht.GetField(name, flags).GetValue(hud); }
            void Write(string name, object value) { ht.GetField(name, flags).SetValue(hud, value); }
            void Call(string name, params object[] args) { ht.GetMethod(name, flags).Invoke(hud, args); }
            var controller = (Wukong.StacklineClassic.StacklineClassicController)Read("controller");
            if (controller == null || controller.gameObject.scene.path != "Assets/Scenes/StacklineVR.unity" ||
                controller.State != Wukong.StacklineClassic.StacklineClassicController.GameState.Menu || Read("currentPanel").ToString() != "None")
                return new { status = "NOT_STARTED", reason = "Require closed modal at the generated VR scene Menu." };
            var ct = controller.GetType();
            var gemField = ct.GetField("gems", flags);
            int savedGems = (int)gemField.GetValue(controller);
            var savedLanguage = Read("language");
            bool savedEnabled = controller.enabled;
            var checks = new System.Collections.Generic.List<string>();
            var errors = new System.Collections.Generic.List<string>();
            string[] keys = { Wukong.StacklineClassic.StacklineProfileStore.ProfileKey, Wukong.StacklineClassic.StacklineProfileStore.LanguageKey };
            var prefExists = new bool[keys.Length];
            var prefValues = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++) { prefExists[i] = UnityEngine.PlayerPrefs.HasKey(keys[i]); prefValues[i] = UnityEngine.PlayerPrefs.GetString(keys[i], ""); }
            var content = (UnityEngine.RectTransform)Read("panelContent");
            var root = (UnityEngine.GameObject)Read("panelRoot");
            var panelType = ht.GetNestedType("MenuPanel", System.Reflection.BindingFlags.NonPublic);
            void Check(bool ok, string name) { if (!ok) throw new System.InvalidOperationException(name); checks.Add(name); }
            System.Collections.Generic.List<UnityEngine.GameObject> Children()
            {
                var list = new System.Collections.Generic.List<UnityEngine.GameObject>();
                for (int i = 0; i < content.childCount; i++) if (content.GetChild(i).gameObject.activeSelf) list.Add(content.GetChild(i).gameObject);
                return list;
            }
            string Fingerprint()
            {
                var sb = new System.Text.StringBuilder();
                foreach (var child in Children()) sb.Append(UnityEngine.EntityId.ToULong(child.GetEntityId())).Append('|');
                return sb.ToString();
            }
            void Open(string panel) { Call("OpenPanel", System.Enum.Parse(panelType, panel)); }
            void Flush() { Call("LateUpdate"); }
            int initialFrame = UnityEngine.Time.frameCount;
            try
            {
                // This entire probe is synchronous. Restore these temporary in-memory values before
                // Unity can deliver Update/pause/quit; never invoke an economy callback or save API.
                controller.enabled = false;
                gemField.SetValue(controller, 2);
                Open("LifeShop");
                Check(root.activeSelf && (bool)ct.GetField("menuInteractionBlocked", flags).GetValue(controller), "Opening modal blocks placement");
                Flush();
                string initial = Fingerprint();
                int childCount = Children().Count;
                var buttons = content.GetComponentsInChildren<UnityEngine.UI.Button>(false);
                Check(childCount == 4 && buttons.Length == 2, "Life shop contains the expected two actions and two text rows");
                Check(!buttons[0].interactable && !buttons[1].interactable, "Two gems keeps both life purchases disabled");
                hud.RefreshPersistentState(); hud.RefreshPersistentState();
                Check(Fingerprint() == initial && (bool)Read("panelRefreshPending"), "Two same-frame requests only queue a refresh");
                Flush();
                Check(Fingerprint() == initial && !(bool)Read("panelRefreshPending"), "Unchanged queued refresh preserves existing objects");
                Flush();
                Check(Fingerprint() == initial, "A second LateUpdate without a request performs no rebuild");
                hud.ClosePanel();
                Check(!root.activeSelf && !(bool)ct.GetField("menuInteractionBlocked", flags).GetValue(controller), "Closing modal restores placement input");
                Open("LifeShop"); Flush();
                Check(Fingerprint() == initial, "Reopening unchanged life shop reuses existing widgets");
                hud.RefreshPersistentState(); hud.ClosePanel(); Flush();
                Check(!root.activeSelf && !(bool)Read("panelRefreshPending") && Fingerprint() == initial, "Close cancels a queued refresh without reopening or rebuilding");
                Open("LifeShop"); Flush();
                var old = Children();
                gemField.SetValue(controller, 3);
                hud.RefreshPersistentState(); hud.RefreshPersistentState();
                Check(Fingerprint() == initial, "Changed data still waits for the single refresh point");
                Flush();
                string changed = Fingerprint();
                Check(changed != initial && Children().Count == childCount, "Displayed data change replaces content once without extra active rows");
                foreach (var child in old) Check(child == null || !child.activeSelf, "Replaced widget is immediately inactive");
                buttons = content.GetComponentsInChildren<UnityEngine.UI.Button>(false);
                Check(buttons.Length == 2 && buttons[0].interactable && !buttons[1].interactable, "Three gems enables exactly the affordable purchase");
                Flush(); Check(Fingerprint() == changed, "Second flush retains the newly built content");
                var english = Wukong.StacklineClassic.StacklineLanguage.English;
                Write("language", english);
                Call("ApplyStaticLocalization");
                hud.RefreshPersistentState(); Flush();
                string englishWidgets = Fingerprint();
                Check(englishWidgets != changed || savedLanguage.Equals(english), "Language changes invalidate cached captions");
                buttons = content.GetComponentsInChildren<UnityEngine.UI.Button>(false);
                Check(buttons[0].GetComponentInChildren<UnityEngine.UI.Text>().text ==
                    Wukong.StacklineClassic.StacklineLocalization.Get(english, Wukong.StacklineClassic.StacklineText.OneLife), "Affordable purchase caption matches English localization");
                Open("Settings"); Flush();
                string settings = Fingerprint();
                gemField.SetValue(controller, 8); hud.RefreshPersistentState(); Flush();
                Check(Fingerprint() == settings, "Unrelated resource changes do not rebuild Settings");
                Check(UnityEngine.Time.frameCount == initialFrame, "Temporary data changes were confined to one synchronous frame");
            }
            catch (System.Exception ex) { errors.Add(ex.ToString()); }
            finally
            {
                gemField.SetValue(controller, savedGems);
                Write("language", savedLanguage);
                hud.ClosePanel();
                Call("ApplyStaticLocalization");
                hud.RefreshPersistentState();
                controller.enabled = savedEnabled;
                for (int i = 0; i < keys.Length; i++)
                    if (UnityEngine.PlayerPrefs.HasKey(keys[i]) != prefExists[i] || UnityEngine.PlayerPrefs.GetString(keys[i], "") != prefValues[i])
                        errors.Add("Unexpected persistent preference change: " + keys[i]);
            }
            return new { status = errors.Count == 0 ? "PASS" : "FAIL", assertions = checks.Count, checks = checks, errors = errors,
                preferencesUnchanged = errors.TrueForAll(x => !x.StartsWith("Unexpected persistent")),
                scope = "Synchronous cache/layout probe: real OpenPanel/RefreshPersistentState/LateUpdate paths; no disk save or economy action invoked. Natural frame scheduling and hardware rays require separate acceptance." };
        }

        public static object StartActionGate()
        {
            const string statusKey = "Stackline.HudPanelActionProbe.20260907";
            if (!UnityEngine.Application.isPlaying || UnityEditor.EditorApplication.isPaused)
                return new { status = "NOT_STARTED", reason = "Use unpaused StacklineVR Play Mode at Menu." };
            var hud = UnityEngine.Object.FindAnyObjectByType<Wukong.StacklineClassic.StacklineHud>();
            if (hud == null) return new { status = "NOT_STARTED", reason = "HUD missing." };
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var ht = hud.GetType();
            var gate = ht.GetMethod("InvokePanelAction", flags);
            var gateFrame = ht.GetField("lastPanelActionFrame", flags);
            if (gate == null || gateFrame == null) return new { status = "NOT_STARTED", reason = "Apply the action-gate patch and compile first." };
            var controller = (Wukong.StacklineClassic.StacklineClassicController)ht.GetField("controller", flags).GetValue(hud);
            if (controller == null || controller.gameObject.scene.path != "Assets/Scenes/StacklineVR.unity" ||
                controller.State != Wukong.StacklineClassic.StacklineClassicController.GameState.Menu ||
                ht.GetField("currentPanel", flags).GetValue(hud).ToString() != "None")
                return new { status = "NOT_STARTED", reason = "Require a closed modal at the generated scene Menu." };
            if (UnityEditor.SessionState.GetString(statusKey, "").StartsWith("RUNNING"))
                return new { status = "NOT_STARTED", reason = "An action-gate probe is already running.", statusKey = statusKey };
            int savedGate = (int)gateFrame.GetValue(hud);
            bool savedControllerEnabled = controller.enabled;
            var systems = UnityEngine.Object.FindObjectsByType<UnityEngine.EventSystems.EventSystem>(UnityEngine.FindObjectsInactive.Include);
            var enabled = new bool[systems.Length];
            for (int i = 0; i < systems.Length; i++) enabled[i] = systems[i].enabled;
            string[] keys = { Wukong.StacklineClassic.StacklineProfileStore.ProfileKey, Wukong.StacklineClassic.StacklineProfileStore.LanguageKey };
            var has = new bool[keys.Length]; var values = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++) { has[i] = UnityEngine.PlayerPrefs.HasKey(keys[i]); values[i] = UnityEngine.PlayerPrefs.GetString(keys[i], ""); }
            int callbacks = 0;
            int initialFrame = UnityEngine.Time.frameCount;
            double deadline = UnityEditor.EditorApplication.timeSinceStartup + 10d;
            bool finished = false;
            System.Action callback = () => callbacks++;
            UnityEditor.EditorApplication.CallbackFunction poll = null;
            void Finish(bool passed, string reason)
            {
                if (finished) return;
                finished = true;
                if (poll != null) UnityEditor.EditorApplication.update -= poll;
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= Abort;
                UnityEditor.EditorApplication.playModeStateChanged -= ModeChanged;
                UnityEditor.EditorApplication.quitting -= Abort;
                try
                {
                    if (hud != null) { hud.ClosePanel(); gateFrame.SetValue(hud, savedGate); }
                    if (controller != null) controller.enabled = savedControllerEnabled;
                    for (int i = 0; i < systems.Length; i++) if (systems[i] != null) systems[i].enabled = enabled[i];
                    for (int i = 0; i < keys.Length; i++)
                        if (UnityEngine.PlayerPrefs.HasKey(keys[i]) != has[i] || UnityEngine.PlayerPrefs.GetString(keys[i], "") != values[i])
                        { passed = false; reason += "; unexpected preference mutation: " + keys[i]; }
                }
                catch (System.Exception ex) { passed = false; reason += "; cleanup error: " + ex; }
                string status = (passed ? "PASS" : "FAIL") + ": callbacks=" + callbacks +
                    "; checks=same-frame/next-frame/closed-panel; " + reason;
                UnityEditor.SessionState.SetString(statusKey, status);
                if (passed) UnityEngine.Debug.Log("STACKLINE_HUD_ACTION_GATE_PASS " + status);
                else UnityEngine.Debug.LogError("STACKLINE_HUD_ACTION_GATE_FAIL " + status);
            }
            void Abort() { Finish(false, "Interrupted by reload or shutdown."); }
            void ModeChanged(UnityEditor.PlayModeStateChange state)
            {
                if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode) Finish(false, "Play Mode ended during probe.");
            }
            try
            {
                controller.enabled = false;
                for (int i = 0; i < systems.Length; i++) systems[i].enabled = false;
                var panelType = ht.GetNestedType("MenuPanel", System.Reflection.BindingFlags.NonPublic);
                ht.GetMethod("OpenPanel", flags).Invoke(hud, new object[] { System.Enum.Parse(panelType, "Settings") });
                ht.GetMethod("LateUpdate", flags).Invoke(hud, null);
                gateFrame.SetValue(hud, -1);
                gate.Invoke(hud, new object[] { callback });
                gate.Invoke(hud, new object[] { callback });
                if (callbacks != 1) throw new System.InvalidOperationException("Two same-frame calls executed " + callbacks + " callbacks.");
                UnityEditor.SessionState.SetString(statusKey, "RUNNING: first-frame duplicate suppressed; waiting for a real new game frame.");
                poll = () =>
                {
                    try
                    {
                        if (!UnityEngine.Application.isPlaying || hud == null) { Finish(false, "Scene or HUD ended."); return; }
                        if (UnityEditor.EditorApplication.timeSinceStartup > deadline) { Finish(false, "No new game frame within 10 seconds."); return; }
                        if (UnityEngine.Time.frameCount == initialFrame) { UnityEditor.EditorApplication.QueuePlayerLoopUpdate(); return; }
                        gate.Invoke(hud, new object[] { callback });
                        gate.Invoke(hud, new object[] { callback });
                        if (callbacks != 2) throw new System.InvalidOperationException("Next-frame pair did not execute exactly one new callback.");
                        hud.ClosePanel();
                        // Clear only the test clock guard to independently test the closed-modal guard.
                        gateFrame.SetValue(hud, -1);
                        gate.Invoke(hud, new object[] { callback });
                        if (callbacks != 2) throw new System.InvalidOperationException("A closed modal accepted a business callback.");
                        Finish(true, "No consumption, reward, setting toggle or Save API was invoked; preferences unchanged.");
                    }
                    catch (System.Exception ex) { Finish(false, ex.ToString()); }
                };
                UnityEditor.EditorApplication.update += poll;
                UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += Abort;
                UnityEditor.EditorApplication.playModeStateChanged += ModeChanged;
                UnityEditor.EditorApplication.quitting += Abort;
                UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (System.Exception ex) { Finish(false, ex.ToString()); }
            return new { status = UnityEditor.SessionState.GetString(statusKey, ""), statusKey = statusKey,
                query = "UnityEditor.SessionState.GetString(\"" + statusKey + "\", \"IDLE\")",
                scope = "Real action entry point with counter-only callbacks; natural cross-frame reset. No economy or persistence actions executed." };
        }

        public static string ActionGateStatus()
        {
            return UnityEditor.SessionState.GetString("Stackline.HudPanelActionProbe.20260907", "IDLE");
        }
    }
}
