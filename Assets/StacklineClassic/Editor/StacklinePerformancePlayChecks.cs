using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using Wukong.StacklineClassic;
using Object = UnityEngine.Object;

namespace Wukong.EditorTools
{
    /// <summary>
    /// Non-blocking checks against the live generated VR scene. Every stress run stays below
    /// the ten-floor reward threshold; no paid revive, finish, purchase, or profile save is called.
    /// Editor SessionState retains a recovery snapshot across script reloads, without exporting
    /// the player's profile into the public validation report.
    /// </summary>
    [InitializeOnLoad]
    public static class StacklinePerformancePlayChecks
    {
        private const string RecoveryKey = "Stackline.PerformancePlayChecks.Recovery.20260907";
        private const string LastStatusKey = "Stackline.PerformancePlayChecks.Status.20260907";
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string RequiredScene = "Assets/Scenes/StacklineVR.unity";

        [Serializable] private sealed class Preference
        {
            public string key;
            public bool exists;
            public bool integer;
            public string text;
            public int number;
        }

        [Serializable] private sealed class BehaviourState
        {
            public string instanceId;
            public bool enabled;
        }

        [Serializable] private sealed class Recovery
        {
            public string controllerId;
            public bool controllerEnabled;
            public bool menuBlocked;
            public string controllerProfile;
            public int gems, lives, stars;
            public Preference[] preferences;
            public BehaviourState[] eventSystems;
        }

        [Serializable] private sealed class Check
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable] private sealed class Report
        {
            public string startedUtc, finishedUtc, scene, phase, error;
            public bool complete, passed, preferencesUnchanged, recoveryVerified;
            public int restartCount, stressPerfectCount, assertionCount;
            public double elapsedSeconds;
            public string arenaLossyScale;
            public List<Check> checks = new List<Check>();
            public string[] limitations =
            {
                "Editor Play Mode functional validation only; not PICO GPU/FPS, thermal, or controller hardware acceptance.",
                "Controller Update is temporarily disabled and EventSystems paused to isolate deterministic input. Coroutines and HUD remain live; the same effect pool is ticked once per game frame by this runner.",
                "100 real Perfect placements are split across 20 runs of 5 floors to avoid the ten-floor currency save. Reward payouts, paid revive, purchases and FinishGame persistence are intentionally excluded.",
                "No test intentionally changes PlayerPrefs. Recovery writes the original values only if an unexpected change is detected.",
                "Arbitrary transformed-pool and Dispose ownership checks are covered separately by StacklineEffectPoolValidation. This runner checks the live arena's collider geometry and pooled resource survival across real restarts."
            };
        }

        private sealed class Wait
        {
            public Func<bool> ready;
            public double deadline;
            public string description;
        }

        private static StacklineClassicController controller;
        private static StacklineEffectPool pool;
        private static Recovery recovery;
        private static Report report;
        private static IEnumerator routine;
        private static Wait wait;
        private static double startedAt;
        private static int lastTickFrame;
        private static readonly List<GameObject> slots = new List<GameObject>();
        private static readonly List<Material> materials = new List<Material>();
        private static readonly List<Mesh> meshes = new List<Mesh>();
        private static readonly HashSet<EntityId> slotIds = new HashSet<EntityId>();

        static StacklinePerformancePlayChecks()
        {
            AssemblyReloadEvents.beforeAssemblyReload += BeforeReload;
            EditorApplication.playModeStateChanged += PlayModeChanged;
            EditorApplication.quitting += BeforeReload;
            if (!string.IsNullOrEmpty(SessionState.GetString(RecoveryKey, string.Empty)))
                EditorApplication.delayCall += RecoverInterruptedRun;
        }

        public static string ReportPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..",
            "outputs/pico/review/performance-20260907/stackline-play-checks.json"));

        [MenuItem("Tools/Stackline Classic/Performance/Start Play Checks")]
        public static void StartMenu() { Debug.Log(Start()); }

        /// <summary>Returns immediately. Call Status() later; the Editor update loop runs checks.</summary>
        public static string Start()
        {
            if (routine != null)
                return Status();
            if (!Application.isPlaying || EditorApplication.isPaused || Time.timeScale <= 0f)
                return "NOT_STARTED: enter unpaused Play Mode in the generated StacklineVR scene first.";
            if (!string.IsNullOrEmpty(SessionState.GetString(RecoveryKey, string.Empty)))
            {
                RecoverInterruptedRun();
                if (!string.IsNullOrEmpty(SessionState.GetString(RecoveryKey, string.Empty)))
                    return "NOT_STARTED: previous recovery needs attention.";
            }

            controller = Object.FindAnyObjectByType<StacklineClassicController>();
            if (controller == null || controller.gameObject.scene.path != RequiredScene ||
                controller.State != StacklineClassicController.GameState.Menu)
                return "NOT_STARTED: expected one initialized controller at Menu in " + RequiredScene;

            try
            {
                pool = Read<StacklineEffectPool>(controller, "effectPool");
                string problem = "Pool is null.";
                if (pool == null || !controller.ValidateEffectPool(out problem))
                    throw new InvalidOperationException("Live effect pool is unavailable: " + problem);
                recovery = CaptureRecovery();
                // Persist recovery before disabling input or invoking any gameplay API.
                SessionState.SetString(RecoveryKey, JsonUtility.ToJson(recovery));
                startedAt = EditorApplication.timeSinceStartup;
                report = new Report
                {
                    startedUtc = DateTime.UtcNow.ToString("O"), scene = RequiredScene,
                    phase = "Starting", preferencesUnchanged = true,
                    arenaLossyScale = Read<Transform>(controller, "arenaAnchor").lossyScale.ToString("F5")
                };
                CapturePoolResources();
                controller.enabled = false;
                controller.SetMenuInteractionBlocked(true);
                foreach (BehaviourState state in recovery.eventSystems)
                    if (ResolveObject(state.instanceId) is Behaviour behaviour)
                        behaviour.enabled = false;
                lastTickFrame = Time.frameCount;
                routine = Run();
                wait = null;
                EditorApplication.update += Tick;
                PublishStatus();
                return Status();
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
                return Status();
            }
        }

        public static string Status()
        {
            if (report == null)
                return SessionState.GetString(LastStatusKey, "IDLE: Play checks have not run.");
            return (report.complete ? (report.passed ? "PASS" : "FAIL") : "RUNNING") +
                ": " + report.phase + "; restarts=" + report.restartCount + "/20; perfects=" +
                report.stressPerfectCount + "/100; assertions=" + report.assertionCount +
                "; report=" + ReportPath + (string.IsNullOrEmpty(report.error) ? "" : "; " + report.error);
        }

        [MenuItem("Tools/Stackline Classic/Performance/Stop Play Checks")]
        public static void Stop()
        {
            if (routine != null) Finish(false, "Cancelled by operator.");
        }

        private static void Tick()
        {
            try
            {
                if (!Application.isPlaying || controller == null)
                    throw new InvalidOperationException("Play Mode or the live controller ended during validation.");
                if (EditorApplication.timeSinceStartup - startedAt > 180d)
                    throw new TimeoutException("Play checks exceeded their 180 second overall deadline.");
                VerifyPreferences();
                if (Time.frameCount != lastTickFrame)
                {
                    lastTickFrame = Time.frameCount;
                    pool.Tick(Time.deltaTime, Read<Transform>(controller, "arenaAnchor").position.y - 12f);
                }
                if (wait != null)
                {
                    if (!wait.ready())
                    {
                        if (EditorApplication.timeSinceStartup > wait.deadline)
                            throw new TimeoutException("Timed out waiting for " + wait.description);
                        EditorApplication.QueuePlayerLoopUpdate();
                        return;
                    }
                    wait = null;
                }
                if (!routine.MoveNext()) { Finish(true, null); return; }
                wait = routine.Current as Wait;
                EditorApplication.QueuePlayerLoopUpdate();
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        private static IEnumerator Run()
        {
            Phase("Begin, input suppression and precise placement");
            controller.BeginFromMenu();
            controller.SetMenuInteractionBlocked(true);
            Read<StacklineTapTarget>(controller, "tapTarget")?.SetInteractable(false);
            Assert(controller.State == StacklineClassicController.GameState.Playing && controller.Height == 0,
                "BeginFromMenu starts one run at height zero");
            Assert(Stack().Count == 1, "Run has one foundation");
            yield return Frames(2);
            AlignMoving(0f);
            controller.TryPlace();
            Assert(controller.Height == 0 && Stack().Count == 1, "Blocked menu input cannot place");
            Place(0f, true);
            Assert(Read<int>(controller, "combo") == 1, "First Perfect starts combo");
            yield return Playing();

            Phase("Trim geometry, collider separation and combo reset");
            bool x = Read<bool>(controller, "moveOnX");
            object before = Moving();
            Vector3 beforeSize = Read<Vector3>(before, "Size");
            Vector3 supportPosition = Read<Vector3>(Top(), "Position");
            const float cut = 0.4f;
            Place(cut, false);
            object clipped = Top();
            Vector3 clippedSize = Read<Vector3>(clipped, "Size");
            Vector3 clippedPosition = Read<Vector3>(clipped, "Position");
            Assert(Near(x ? clippedSize.x : clippedSize.z, (x ? beforeSize.x : beforeSize.z) - cut),
                "Retained footprint loses only the overhanging width");
            Assert(Near(x ? clippedPosition.x : clippedPosition.z, (x ? supportPosition.x : supportPosition.z) + cut * 0.5f),
                "Retained footprint is centered over the intersection");
            Assert(Read<int>(controller, "combo") == 0, "A non-perfect trim resets combo");
            Assert(controller.EffectPoolStats.ActiveFragments == 1, "Trim produces one cosmetic fragment");
            CheckContact();
            CheckPoolResources();
            yield return Playing();
            Place(0f, true);
            Assert(Read<int>(controller, "combo") == 1, "Perfect restarts combo after trim");
            yield return Playing();

            Phase("Ordinary miss and free rescue without economy writes");
            int retainedHeight = controller.Height;
            int retainedLives = controller.Lives;
            AlignMoving(10f);
            controller.SetMenuInteractionBlocked(false);
            controller.TryPlace();
            controller.SetMenuInteractionBlocked(true);
            Assert(controller.State == StacklineClassicController.GameState.Failing && controller.Height == retainedHeight,
                "A complete miss enters Failing without awarding height");
            yield return Until(() => controller.State == StacklineClassicController.GameState.Revive, 4d, "revive offer");
            controller.RequestRescue();
            controller.RequestRescue();
            Assert(controller.State == StacklineClassicController.GameState.Rescue, "Free rescue starts one countdown");
            yield return Until(() => controller.State == StacklineClassicController.GameState.Playing, 7d, "free rescue completion");
            Assert(controller.Height == retainedHeight && controller.Lives == retainedLives,
                "Free rescue preserves height and consumes no life");
            Assert(Read<bool>(controller, "usedRescue"), "Free rescue marks the one-use guard");

            for (int run = 0; run < 20; run++)
            {
                Phase("Stress run " + (run + 1) + "/20, five real Perfect placements");
                controller.RestartRun();
                controller.SetMenuInteractionBlocked(true);
                Read<StacklineTapTarget>(controller, "tapTarget")?.SetInteractable(false);
                report.restartCount++;
                Assert(controller.Height == 0 && Stack().Count == 1 && Moving() != null,
                    "Restart " + (run + 1) + " resets tower, score and moving block");
                Assert(ActiveEffects() == 0, "Restart " + (run + 1) + " clears active effects");
                CheckPoolResources();
                yield return Frames(2);
                for (int floor = 1; floor <= 5; floor++)
                {
                    Place(0f, true);
                    report.stressPerfectCount++;
                    Assert(controller.Height == floor && Read<int>(controller, "combo") == floor,
                        "Stress run " + (run + 1) + " floor " + floor + " preserves score/combo");
                    CheckContact();
                    yield return Playing();
                }
                Assert(controller.Gems == recovery.gems && controller.Lives == recovery.lives && controller.Stars == recovery.stars,
                    "Run below reward threshold leaves resources unchanged");
            }

            Phase("One-frame saturation and pooled resource lifetime");
            Vector3 topPosition = Read<Vector3>(Top(), "Position");
            Vector3 topSize = Read<Vector3>(Top(), "Size");
            for (int index = 0; index < 100; index++)
            {
                Invoke(controller, "CreatePerfectBurst", topPosition);
                Invoke(controller, "CreatePerfectOutline", topPosition, topSize);
            }
            StacklineEffectPoolStats saturated = controller.EffectPoolStats;
            Assert(saturated.ActiveSparks == saturated.SparkCapacity && saturated.ActiveOutlines == saturated.OutlineCapacity,
                "One hundred simultaneous bursts recycle at the fixed pool capacities");
            CheckPoolResources();
            controller.ReturnToMenu();
            controller.SetMenuInteractionBlocked(true);
            Assert(ActiveEffects() == 0, "Returning to menu clears all pooled effects");
            int expiryFrame = Time.frameCount;
            double expiry = EditorApplication.timeSinceStartup + 5.0d;
            yield return Until(() => Time.frameCount > expiryFrame && EditorApplication.timeSinceStartup >= expiry,
                10d, "old effect lifetime to pass after reuse/clear");
            CheckPoolResources();
            Assert(report.restartCount == 20 && report.stressPerfectCount == 100, "All requested stress iterations completed");
            VerifyPreferences();
            Phase("Completed; restoring Menu and input");
        }

        private static void Place(float offset, bool perfect)
        {
            int height = controller.Height;
            int count = Stack().Count;
            Assert(controller.State == StacklineClassicController.GameState.Playing && height < 9,
                "Placement is in Playing and below the persistence threshold");
            AlignMoving(offset);
            controller.SetMenuInteractionBlocked(false);
            controller.TryPlace();
            controller.TryPlace();
            controller.SetMenuInteractionBlocked(true);
            Assert(controller.Height == height + 1 && Stack().Count == count + 1 &&
                controller.State == StacklineClassicController.GameState.Resolving,
                "Two same-frame requests produce exactly one " + (perfect ? "Perfect" : "trimmed") + " placement");
        }

        private static void AlignMoving(float offset)
        {
            object moving = Moving();
            object top = Top();
            Assert(moving != null && top != null, "Placement has a live moving block and support");
            GameObject obj = Read<GameObject>(moving, "Object");
            Vector3 position = obj.transform.localPosition;
            Vector3 support = Read<Vector3>(top, "Position");
            if (Read<bool>(controller, "moveOnX")) position.x = support.x + offset;
            else position.z = support.z + offset;
            obj.transform.localPosition = position;
            // Only construct input geometry; production TryPlace/ResolvePlacement owns the result.
        }

        private static void CheckContact()
        {
            IList stack = Stack();
            GameObject lower = Read<GameObject>(stack[stack.Count - 2], "Object");
            GameObject upper = Read<GameObject>(stack[stack.Count - 1], "Object");
            BoxCollider a = lower.GetComponent<BoxCollider>();
            BoxCollider b = upper.GetComponent<BoxCollider>();
            Assert(a != null && b != null && a.enabled && b.enabled, "Gameplay support keeps both live colliders");
            Physics.SyncTransforms();
            Assert(!Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                b, b.transform.position, b.transform.rotation, out _, out _),
                "Adjacent floors do not penetrate after world transform/scale");
            Transform arena = Read<Transform>(controller, "arenaAnchor");
            Vector3 expectedCenter = arena.TransformPoint(Read<Vector3>(stack[stack.Count - 1], "Position"));
            Assert((b.bounds.center - expectedCenter).sqrMagnitude < 0.00001f,
                "Collider world center follows the authored arena transform");
            Renderer renderer = Read<Renderer>(stack[stack.Count - 1], "Renderer");
            Assert(renderer != null && renderer.sharedMaterial != null, "Placed gold retains a live material");
        }

        private static void CapturePoolResources()
        {
            slots.Clear(); materials.Clear(); meshes.Clear(); slotIds.Clear();
            foreach (string name in new[] { "sparks", "fragments", "outlines" })
                foreach (object slot in Read<Array>(pool, name))
                {
                    GameObject obj = Read<GameObject>(slot, "Object");
                    slots.Add(obj);
                    slotIds.Add(obj.GetEntityId());
                    materials.Add(obj.GetComponent<MeshRenderer>().sharedMaterial);
                    meshes.Add(obj.GetComponent<MeshFilter>().sharedMesh);
                }
        }

        private static void CheckPoolResources()
        {
            StacklineEffectPoolStats stats = controller.EffectPoolStats;
            Assert(stats.ActiveSparks <= stats.SparkCapacity && stats.ActiveFragments <= stats.FragmentCapacity &&
                stats.ActiveOutlines <= stats.OutlineCapacity, "Live pool remains within all three capacities");
            Assert(controller.ValidateEffectPool(out string problem), "Live pool ownership validation", problem);
            Assert(slots.Count == stats.SparkCapacity + stats.FragmentCapacity + stats.OutlineCapacity &&
                slotIds.Count == slots.Count, "Effect capacity corresponds to distinct reusable objects");
            for (int index = 0; index < slots.Count; index++)
            {
                GameObject obj = slots[index];
                if (obj == null || materials[index] == null || meshes[index] == null ||
                    obj.GetComponent<MeshRenderer>().sharedMaterial == null ||
                    obj.GetComponent<MeshFilter>().sharedMesh == null ||
                    obj.GetComponent<Collider>() != null || obj.GetComponent<Rigidbody>() != null)
                    throw new InvalidOperationException("Pooled slot " + index + " lost a resource or acquired gameplay physics.");
            }
            Assert(true, "Pooled objects, original materials and meshes survive without colliders/Rigidbodies");
        }

        private static Recovery CaptureRecovery()
        {
            EventSystem[] events = Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include);
            BehaviourState[] eventStates = new BehaviourState[events.Length];
            for (int i = 0; i < events.Length; i++)
                eventStates[i] = new BehaviourState { instanceId = EntityId.ToULong(events[i].GetEntityId()).ToString(), enabled = events[i].enabled };
            string[] keys = { StacklineProfileStore.ProfileKey, StacklineProfileStore.LanguageKey,
                "Wukong.StacklineClassic.Best", "Wukong.StacklineClassic.Gems", "Wukong.StacklineClassic.Lives" };
            Preference[] preferences = new Preference[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                preferences[i] = new Preference { key = keys[i], exists = PlayerPrefs.HasKey(keys[i]), integer = i >= 2,
                    text = i < 2 ? PlayerPrefs.GetString(keys[i], string.Empty) : null,
                    number = i >= 2 ? PlayerPrefs.GetInt(keys[i], 0) : 0 };
            return new Recovery
            {
                controllerId = EntityId.ToULong(controller.GetEntityId()).ToString(), controllerEnabled = controller.enabled,
                menuBlocked = Read<bool>(controller, "menuInteractionBlocked"),
                controllerProfile = JsonUtility.ToJson(Read<StacklineProfileData>(controller, "profile")),
                gems = controller.Gems, lives = controller.Lives, stars = controller.Stars,
                preferences = preferences, eventSystems = eventStates
            };
        }

        private static bool PreferenceMatches(Preference preference)
        {
            if (PlayerPrefs.HasKey(preference.key) != preference.exists) return false;
            return !preference.exists || (preference.integer
                ? PlayerPrefs.GetInt(preference.key, 0) == preference.number
                : PlayerPrefs.GetString(preference.key, string.Empty) == preference.text);
        }

        private static void VerifyPreferences()
        {
            foreach (Preference preference in recovery.preferences)
                if (!PreferenceMatches(preference))
                {
                    report.preferencesUnchanged = false;
                    throw new InvalidOperationException("Unexpected preference mutation: " + preference.key + "; restoring original values.");
                }
        }

        private static void Restore()
        {
            if (recovery == null) return;
            StacklineClassicController live = ResolveObject(recovery.controllerId) as StacklineClassicController;
            if (live != null && Application.isPlaying)
            {
                live.StopAllCoroutines();
                Write(live, "profile", JsonUtility.FromJson<StacklineProfileData>(recovery.controllerProfile));
                Write(live, "gems", recovery.gems); Write(live, "lives", recovery.lives); Write(live, "stars", recovery.stars);
                live.ReturnToMenu();
                live.SetMenuInteractionBlocked(recovery.menuBlocked);
                live.enabled = recovery.controllerEnabled;
            }
            foreach (BehaviourState state in recovery.eventSystems)
                if (ResolveObject(state.instanceId) is Behaviour behaviour)
                    behaviour.enabled = state.enabled;
            bool changed = false;
            foreach (Preference preference in recovery.preferences)
            {
                if (PreferenceMatches(preference)) continue;
                changed = true;
                if (!preference.exists) PlayerPrefs.DeleteKey(preference.key);
                else if (preference.integer) PlayerPrefs.SetInt(preference.key, preference.number);
                else PlayerPrefs.SetString(preference.key, preference.text);
            }
            if (changed)
            {
                if (report != null) report.preferencesUnchanged = false;
                PlayerPrefs.Save();
            }
            foreach (Preference preference in recovery.preferences)
                if (!PreferenceMatches(preference)) throw new InvalidOperationException("Preference recovery failed: " + preference.key);
            if (report != null) report.recoveryVerified = true;
            SessionState.EraseString(RecoveryKey);
            recovery = null;
        }

        private static void Finish(bool success, string error)
        {
            EditorApplication.update -= Tick;
            routine = null; wait = null;
            if (report == null) report = new Report { scene = RequiredScene, startedUtc = DateTime.UtcNow.ToString("O") };
            try { Restore(); }
            catch (Exception restoreError) { success = false; error += "\nRECOVERY FAILED: " + restoreError; }
            report.complete = true;
            report.passed = success && report.preferencesUnchanged && report.recoveryVerified;
            report.error = error;
            report.phase = report.passed ? "All functional checks passed; Menu/input/preferences restored" : "Validation failed or interrupted";
            report.finishedUtc = DateTime.UtcNow.ToString("O");
            report.elapsedSeconds = Math.Max(0d, EditorApplication.timeSinceStartup - startedAt);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
                File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            }
            catch (Exception writeError) { report.passed = false; report.error += "\nREPORT WRITE FAILED: " + writeError.Message; }
            slots.Clear(); materials.Clear(); meshes.Clear(); slotIds.Clear();
            controller = null;
            pool = null;
            PublishStatus();
            if (report.passed) Debug.Log("STACKLINE_PERFORMANCE_PLAY_CHECKS_PASS " + Status());
            else Debug.LogError("STACKLINE_PERFORMANCE_PLAY_CHECKS_FAIL " + Status());
        }

        private static void BeforeReload()
        {
            if (routine != null) Finish(false, "Interrupted by script reload or Editor shutdown.");
        }

        private static void PlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode && routine != null)
                Finish(false, "Play Mode exited before checks completed.");
        }

        private static void RecoverInterruptedRun()
        {
            string json = SessionState.GetString(RecoveryKey, string.Empty);
            if (string.IsNullOrEmpty(json) || routine != null) return;
            try
            {
                recovery = JsonUtility.FromJson<Recovery>(json);
                Restore();
                SessionState.SetString(LastStatusKey, "INTERRUPTED: prior run recovered; no PASS was recorded.");
                Debug.LogWarning("Stackline interrupted play-check recovery completed.");
            }
            catch (Exception exception) { Debug.LogError("Stackline play-check recovery needs attention: " + exception); }
        }

        private static void Assert(bool condition, string name, string detail = null)
        {
            report.assertionCount++;
            report.checks.Add(new Check { name = name, passed = condition, detail = detail });
            if (!condition) throw new InvalidOperationException(name + (string.IsNullOrEmpty(detail) ? "" : ": " + detail));
        }

        private static void Phase(string phase) { report.phase = phase; PublishStatus(); }
        private static void PublishStatus() { SessionState.SetString(LastStatusKey, Status()); }
        private static Wait Frames(int frames) { int target = Time.frameCount + frames; return Until(() => Time.frameCount >= target, 8d, frames + " game frames"); }
        private static Wait Playing() { return Until(() => controller.State == StacklineClassicController.GameState.Playing && Moving() != null, 5d, "next moving block"); }
        private static Wait Until(Func<bool> predicate, double seconds, string description) { return new Wait { ready = predicate, deadline = EditorApplication.timeSinceStartup + seconds, description = description }; }
        private static Object ResolveObject(string id) { return EditorUtility.EntityIdToObject(EntityId.FromULong(ulong.Parse(id))); }
        private static bool Near(float a, float b) { return Mathf.Abs(a - b) < 0.0001f; }
        private static IList Stack() { return Read<IList>(controller, "stack"); }
        private static object Top() { IList stack = Stack(); return stack.Count == 0 ? null : stack[stack.Count - 1]; }
        private static object Moving() { return Read<object>(controller, "movingBlock"); }
        private static int ActiveEffects() { StacklineEffectPoolStats s = controller.EffectPoolStats; return s.ActiveSparks + s.ActiveFragments + s.ActiveOutlines; }
        private static T Read<T>(object instance, string field) { return (T)(instance.GetType().GetField(field, Fields) ?? throw new MissingFieldException(instance.GetType().Name, field)).GetValue(instance); }
        private static void Write(object instance, string field, object value) { (instance.GetType().GetField(field, Fields) ?? throw new MissingFieldException(instance.GetType().Name, field)).SetValue(instance, value); }
        private static void Invoke(object instance, string method, params object[] values) { (instance.GetType().GetMethod(method, Fields) ?? throw new MissingMethodException(instance.GetType().Name, method)).Invoke(instance, values); }
    }
}
