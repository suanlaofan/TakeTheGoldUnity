using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Unity.XR.CoreUtils;
using Wukong.StackingHouseVR;

namespace Wukong.EditorTools
{
    public static class StackingHouseVRSceneBuilder
    {
        private const string RootName = "Stacking House VR";
        private const string AssetRoot = "Assets/StackingHouseVR";
        private const string GeneratedRoot = AssetRoot + "/Generated";
        private const string HousePrefabPath = GeneratedRoot + "/StackableHouse.prefab";
        private const string HouseMaterialPath = GeneratedRoot + "/HouseWarmWood.mat";
        private const string RoofMaterialPath = GeneratedRoot + "/HouseTileRoof.mat";
        private const string StoneMaterialPath = GeneratedRoot + "/FoundationStone.mat";
        private const string GoldMaterialPath = GeneratedRoot + "/ElevatorBronze.mat";
        private const string ControllerRigPrefabPath = GeneratedRoot + "/PicoControllerRig.prefab";
        private const string InputSettingsPath = GeneratedRoot + "/StackingHouseInputSettings.asset";
        private const string SimulatorSettingsPath = "Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset";
        private const string SimulatorSampleName = "XR Interaction Simulator";
        private const string ResumeBuildKey = "Wukong.StackingHouseVR.ResumeBuild";
        private const float EditorEyeHeight = 1.55f;

        [InitializeOnLoadMethod]
        private static void ResumePendingBuild()
        {
            if (!SessionState.GetBool(ResumeBuildKey, false))
                return;

            SessionState.EraseBool(ResumeBuildKey);
            EditorApplication.delayCall += CreateOrRefreshGameplay;
        }

        [MenuItem("Tools/Stacking House VR/Create or Refresh Gameplay")]
        public static void CreateOrRefreshGameplay()
        {
            if (ImportRequiredInteractionSamples())
                return;

            EnsureDirectories();
            ConfigureEditorInteractionSimulator();
            Material houseMaterial = GetOrCreateMaterial(HouseMaterialPath, new Color(0.44f, 0.095f, 0.045f), 0.18f, 0f);
            Material roofMaterial = GetOrCreateMaterial(RoofMaterialPath, new Color(0.075f, 0.027f, 0.022f), 0.28f, 0f);
            Material stoneMaterial = GetOrCreateMaterial(StoneMaterialPath, new Color(0.18f, 0.2f, 0.19f), 0.72f, 0f);
            Material bronzeMaterial = GetOrCreateMaterial(GoldMaterialPath, new Color(0.42f, 0.18f, 0.035f), 0.7f, 0.2f);

            GameObject housePrefab = CreateOrUpdateHousePrefab(houseMaterial, roofMaterial, stoneMaterial);
            Scene scene = SceneManager.GetActiveScene();
            GameObject existingRoot = null;
            foreach (Transform candidate in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (candidate.parent == null && candidate.name == RootName && candidate.gameObject.scene == scene)
                {
                    existingRoot = candidate.gameObject;
                    break;
                }
            }
            if (existingRoot != null)
                Object.DestroyImmediate(existingRoot);

            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create stacking house VR gameplay");
            GameObject playerSpawn = GameObject.Find("PlayerSpawn");
            if (playerSpawn != null)
            {
                root.transform.SetPositionAndRotation(playerSpawn.transform.position, playerSpawn.transform.rotation);
            }
            else
            {
                root.transform.position = Vector3.zero;
                Debug.LogWarning("PlayerSpawn was not found. The gameplay root was placed at the world origin.");
            }

            GameObject foundation = CreateFoundation(root.transform, stoneMaterial, bronzeMaterial);
            GameObject gameControllerObject = new GameObject("Game Controller");
            gameControllerObject.transform.SetParent(root.transform, false);
            StackingHouseGameController gameController = gameControllerObject.AddComponent<StackingHouseGameController>();
            GameObject interactionManagerObject = new GameObject("XR Interaction Manager");
            interactionManagerObject.transform.SetParent(root.transform, false);
            interactionManagerObject.AddComponent<XRInteractionManager>();

            GameObject elevatorRoot = CreateElevator(root.transform, bronzeMaterial, stoneMaterial, out Transform movingPlatform,
                out Transform leftGuide, out Transform rightGuide);
            PlayerElevatorController elevator = elevatorRoot.AddComponent<PlayerElevatorController>();
            elevator.Configure(movingPlatform, leftGuide, rightGuide);
            gameController.Configure(foundation.transform.Find("Foundation Top"), elevator);

            CreateTray(movingPlatform, housePrefab, houseMaterial, bronzeMaterial, gameController);
            SetupVrRig(movingPlatform);
            ConfigureXrStartup();
            ConfigureEditorInput();
            ConfigurePhysics();

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Stacking House VR gameplay created. Use PICO controllers to take houses from the tray.");
        }

        [MenuItem("Tools/Stacking House VR/Focus Gameplay Anchor")]
        public static void FocusGameplayAnchor()
        {
            GameObject root = GameObject.Find(RootName);
            if (root == null)
            {
                Debug.LogWarning("Create the stacking house gameplay first.");
                return;
            }

            Transform focusTarget = root.transform.Find("Tower Foundation");
            Selection.activeGameObject = focusTarget != null ? focusTarget.gameObject : root;
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return;

            sceneView.pivot = root.transform.position + new Vector3(0f, 0.65f, 0f);
            sceneView.rotation = Quaternion.Euler(15f, 0f, 0f);
            sceneView.size = 2.4f;
            sceneView.orthographic = false;
            sceneView.Repaint();
        }

        private static void EnsureDirectories()
        {
            EnsureFolder("Assets", "StackingHouseVR");
            EnsureFolder(AssetRoot, "Generated");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string folderPath = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(folderPath))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static Material GetOrCreateMaterial(string path, Color color, float smoothness, float metallic)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject CreateOrUpdateHousePrefab(Material houseMaterial, Material roofMaterial, Material stoneMaterial)
        {
            bool prefabAlreadyExists = AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefabPath) != null;
            GameObject prefabContents = prefabAlreadyExists
                ? PrefabUtility.LoadPrefabContents(HousePrefabPath)
                : new GameObject("Stackable House");

            ClearChildren(prefabContents.transform);
            RemoveGeneratedComponents(prefabContents);
            prefabContents.name = "Stackable House";

            CreatePrimitive(prefabContents.transform, PrimitiveType.Cube, "House Body", new Vector3(0f, 0.135f, 0f),
                new Vector3(0.44f, 0.27f, 0.36f), houseMaterial);
            CreatePrimitive(prefabContents.transform, PrimitiveType.Cube, "Front Door", new Vector3(0f, 0.095f, -0.185f),
                new Vector3(0.1f, 0.17f, 0.015f), stoneMaterial);
            CreatePrimitive(prefabContents.transform, PrimitiveType.Cube, "Left Window", new Vector3(-0.13f, 0.17f, -0.187f),
                new Vector3(0.075f, 0.07f, 0.015f), roofMaterial);
            CreatePrimitive(prefabContents.transform, PrimitiveType.Cube, "Right Window", new Vector3(0.13f, 0.17f, -0.187f),
                new Vector3(0.075f, 0.07f, 0.015f), roofMaterial);

            CreatePrimitive(prefabContents.transform, PrimitiveType.Cube, "Roof Left", new Vector3(-0.13f, 0.345f, 0f),
                new Vector3(0.34f, 0.055f, 0.46f), roofMaterial, new Vector3(0f, 0f, -20f));
            CreatePrimitive(prefabContents.transform, PrimitiveType.Cube, "Roof Right", new Vector3(0.13f, 0.345f, 0f),
                new Vector3(0.34f, 0.055f, 0.46f), roofMaterial, new Vector3(0f, 0f, 20f));
            CreatePrimitive(prefabContents.transform, PrimitiveType.Cylinder, "Roof Finial", new Vector3(0f, 0.43f, 0f),
                new Vector3(0.025f, 0.03f, 0.025f), roofMaterial);

            BoxCollider collider = prefabContents.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.23f, 0f);
            collider.size = new Vector3(0.58f, 0.46f, 0.48f);
            collider.material = GetOrCreatePhysicsMaterial();

            Rigidbody rigidbody = prefabContents.AddComponent<Rigidbody>();
            rigidbody.mass = 0.8f;
            rigidbody.linearDamping = 0.4f;
            rigidbody.angularDamping = 0.9f;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.maxAngularVelocity = 12f;
            rigidbody.centerOfMass = new Vector3(0f, 0.15f, 0f);
            rigidbody.solverIterations = 12;
            rigidbody.solverVelocityIterations = 4;

            XRGrabInteractable grab = prefabContents.AddComponent<XRGrabInteractable>();
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
            grab.useDynamicAttach = true;
            grab.matchAttachPosition = true;
            grab.matchAttachRotation = true;
            grab.snapToColliderVolume = true;
            grab.trackPosition = true;
            grab.trackRotation = true;
            grab.smoothPosition = true;
            grab.smoothPositionAmount = 18f;
            grab.tightenPosition = 0.6f;
            grab.smoothRotation = true;
            grab.smoothRotationAmount = 16f;
            grab.tightenRotation = 0.6f;
            grab.throwOnDetach = false;
            grab.forceGravityOnDetach = true;
            grab.unparentTransformOnGrab = true;
            grab.retainTransformParent = false;
            grab.selectMode = InteractableSelectMode.Single;

            prefabContents.AddComponent<StackableHouse>();

            PrefabUtility.SaveAsPrefabAsset(prefabContents, HousePrefabPath);
            if (prefabAlreadyExists)
                PrefabUtility.UnloadPrefabContents(prefabContents);
            else
                Object.DestroyImmediate(prefabContents);
            AssetDatabase.ImportAsset(HousePrefabPath);
            return AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefabPath);
        }

        private static PhysicsMaterial GetOrCreatePhysicsMaterial()
        {
            const string path = GeneratedRoot + "/HouseGrip.asset";
            PhysicsMaterial physicsMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
            if (physicsMaterial == null)
            {
                physicsMaterial = new PhysicsMaterial("House Grip");
                AssetDatabase.CreateAsset(physicsMaterial, path);
            }

            physicsMaterial.staticFriction = 0.85f;
            physicsMaterial.dynamicFriction = 0.7f;
            physicsMaterial.bounciness = 0f;
            physicsMaterial.frictionCombine = PhysicsMaterialCombine.Maximum;
            physicsMaterial.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(physicsMaterial);
            return physicsMaterial;
        }

        private static GameObject CreateFoundation(Transform parent, Material stoneMaterial, Material bronzeMaterial)
        {
            GameObject foundation = new GameObject("Tower Foundation");
            foundation.transform.SetParent(parent, false);
            foundation.transform.localPosition = Vector3.zero;

            CreatePrimitive(foundation.transform, PrimitiveType.Cylinder, "Stone Base", new Vector3(0f, 0.07f, 0f),
                new Vector3(1.12f, 0.07f, 1.12f), stoneMaterial, null, true);
            CreatePrimitive(foundation.transform, PrimitiveType.Cylinder, "Bronze Inlay", new Vector3(0f, 0.15f, 0f),
                new Vector3(0.96f, 0.015f, 0.96f), bronzeMaterial);
            GameObject topPlate = CreatePrimitive(foundation.transform, PrimitiveType.Cylinder, "Top Plate", new Vector3(0f, 0.18f, 0f),
                new Vector3(0.84f, 0.02f, 0.84f), stoneMaterial, null, true);
            topPlate.GetComponent<Collider>().material = GetOrCreatePhysicsMaterial();

            GameObject topMarker = new GameObject("Foundation Top");
            topMarker.transform.SetParent(foundation.transform, false);
            topMarker.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            return foundation;
        }

        private static GameObject CreateElevator(Transform parent, Material bronzeMaterial, Material stoneMaterial, out Transform movingPlatform,
            out Transform leftGuide, out Transform rightGuide)
        {
            GameObject elevator = new GameObject("Player Elevator");
            elevator.transform.SetParent(parent, false);
            elevator.transform.localPosition = new Vector3(0f, 0.2f, -1.15f);

            leftGuide = CreatePrimitive(elevator.transform, PrimitiveType.Cube, "Left Guide", new Vector3(-0.56f, 0.175f, 0f),
                new Vector3(0.05f, 0.35f, 0.05f), bronzeMaterial).transform;
            rightGuide = CreatePrimitive(elevator.transform, PrimitiveType.Cube, "Right Guide", new Vector3(0.56f, 0.175f, 0f),
                new Vector3(0.05f, 0.35f, 0.05f), bronzeMaterial).transform;

            GameObject platform = new GameObject("Moving Platform");
            platform.transform.SetParent(elevator.transform, false);
            movingPlatform = platform.transform;
            CreatePrimitive(platform.transform, PrimitiveType.Cylinder, "Elevator Floor", new Vector3(0f, 0.05f, 0f),
                new Vector3(1.25f, 0.05f, 1.15f), stoneMaterial, null, true);
            CreatePrimitive(platform.transform, PrimitiveType.Cylinder, "Elevator Rim", new Vector3(0f, 0.115f, 0f),
                new Vector3(1.16f, 0.015f, 1.06f), bronzeMaterial);
            CreatePrimitive(platform.transform, PrimitiveType.Cube, "Left Rail", new Vector3(-0.55f, 0.34f, 0.02f),
                new Vector3(0.035f, 0.45f, 0.75f), bronzeMaterial);
            CreatePrimitive(platform.transform, PrimitiveType.Cube, "Right Rail", new Vector3(0.55f, 0.34f, 0.02f),
                new Vector3(0.035f, 0.45f, 0.75f), bronzeMaterial);
            return elevator;
        }

        private static void CreateTray(Transform platform, GameObject housePrefab, Material houseMaterial, Material bronzeMaterial,
            StackingHouseGameController gameController)
        {
            GameObject tray = new GameObject("House Supply Tray");
            tray.transform.SetParent(platform, false);
            tray.transform.localPosition = new Vector3(0.82f, 0.18f, 0.05f);
            tray.transform.localRotation = Quaternion.Euler(0f, -10f, 0f);

            CreatePrimitive(tray.transform, PrimitiveType.Cube, "Tray Deck", new Vector3(0f, 0.04f, 0f),
                new Vector3(0.66f, 0.05f, 1.05f), bronzeMaterial, null, true);
            CreatePrimitive(tray.transform, PrimitiveType.Cube, "Tray Back", new Vector3(0.31f, 0.17f, 0f),
                new Vector3(0.04f, 0.25f, 1.05f), houseMaterial, null, true);

            List<Transform> slots = new List<Transform>();
            for (int index = 0; index < 2; index++)
            {
                GameObject slot = new GameObject("House Slot " + (index + 1));
                slot.transform.SetParent(tray.transform, false);
                slot.transform.localPosition = new Vector3(0f, 0.07f, index == 0 ? -0.26f : 0.26f);
                slot.transform.localRotation = Quaternion.Euler(0f, index == 0 ? 4f : -4f, 0f);
                slots.Add(slot.transform);
            }

            HouseTrayController trayController = tray.AddComponent<HouseTrayController>();
            trayController.Configure(gameController, housePrefab, slots);

            for (int index = 0; index < slots.Count; index++)
            {
                GameObject houseInstance = PrefabUtility.InstantiatePrefab(housePrefab, SceneManager.GetActiveScene()) as GameObject;
                houseInstance.name = "House Ready " + (index + 1);
                houseInstance.transform.SetParent(slots[index], false);
                houseInstance.transform.localPosition = Vector3.zero;
                houseInstance.transform.localRotation = Quaternion.identity;
                trayController.SetInitialHouse(index, houseInstance.GetComponent<StackableHouse>());
            }
        }

        private static bool ImportRequiredInteractionSamples()
        {
            bool imported = false;
            imported |= ImportSampleIfNeeded("com.unity.xr.interaction.toolkit", "Starter Assets");
            imported |= ImportSampleIfNeeded("com.unity.xr.interaction.toolkit", SimulatorSampleName);
            imported |= ImportSampleIfNeeded("com.bytedance.pico.xr", "PICO Interaction Demo");
            if (!imported)
                return false;

            SessionState.SetBool(ResumeBuildKey, true);
            AssetDatabase.Refresh();
            return true;
        }

        private static bool ImportSampleIfNeeded(string packageName, string sampleDisplayName)
        {
            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForPackageName(packageName);
            if (packageInfo == null)
            {
                Debug.LogError("Required package is not installed: " + packageName);
                return false;
            }

            foreach (Sample sample in Sample.FindByPackage(packageInfo.name, packageInfo.version))
            {
                if (sample.displayName != sampleDisplayName)
                    continue;

                if (Directory.Exists(sample.importPath))
                    return false;

                if (sample.Import(Sample.ImportOptions.HideImportWindow))
                {
                    Debug.Log("Imported required sample: " + sampleDisplayName);
                    return true;
                }
            }

            return false;
        }

        private static void ConfigureEditorInteractionSimulator()
        {
            GameObject simulatorPrefab = LoadImportedSamplePrefab(
                "com.unity.xr.interaction.toolkit", SimulatorSampleName, "XR Interaction Simulator.prefab");
            ScriptableObject simulatorSettings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SimulatorSettingsPath);
            if (simulatorPrefab == null || simulatorSettings == null)
            {
                Debug.LogError("XR Interaction Simulator could not be configured. Re-run Create or Refresh Gameplay after package samples finish importing.");
                return;
            }

            SerializedObject serializedSettings = new SerializedObject(simulatorSettings);
            SerializedProperty autoInstantiate = serializedSettings.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab");
            SerializedProperty editorOnly = serializedSettings.FindProperty("m_AutomaticallyInstantiateInEditorOnly");
            SerializedProperty useClassic = serializedSettings.FindProperty("m_UseClassic");
            SerializedProperty prefab = serializedSettings.FindProperty("m_SimulatorPrefab");
            if (autoInstantiate == null || editorOnly == null || useClassic == null || prefab == null)
            {
                Debug.LogError("XR Interaction Simulator settings format was not recognized.");
                return;
            }

            autoInstantiate.boolValue = true;
            editorOnly.boolValue = true;
            useClassic.boolValue = false;
            prefab.objectReferenceValue = simulatorPrefab;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(simulatorSettings);
        }

        private static GameObject LoadImportedSamplePrefab(string packageName, string sampleDisplayName, string prefabName)
        {
            UnityEditor.PackageManager.PackageInfo packageInfo =
                UnityEditor.PackageManager.PackageInfo.FindForPackageName(packageName);
            if (packageInfo == null)
                return null;

            foreach (Sample sample in Sample.FindByPackage(packageInfo.name, packageInfo.version))
            {
                if (sample.displayName != sampleDisplayName || !Directory.Exists(sample.importPath))
                    continue;

                string importPath = sample.importPath.Replace('\\', '/').TrimEnd('/');
                string dataPath = Application.dataPath.Replace('\\', '/');
                if (Path.IsPathRooted(importPath) && importPath.StartsWith(dataPath))
                    importPath = "Assets" + importPath.Substring(dataPath.Length);

                return AssetDatabase.LoadAssetAtPath<GameObject>(importPath + "/" + prefabName);
            }

            return null;
        }

        private static void SetupVrRig(Transform platform)
        {
            XROrigin[] existingOrigins = Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include);
            GameObject rig = null;
            foreach (XROrigin existingOrigin in existingOrigins)
            {
                if (existingOrigin.gameObject.name == "VR Player Rig")
                {
                    rig = existingOrigin.gameObject;
                    break;
                }
            }
            if (rig == null)
            {
                GameObject rigPrefab = LoadPicoRigPrefab();
                if (rigPrefab == null)
                {
                    Debug.LogError("PICO interaction sample prefab could not be loaded. Gameplay world was created, but an XR rig could not be installed.");
                    return;
                }

                rig = PrefabUtility.InstantiatePrefab(rigPrefab, SceneManager.GetActiveScene()) as GameObject;
                rig.name = "VR Player Rig";
            }

            rig.transform.SetParent(platform, false);
            rig.transform.localPosition = new Vector3(0f, 0.11f, 0f);
            rig.transform.localRotation = Quaternion.identity;
            rig.transform.localScale = Vector3.one;
            rig.SetActive(true);

            XROrigin origin = rig.GetComponent<XROrigin>();
            if (origin != null)
            {
                origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
                origin.CameraYOffset = EditorEyeHeight;
                if (origin.CameraFloorOffsetObject != null)
                {
                    Transform cameraOffset = origin.CameraFloorOffsetObject.transform;
                    Vector3 offsetPosition = cameraOffset.localPosition;
                    offsetPosition.y = EditorEyeHeight;
                    cameraOffset.localPosition = offsetPosition;
                }
            }

            Camera[] existingCameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            foreach (Camera existingCamera in existingCameras)
            {
                if (existingCamera.GetComponentInParent<XROrigin>() != origin && existingCamera.name == "Main Camera")
                    existingCamera.gameObject.SetActive(false);
            }
        }

        private static GameObject LoadPicoRigPrefab()
        {
            const string packagePrefabPath = "Packages/com.bytedance.pico.xr/Samples~/PICOInteractionDemo/Prefabs/XR Origin (VR).prefab";
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(packagePrefabPath);

            if (sourcePrefab == null)
            {
                foreach (Sample sample in Sample.FindByPackage("com.bytedance.pico.xr", string.Empty))
                {
                    if (sample.displayName != "PICO Interaction Demo")
                        continue;

                    if (!Directory.Exists(sample.importPath))
                        sample.Import(Sample.ImportOptions.OverridePreviousImports | Sample.ImportOptions.HideImportWindow);

                    string importPath = sample.importPath.Replace('\\', '/');
                    if (Path.IsPathRooted(importPath))
                        importPath = "Assets" + importPath.Substring(Application.dataPath.Length);
                    sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(importPath + "/Prefabs/XR Origin (VR).prefab");
                    if (sourcePrefab != null)
                        break;
                }
            }

            return sourcePrefab != null ? CreateOrUpdateControllerRigPrefab(sourcePrefab) : null;
        }

        private static GameObject CreateOrUpdateControllerRigPrefab(GameObject sourcePrefab)
        {
            string sourcePath = AssetDatabase.GetAssetPath(sourcePrefab);
            GameObject prefabContents = PrefabUtility.LoadPrefabContents(sourcePath);
            Transform[] allTransforms = prefabContents.GetComponentsInChildren<Transform>(true);
            for (int index = allTransforms.Length - 1; index >= 0; index--)
            {
                Transform child = allTransforms[index];
                if (child != null && child != prefabContents.transform &&
                    (child.name == "Left Hand" || child.name == "Right Hand" || child.name == "Hand Visualizer"))
                    Object.DestroyImmediate(child.gameObject);
            }

            foreach (Transform child in prefabContents.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.RemoveMonoBehavioursWithMissingScript(child.gameObject);

            prefabContents.name = "VR Player Rig";
            PrefabUtility.SaveAsPrefabAsset(prefabContents, ControllerRigPrefabPath);
            PrefabUtility.UnloadPrefabContents(prefabContents);
            AssetDatabase.ImportAsset(ControllerRigPrefabPath);
            return AssetDatabase.LoadAssetAtPath<GameObject>(ControllerRigPrefabPath);
        }

        private static void ConfigureXrStartup()
        {
            XRGeneralSettings androidSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
            if (androidSettings == null)
                return;

            androidSettings.InitManagerOnStart = true;
            EditorUtility.SetDirty(androidSettings);
        }

        private static void ConfigureEditorInput()
        {
            PlayerSettings.runInBackground = true;

            InputSettings inputSettings = InputSystem.settings;
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(inputSettings)))
            {
                inputSettings = AssetDatabase.LoadAssetAtPath<InputSettings>(InputSettingsPath);
                if (inputSettings == null)
                {
                    inputSettings = Object.Instantiate(InputSystem.settings);
                    inputSettings.name = "Stacking House Input Settings";
                    inputSettings.hideFlags = HideFlags.None;
                    AssetDatabase.CreateAsset(inputSettings, InputSettingsPath);
                }

                InputSystem.settings = inputSettings;
            }

            inputSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            inputSettings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            EditorUtility.SetDirty(inputSettings);
        }

        private static void ConfigurePhysics()
        {
            Physics.defaultSolverIterations = Mathf.Max(Physics.defaultSolverIterations, 12);
            Physics.defaultSolverVelocityIterations = Mathf.Max(Physics.defaultSolverVelocityIterations, 4);
        }

        private static GameObject CreatePrimitive(Transform parent, PrimitiveType primitiveType, string objectName, Vector3 localPosition,
            Vector3 localScale, Material material, Vector3? localEulerAngles = null, bool keepCollider = false)
        {
            GameObject primitive = GameObject.CreatePrimitive(primitiveType);
            primitive.name = objectName;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localRotation = Quaternion.Euler(localEulerAngles ?? Vector3.zero);
            primitive.transform.localScale = localScale;
            Renderer renderer = primitive.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
            if (!keepCollider)
            {
                Collider collider = primitive.GetComponent<Collider>();
                if (collider != null)
                    Object.DestroyImmediate(collider);
            }
            else if (primitiveType == PrimitiveType.Cylinder)
            {
                Collider collider = primitive.GetComponent<Collider>();
                if (collider != null)
                    Object.DestroyImmediate(collider);

                BoxCollider boxCollider = primitive.AddComponent<BoxCollider>();
                boxCollider.size = new Vector3(1f, 2f, 1f);
            }
            return primitive;
        }

        private static void ClearChildren(Transform transform)
        {
            for (int index = transform.childCount - 1; index >= 0; index--)
                Object.DestroyImmediate(transform.GetChild(index).gameObject);
        }

        private static void RemoveGeneratedComponents(GameObject target)
        {
            Component[] components = target.GetComponents<Component>();
            for (int index = components.Length - 1; index >= 0; index--)
            {
                Component component = components[index];
                if (component != null && !(component is Transform))
                    Object.DestroyImmediate(component);
            }
        }
    }

}
