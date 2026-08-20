using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Wukong.StackingHouseVR;

namespace Wukong.EditorTools
{
    [InitializeOnLoad]
    public static class StackingHouseVRSmokeTest
    {
        private const string RunKey = "Wukong.StackingHouseVR.SmokeTestRunning";
        private const string LogPrefix = "[Stacking House VR Smoke Test]";
        private const string HousePrefabPath = "Assets/StackingHouseVR/Generated/StackableHouse.prefab";

        private static readonly MethodInfo SelectHouseMethod = typeof(StackableHouse).GetMethod(
            "OnSelected", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo ReleaseHouseMethod = typeof(StackableHouse).GetMethod(
            "OnReleased", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo StableBackingField = typeof(StackableHouse).GetField(
            "<IsStable>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo SupportingCollidersField = typeof(StackableHouse).GetField(
            "supportingColliders", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo HasBeenReleasedField = typeof(StackableHouse).GetField(
            "hasBeenReleased", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TranslateZValueField = typeof(XRInteractionSimulator).GetField(
            "m_TranslateZValue", BindingFlags.Instance | BindingFlags.NonPublic);

        private static StackingHouseGameController game;
        private static HouseTrayController tray;
        private static PlayerElevatorController elevator;
        private static Transform foundationTop;
        private static Transform movingPlatform;
        private static Transform simulatedCamera;
        private static XRSimulatedHMD simulatedHmd;
        private static Vector3 cameraPositionBeforeInput;
        private static Vector3 hmdPositionBeforeInput;
        private static Quaternion cameraRotationBeforeInput;
        private static StackableHouse heldHouse;
        private static readonly List<StackableHouse> testTower = new List<StackableHouse>();
        private static int stage;
        private static double stageStartedAt;

        static StackingHouseVRSmokeTest()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [MenuItem("Tools/Stacking House VR/Run Play Mode Smoke Test")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning(LogPrefix + " Stop Play Mode before starting a new smoke test.");
                return;
            }

            SessionState.SetBool(RunKey, true);
            Debug.Log(LogPrefix + " Starting...");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunKey, false))
                BeginRuntimeValidation();
            else if (state == PlayModeStateChange.EnteredEditMode)
                EditorApplication.update -= Tick;
        }

        private static void BeginRuntimeValidation()
        {
            stage = 0;
            stageStartedAt = EditorApplication.timeSinceStartup;
            testTower.Clear();
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying)
                return;

            try
            {
                switch (stage)
                {
                    case 0:
                        if (Time.time <= 1.1f)
                            return;
                        ValidateInitialScene();
                        cameraPositionBeforeInput = simulatedCamera.position;
                        hmdPositionBeforeInput = simulatedHmd.centerEyePosition.ReadValue();
                        SetOnlyKey(Keyboard.current.wKey);
                        AdvanceStage();
                        break;
                    case 1:
                        if (Elapsed < 0.3d)
                            return;
                        bool wIsPressed = Keyboard.current.wKey.isPressed;
                        float translateZ = TranslateZValueField != null
                            ? (float)TranslateZValueField.GetValue(XRInteractionSimulator.instance)
                            : float.NaN;
                        float hmdDistance = Vector3.Distance(
                            simulatedHmd.centerEyePosition.ReadValue(), hmdPositionBeforeInput);
                        float cameraDistance = Vector3.Distance(simulatedCamera.position, cameraPositionBeforeInput);
                        ReleaseKeys();
                        Require(cameraDistance > 0.03f,
                            $"WASD input did not move the simulated XR camera (key={wIsPressed}, " +
                            $"axis={translateZ:F2}, hmdDelta={hmdDistance:F3}, cameraDelta={cameraDistance:F3}, time={Time.time:F2}).");
                        cameraRotationBeforeInput = simulatedCamera.rotation;
                        SetOnlyKey(Keyboard.current.rightArrowKey);
                        AdvanceStage();
                        break;
                    case 2:
                        if (Elapsed < 0.3d)
                            return;
                        ReleaseKeys();
                        Require(Quaternion.Angle(simulatedCamera.rotation, cameraRotationBeforeInput) > 0.5f,
                            "Arrow-key input did not rotate the simulated XR camera.");
                        StartHeldHouseAndTowerTest();
                        AdvanceStage();
                        break;
                    case 3:
                        if (game.StableHouseCount >= 4)
                        {
                            Require(elevator.CurrentRise <= 0.01f,
                                "Elevator moved while a house was being held.");
                            ReleaseHouseMethod.Invoke(heldHouse, new object[] { null });
                            AdvanceStage();
                        }
                        else if (Elapsed > 8d)
                        {
                            Fail($"Tower did not settle. Stable connected houses: {game.StableHouseCount}/4. {DescribeTower()}");
                        }
                        break;
                    case 4:
                        if (elevator.CurrentRise > 0.08f)
                        {
                            ValidateReleaseAndRefill();
                            ValidateDisconnectedHouseIsIgnored();
                            AdvanceStage();
                        }
                        else if (Elapsed > 3d)
                        {
                            Fail("Elevator did not rise after the held house was released.");
                        }
                        break;
                    case 5:
                        if (Elapsed < 0.35d)
                            return;
                        Require(game.StableHouseCount == 4,
                            "A stable house without a support path to the foundation was counted in the tower.");
                        foreach (StackableHouse house in testTower)
                        {
                            if (house != null)
                                UnityEngine.Object.Destroy(house.gameObject);
                        }
                        game.NotifyHouseStateChanged();
                        AdvanceStage();
                        break;
                    case 6:
                        if (game.StableHouseCount == 0 && elevator.CurrentRise <= 0.02f)
                        {
                            Pass();
                        }
                        else if (Elapsed > 3d)
                        {
                            Fail($"Elevator did not return after the tower was removed (rise {elevator.CurrentRise:F2} m).");
                        }
                        break;
                }
            }
            catch (TargetInvocationException exception)
            {
                Fail(exception.InnerException?.ToString() ?? exception.ToString());
            }
            catch (Exception exception)
            {
                Fail(exception.ToString());
            }
        }

        private static void ValidateInitialScene()
        {
            int gameplayRootCount = 0;
            foreach (GameObject rootObject in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (rootObject.name == "Stacking House VR")
                    gameplayRootCount++;
            }

            Require(gameplayRootCount == 1, $"Expected one gameplay root, found {gameplayRootCount}.");
            game = UnityEngine.Object.FindAnyObjectByType<StackingHouseGameController>();
            tray = UnityEngine.Object.FindAnyObjectByType<HouseTrayController>();
            elevator = UnityEngine.Object.FindAnyObjectByType<PlayerElevatorController>();
            foundationTop = GameObject.Find("Foundation Top")?.transform;
            Require(game != null, "Game Controller is missing.");
            Require(tray != null, "House Supply Tray is missing.");
            Require(elevator != null, "Player Elevator is missing.");
            Require(foundationTop != null, "Foundation Top marker is missing.");

            movingPlatform = elevator.transform.Find("Moving Platform");
            Require(movingPlatform != null, "Moving Platform is missing.");
            Require(tray.transform.IsChildOf(movingPlatform), "House tray does not move with the elevator.");
            GameObject rig = GameObject.Find("VR Player Rig");
            Require(rig != null && rig.transform.IsChildOf(movingPlatform),
                "VR Player Rig does not move with the elevator.");
            Require(rig.GetComponentsInChildren<XRBaseInteractor>(true).Length >= 2,
                "PICO controller interactors are missing from the VR Player Rig.");
            Require(XRInteractionSimulator.instance != null,
                "XR Interaction Simulator was not created in Editor Play Mode.");
            Require(SimulatedDeviceLifecycleManager.instance != null,
                "Simulated XR device lifecycle manager is missing.");
            Require(Keyboard.current != null, "A keyboard is required for the Editor input smoke test.");

            int simulatedControllerCount = 0;
            simulatedHmd = null;
            foreach (InputDevice device in InputSystem.devices)
            {
                if (device is XRSimulatedHMD hmd)
                    simulatedHmd = hmd;
                else if (device is XRSimulatedController)
                    simulatedControllerCount++;
            }

            Require(simulatedHmd != null, "The simulated XR headset input device was not created.");
            Require(simulatedControllerCount == 2,
                $"Expected two simulated XR controllers, found {simulatedControllerCount}.");
            simulatedCamera = Camera.main != null ? Camera.main.transform : null;
            Require(simulatedCamera != null && simulatedCamera.IsChildOf(rig.transform),
                "The simulated XR camera is missing from the VR Player Rig.");
            Require(simulatedCamera.position.y - movingPlatform.position.y > 1.3f,
                "Editor camera eye height is too low for standing interaction.");
            Require(!foundationTop.IsChildOf(movingPlatform), "Foundation must remain fixed in world space.");
            Require(UnityEngine.Object.FindObjectsByType<XRInteractionManager>().Length == 1,
                "Expected exactly one XR Interaction Manager.");

            StackableHouse[] stockedHouses = tray.GetComponentsInChildren<StackableHouse>(true);
            Require(stockedHouses.Length == 2, $"Expected two stocked houses, found {stockedHouses.Length}.");
            foreach (StackableHouse house in stockedHouses)
            {
                Rigidbody body = house.GetComponent<Rigidbody>();
                Require(house.IsOnTray, house.name + " is not marked as tray stock.");
                Require(body.isKinematic && !body.useGravity,
                    house.name + " must be kinematic without gravity while stocked.");
            }
        }

        private static void StartHeldHouseAndTowerTest()
        {
            heldHouse = tray.GetComponentsInChildren<StackableHouse>(true)[0];
            SelectHouseMethod.Invoke(heldHouse, new object[] { null });
            heldHouse.transform.position = foundationTop.position + new Vector3(2f, 1.4f, 0f);
            Require(StackableHouse.AnyHouseSelected, "Selecting a house did not enter the held state.");
            Require(!heldHouse.IsOnTray && heldHouse.transform.parent == null,
                "Selected house did not detach from its tray slot.");

            GameObject housePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefabPath);
            Require(housePrefab != null, "Generated house prefab is missing.");
            for (int index = 0; index < 4; index++)
            {
                GameObject instance = UnityEngine.Object.Instantiate(housePrefab);
                instance.name = "Smoke Test Tower House " + (index + 1);
                instance.transform.SetPositionAndRotation(
                    foundationTop.position + Vector3.up * (0.012f + index * 0.472f),
                    Quaternion.identity);
                StackableHouse house = instance.GetComponent<StackableHouse>();
                Rigidbody body = instance.GetComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                game.RegisterHouse(house);
                SelectHouseMethod.Invoke(house, new object[] { null });
                ReleaseHouseMethod.Invoke(house, new object[] { null });
                testTower.Add(house);
            }
        }

        private static void ValidateReleaseAndRefill()
        {
            Rigidbody releasedBody = heldHouse.GetComponent<Rigidbody>();
            Require(!releasedBody.isKinematic && releasedBody.useGravity,
                "Released house did not return to dynamic gravity physics.");
            Require(!StackableHouse.AnyHouseSelected, "Held-house count did not clear after release.");

            StackableHouse[] stockedHouses = tray.GetComponentsInChildren<StackableHouse>(true);
            Require(stockedHouses.Length == 2,
                $"Tray did not refill to two houses; found {stockedHouses.Length}.");
            foreach (StackableHouse house in stockedHouses)
            {
                Rigidbody body = house.GetComponent<Rigidbody>();
                Require(house.IsOnTray && body.isKinematic && !body.useGravity,
                    house.name + " has invalid stock physics after refill.");
            }
        }

        private static void ValidateDisconnectedHouseIsIgnored()
        {
            GameObject housePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefabPath);
            GameObject instance = UnityEngine.Object.Instantiate(housePrefab);
            instance.name = "Smoke Test Disconnected House";
            instance.transform.position = foundationTop.position + new Vector3(2.5f, 0.3f, 0f);
            StackableHouse house = instance.GetComponent<StackableHouse>();
            instance.GetComponent<Rigidbody>().isKinematic = true;
            StableBackingField.SetValue(house, true);
            game.RegisterHouse(house);
            game.NotifyHouseStateChanged();
        }

        private static double Elapsed => EditorApplication.timeSinceStartup - stageStartedAt;

        private static void SetOnlyKey(UnityEngine.InputSystem.Controls.KeyControl key)
        {
            if (Keyboard.current != null && key != null)
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(key.keyCode));
        }

        private static void ReleaseKeys()
        {
            if (Keyboard.current != null)
                InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState());
        }

        private static string DescribeTower()
        {
            StringBuilder description = new StringBuilder();
            for (int index = 0; index < testTower.Count; index++)
            {
                StackableHouse house = testTower[index];
                if (house == null)
                {
                    description.Append($"H{index + 1}=destroyed; ");
                    continue;
                }

                Rigidbody body = house.GetComponent<Rigidbody>();
                HashSet<Collider> supports = SupportingCollidersField?.GetValue(house) as HashSet<Collider>;
                bool wasReleased = HasBeenReleasedField != null && (bool)HasBeenReleasedField.GetValue(house);
                description.Append(
                    $"H{index + 1}[y={house.transform.position.y:F3}, stable={house.IsStable}, " +
                    $"supports={supports?.Count ?? -1}, released={wasReleased}, kinematic={body.isKinematic}, " +
                    $"sleeping={body.IsSleeping()}, v={body.linearVelocity.magnitude:F3}, w={body.angularVelocity.magnitude:F3}]; ");
            }

            return description.ToString();
        }

        private static void AdvanceStage()
        {
            stage++;
            stageStartedAt = EditorApplication.timeSinceStartup;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void Pass()
        {
            Finish(true,
                "PASS: Editor XR input, camera movement/rotation, tray refill, release physics, connected tower filtering, hold pause, and elevator rise/return verified.");
        }

        private static void Fail(string message)
        {
            Finish(false, "FAIL: " + message);
        }

        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= Tick;
            ReleaseKeys();
            SessionState.EraseBool(RunKey);
            if (success)
                Debug.Log(LogPrefix + " " + message);
            else
                Debug.LogError(LogPrefix + " " + message);

            EditorApplication.ExitPlaymode();
        }
    }
}
