using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Wukong.StacklineClassic;

namespace Wukong.EditorTools
{
    /// <summary>
    /// Installs the classic tap-to-stack game as an isolated root in the current scene.
    /// The existing environment and legacy gameplay roots are kept intact.
    /// </summary>
    public static class StacklineClassicSceneBuilder
    {
        private const string RootName = "Stackline Classic Game";
        private const string LegacyRootName = "Stacking House VR";
        private const string GeneratedRoot = "Assets/StacklineClassic/Generated";
        private const string StageMaterialPath = GeneratedRoot + "/StacklineStage.mat";
        private const string TrimMaterialPath = GeneratedRoot + "/StacklineTrim.mat";
        private const string AccentMaterialPath = GeneratedRoot + "/StacklineAccent.mat";
        private const string GripMaterialPath = GeneratedRoot + "/StacklineGrip.asset";
        private const string GoldBarAssetPath = "Assets/StacklineClassic/Art/AngeGoldBar.glb";
        private const string SimulatorSettingsPath = "Assets/XRI/Settings/Resources/XRDeviceSimulatorSettings.asset";
        private const int GameplayLayer = 30;

        [MenuItem("Tools/Stackline Classic/Create or Refresh in Current Scene")]
        public static void CreateOrRefreshGameplay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Stop Play Mode before creating Stackline Classic gameplay.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                Debug.LogError("Stackline Classic requires a loaded scene.");
                return;
            }

            EnsureGeneratedFolder();
            DisableAutoXrSimulator();
            Material stageMaterial = GetOrCreateMaterial(StageMaterialPath,
                new Color(0.06f, 0.17f, 0.16f, 1f), 0.58f, 0.18f);
            Material trimMaterial = GetOrCreateMaterial(TrimMaterialPath,
                new Color(0.82f, 0.43f, 0.10f, 1f), 0.72f, 0.58f);
            Material accentMaterial = GetOrCreateMaterial(AccentMaterialPath,
                new Color(0.93f, 0.68f, 0.20f, 1f), 0.72f, 0.62f);
            PhysicsMaterial gripMaterial = GetOrCreatePhysicsMaterial();
            GameObject goldBarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GoldBarAssetPath);
            if (goldBarPrefab == null)
                Debug.LogWarning("The ange-embed gold bar asset could not be loaded. A metallic gold fallback will be used.");

            GameObject oldRoot = FindRootInScene(LegacyRootName, scene);
            if (oldRoot != null && oldRoot.activeSelf)
            {
                Undo.RecordObject(oldRoot, "Disable legacy stacking gameplay");
                oldRoot.SetActive(false);
            }

            GameObject previousRoot = FindRootInScene(RootName, scene);
            if (previousRoot != null)
                Undo.DestroyObjectImmediate(previousRoot);

            Transform playerSpawn = FindSceneTransform("PlayerSpawn", scene);
            GameObject root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Create Stackline Classic gameplay");
            root.layer = GameplayLayer;
            root.transform.SetPositionAndRotation(
                playerSpawn != null ? playerSpawn.position : Vector3.zero,
                playerSpawn != null ? playerSpawn.rotation : Quaternion.identity);
            if (playerSpawn == null)
                Debug.LogWarning("PlayerSpawn was not found. Stackline Classic was placed at the world origin.");

            Transform arenaAnchor = CreateAnchor(root.transform);
            CreateStage(root.transform, stageMaterial, trimMaterial, accentMaterial, gripMaterial);
            CreateGameplayLighting(root.transform);
            Camera gameplayCamera = CreateGameplayCamera(root.transform);
            StacklineHud hud = CreateHud(root.transform);
            StacklineTapTarget tapTarget = CreateTapTarget(root.transform);
            StacklineClassicController controller = root.AddComponent<StacklineClassicController>();
            controller.Configure(arenaAnchor, gameplayCamera, hud, tapTarget, goldBarPrefab);
            EditorUtility.SetDirty(controller);

            EnsureEventSystem(root.transform);
            EnsureInteractionManager(root.transform);
            DisableLegacyMainCamera(oldRoot);
            SetLayerRecursively(root.transform, GameplayLayer);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(scene);
            AssetDatabase.SaveAssets();
            if (!string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);

            Debug.Log("Stackline Classic gameplay installed at PlayerSpawn. Click Tools > Stackline Classic > Focus Gameplay to inspect it.");
        }

        [MenuItem("Tools/Stackline Classic/Focus Gameplay")]
        public static void FocusGameplay()
        {
            GameObject root = FindRootInScene(RootName, SceneManager.GetActiveScene());
            if (root == null)
            {
                Debug.LogWarning("Create Stackline Classic gameplay first.");
                return;
            }

            Selection.activeGameObject = root;
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null)
                return;

            sceneView.pivot = root.transform.position + root.transform.up * 1.2f;
            sceneView.rotation = root.transform.rotation * Quaternion.Euler(22f, 0f, 0f);
            sceneView.size = 8f;
            sceneView.orthographic = false;
            sceneView.Repaint();
        }

        private static Transform CreateAnchor(Transform parent)
        {
            GameObject anchor = new GameObject("Arena Anchor");
            anchor.transform.SetParent(parent, false);
            anchor.transform.localPosition = Vector3.zero;
            anchor.transform.localRotation = Quaternion.identity;
            anchor.transform.localScale = Vector3.one;
            return anchor.transform;
        }

        private static void CreateStage(Transform parent, Material stageMaterial, Material trimMaterial,
            Material accentMaterial, PhysicsMaterial gripMaterial)
        {
            GameObject stage = CreatePrimitive(parent, PrimitiveType.Cube, "Stackline Stage",
                new Vector3(0f, -0.10f, 0f), new Vector3(8.4f, 0.20f, 8.4f), stageMaterial, true);
            BoxCollider stageCollider = stage.GetComponent<BoxCollider>();
            if (stageCollider != null)
                stageCollider.sharedMaterial = gripMaterial;

            // A thin trim frame makes the marked gameplay area legible without covering the environment.
            Renderer stageRenderer = stage.GetComponent<Renderer>();
            if (stageRenderer != null)
                stageRenderer.enabled = false;
        }

        private static Camera CreateGameplayCamera(Transform parent)
        {
            GameObject cameraObject = new GameObject("Stackline Gameplay Camera");
            cameraObject.transform.SetParent(parent, false);
            cameraObject.transform.localPosition = new Vector3(6.8f, 6.1f, -9.4f);
            Vector3 localTarget = new Vector3(0f, 2.0f, 0f);
            cameraObject.transform.localRotation = Quaternion.LookRotation(
                localTarget - cameraObject.transform.localPosition, Vector3.up);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 7.8f;
            camera.fieldOfView = 48f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 1000f;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = new Color(0.42f, 0.73f, 0.68f, 1f);
            // The legacy gameplay root is inactive, so rendering every scene layer restores the
            // original environment while still including Stackline's dedicated gameplay layer.
            camera.cullingMask = ~0;
            camera.depth = 10f;
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.stereoTargetEye = StereoTargetEyeMask.None;

            if (Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude).Length == 0)
                cameraObject.AddComponent<AudioListener>();
            return camera;
        }

        private static void CreateGameplayLighting(Transform parent)
        {
            CreateDirectionalLight(parent, "Stackline Key Light", new Color(1f, 0.91f, 0.78f), 0.68f,
                new Vector3(48f, -38f, 0f), true);
            CreateDirectionalLight(parent, "Stackline Fill Light", new Color(0.56f, 0.76f, 1f), 0.24f,
                new Vector3(24f, 142f, 0f), false);
        }

        private static void CreateDirectionalLight(Transform parent, string name, Color color, float intensity,
            Vector3 rotation, bool castShadows)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.layer = GameplayLayer;
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.localRotation = Quaternion.Euler(rotation);

            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.cullingMask = 1 << GameplayLayer;
            light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
            light.shadowStrength = castShadows ? 0.32f : 0f;
            light.renderMode = LightRenderMode.ForcePixel;
        }

        private static StacklineHud CreateHud(Transform parent)
        {
            GameObject hudObject = new GameObject("Stackline HUD");
            hudObject.transform.SetParent(parent, false);
            hudObject.transform.localPosition = Vector3.zero;
            hudObject.transform.localRotation = Quaternion.identity;
            hudObject.transform.localScale = Vector3.one;
            return hudObject.AddComponent<StacklineHud>();
        }

        private static StacklineTapTarget CreateTapTarget(Transform parent)
        {
            GameObject targetObject = new GameObject("Stackline Tap Target");
            targetObject.transform.SetParent(parent, false);
            targetObject.transform.localPosition = new Vector3(0f, 2.4f, 2.8f);
            targetObject.transform.localRotation = Quaternion.identity;
            targetObject.transform.localScale = Vector3.one;

            BoxCollider collider = targetObject.AddComponent<BoxCollider>();
            collider.size = new Vector3(10f, 6f, 0.08f);
            collider.isTrigger = true;
            targetObject.AddComponent<XRSimpleInteractable>();
            return targetObject.AddComponent<StacklineTapTarget>();
        }

        private static void EnsureEventSystem(Transform parent)
        {
            EventSystem eventSystem = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Exclude);
            if (eventSystem == null)
            {
                GameObject eventObject = new GameObject("Stackline EventSystem");
                eventObject.transform.SetParent(parent, false);
                eventSystem = eventObject.AddComponent<EventSystem>();
                Undo.RegisterCreatedObjectUndo(eventObject, "Create Stackline EventSystem");
            }

            InputSystemUIInputModule inputModule = eventSystem.GetComponent<InputSystemUIInputModule>();
            if (inputModule == null)
            {
                foreach (BaseInputModule module in eventSystem.GetComponents<BaseInputModule>())
                    module.enabled = false;
                inputModule = eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }

            if (inputModule.actionsAsset == null)
                inputModule.AssignDefaultActions();
            EditorUtility.SetDirty(eventSystem);
            EditorUtility.SetDirty(inputModule);
        }

        private static void EnsureInteractionManager(Transform parent)
        {
            XRInteractionManager manager = Object.FindAnyObjectByType<XRInteractionManager>(FindObjectsInactive.Exclude);
            if (manager != null)
                return;

            GameObject managerObject = new GameObject("Stackline XR Interaction Manager");
            managerObject.transform.SetParent(parent, false);
            managerObject.AddComponent<XRInteractionManager>();
            Undo.RegisterCreatedObjectUndo(managerObject, "Create Stackline XR Interaction Manager");
        }

        private static void DisableLegacyMainCamera(GameObject legacyRoot)
        {
            if (legacyRoot == null)
                return;

            foreach (Camera camera in legacyRoot.GetComponentsInChildren<Camera>(true))
                camera.enabled = false;
            foreach (AudioListener listener in legacyRoot.GetComponentsInChildren<AudioListener>(true))
                listener.enabled = false;
        }

        private static GameObject CreatePrimitive(Transform parent, PrimitiveType primitiveType, string name,
            Vector3 localPosition, Vector3 localScale, Material material, bool keepCollider)
        {
            GameObject primitive = GameObject.CreatePrimitive(primitiveType);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localRotation = Quaternion.identity;
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
            return primitive;
        }

        private static Material GetOrCreateMaterial(string path, Color color, float smoothness, float metallic)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = FindLitShader();
                material = new Material(shader) { name = System.IO.Path.GetFileNameWithoutExtension(path) };
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

        private static PhysicsMaterial GetOrCreatePhysicsMaterial()
        {
            PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(GripMaterialPath);
            if (material == null)
            {
                material = new PhysicsMaterial("Stackline Grip");
                AssetDatabase.CreateAsset(material, GripMaterialPath);
            }

            material.staticFriction = 0.88f;
            material.dynamicFriction = 0.74f;
            material.bounciness = 0f;
            material.frictionCombine = PhysicsMaterialCombine.Maximum;
            material.bounceCombine = PhysicsMaterialCombine.Minimum;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Shader FindLitShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Unlit/Color");
        }

        private static void DisableAutoXrSimulator()
        {
            ScriptableObject settings = AssetDatabase.LoadAssetAtPath<ScriptableObject>(SimulatorSettingsPath);
            if (settings == null)
                return;

            SerializedObject serializedSettings = new SerializedObject(settings);
            SerializedProperty autoInstantiate = serializedSettings.FindProperty("m_AutomaticallyInstantiateSimulatorPrefab");
            if (autoInstantiate == null || !autoInstantiate.boolValue)
                return;

            autoInstantiate.boolValue = false;
            serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        private static void EnsureGeneratedFolder()
        {
            EnsureFolder("Assets", "StacklineClassic");
            EnsureFolder("Assets/StacklineClassic", "Generated");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = parent + "/" + child;
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static GameObject FindRootInScene(string name, Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root;
            }
            return null;
        }

        private static Transform FindSceneTransform(string name, Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform.name == name && transform.gameObject.scene == scene)
                        return transform;
                }
            }
            return null;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            if (root == null)
                return;
            root.gameObject.layer = layer;
            foreach (Transform child in root)
                SetLayerRecursively(child, layer);
        }
    }
}
