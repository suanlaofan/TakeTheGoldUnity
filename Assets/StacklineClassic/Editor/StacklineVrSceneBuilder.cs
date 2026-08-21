using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;
using Wukong.StacklineClassic;

namespace Wukong.EditorTools
{
    /// <summary>
    /// Creates a disposable PICO scene from the current desktop authoring scene. EnvironmentScene
    /// remains the user's source of truth, including its hand-authored terrain and transforms.
    /// </summary>
    public static class StacklineVrSceneBuilder
    {
        public const string SourceScenePath = "Assets/Scenes/EnvironmentScene.unity";
        public const string VrScenePath = "Assets/Scenes/StacklineVR.unity";
        private const string VrRigPrefabPath = "Assets/Samples/PICO XR/0.13.1/PICO Interaction Demo/Prefabs/XR Origin (VR).prefab";
        private const string GoldBarPrefabPath = "Assets/StacklineClassic/Art/AngeGoldBar.glb";
        private const string VrRigName = "[PICO] Stackline XR Origin";

        [MenuItem("Tools/Stackline Classic/Create or Refresh PICO VR Scene")]
        public static void BuildAndSaveVrScene()
        {
            RecreateVrSceneFromCurrentSource();
            Scene vrScene = EditorSceneManager.OpenScene(VrScenePath, OpenSceneMode.Single);

            GameObject desktopCameraObject = FindRootOrChild("Stackline Gameplay Camera", vrScene);
            Camera desktopCamera = desktopCameraObject != null ? desktopCameraObject.GetComponent<Camera>() : null;
            if (desktopCamera == null)
                throw new BuildFailedException("Stackline Gameplay Camera is missing from " + VrScenePath);

            GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VrRigPrefabPath);
            if (rigPrefab == null)
                throw new BuildFailedException("PICO XR Origin prefab is missing: " + VrRigPrefabPath);

            GameObject vrRig = PrefabUtility.InstantiatePrefab(rigPrefab, vrScene) as GameObject;
            if (vrRig == null)
                throw new BuildFailedException("Could not instantiate the PICO XR Origin prefab.");
            vrRig.name = VrRigName;

            Camera hmdCamera = vrRig.GetComponentInChildren<Camera>(true);
            if (hmdCamera == null)
                throw new BuildFailedException("PICO XR Origin does not contain an HMD camera.");

            // Match the desktop camera's authored view at startup, but leave its position and
            // rotation to the runtime after launch. The sample rig uses a 1.6 m floor offset.
            Vector3 desktopPosition = desktopCamera.transform.position;
            Vector3 desktopForward = desktopCamera.transform.forward;
            desktopForward.y = 0f;
            if (desktopForward.sqrMagnitude < 0.001f)
                desktopForward = Vector3.forward;
            desktopForward.Normalize();
            vrRig.transform.SetPositionAndRotation(desktopPosition - Vector3.up * 1.6f,
                Quaternion.LookRotation(desktopForward, Vector3.up));
            vrRig.transform.localScale = Vector3.one;
            hmdCamera.tag = "MainCamera";
            hmdCamera.cullingMask = ~0;

            DisableDesktopCameraAndListeners(vrScene, hmdCamera);
            ConfigureGameplay(vrScene, hmdCamera);
            ConfigureEventSystem(vrScene);

            EditorSceneManager.MarkSceneDirty(vrScene);
            EditorSceneManager.SaveScene(vrScene);
            AssetDatabase.SaveAssets();
            Debug.Log("PICO VR scene saved: " + VrScenePath);
        }

        private static void RecreateVrSceneFromCurrentSource()
        {
            if (File.Exists(VrScenePath))
            {
                if (!AssetDatabase.DeleteAsset(VrScenePath))
                    throw new BuildFailedException("Could not refresh generated PICO scene: " + VrScenePath);
            }
            if (!AssetDatabase.CopyAsset(SourceScenePath, VrScenePath))
                throw new BuildFailedException("Could not copy current EnvironmentScene into " + VrScenePath);
            AssetDatabase.Refresh();
        }

        private static void ConfigureGameplay(Scene scene, Camera hmdCamera)
        {
            GameObject root = FindRoot("Stackline Classic Game", scene);
            StacklineClassicController controller = root != null ? root.GetComponent<StacklineClassicController>() : null;
            StacklineHud hud = root != null ? root.GetComponentInChildren<StacklineHud>(true) : null;
            StacklineTapTarget tapTarget = root != null ? root.GetComponentInChildren<StacklineTapTarget>(true) : null;
            Transform arenaAnchor = FindChild(root != null ? root.transform : null, "Arena Anchor") ?? (root != null ? root.transform : null);
            GameObject goldBar = AssetDatabase.LoadAssetAtPath<GameObject>(GoldBarPrefabPath);

            if (controller == null || hud == null || tapTarget == null || arenaAnchor == null || goldBar == null)
                throw new BuildFailedException("Stackline VR scene is missing a controller, HUD, tap target, anchor, or gold bar prefab.");

            controller.ConfigureVr(arenaAnchor, hmdCamera, hud, tapTarget, goldBar);
            EditorUtility.SetDirty(controller);
        }

        private static void ConfigureEventSystem(Scene scene)
        {
            EventSystem eventSystem = Object.FindAnyObjectByType<EventSystem>(FindObjectsInactive.Exclude);
            if (eventSystem == null || eventSystem.gameObject.scene != scene)
            {
                GameObject eventObject = new GameObject("Stackline PICO EventSystem");
                SceneManager.MoveGameObjectToScene(eventObject, scene);
                eventSystem = eventObject.AddComponent<EventSystem>();
            }

            foreach (BaseInputModule module in eventSystem.GetComponents<BaseInputModule>())
            {
                if (!(module is XRUIInputModule))
                    Object.DestroyImmediate(module);
            }

            XRUIInputModule xrInput = eventSystem.GetComponent<XRUIInputModule>();
            if (xrInput == null)
                xrInput = eventSystem.gameObject.AddComponent<XRUIInputModule>();
            xrInput.activeInputMode = XRUIInputModule.ActiveInputMode.InputSystemActions;
            xrInput.enableXRInput = true;
            xrInput.enableBuiltinActionsAsFallback = true;
            EditorUtility.SetDirty(eventSystem);
            EditorUtility.SetDirty(xrInput);
        }

        private static void DisableDesktopCameraAndListeners(Scene scene, Camera hmdCamera)
        {
            foreach (Camera camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (camera != null && camera.gameObject.scene == scene && camera != hmdCamera &&
                    camera.name == "Stackline Gameplay Camera")
                    camera.gameObject.SetActive(false);
            }

            foreach (AudioListener listener in Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (listener != null && listener.gameObject.scene == scene && listener.gameObject != hmdCamera.gameObject)
                    listener.enabled = false;
            }
        }

        private static GameObject FindRoot(string name, Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root;
            }
            return null;
        }

        private static GameObject FindRootOrChild(string name, Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindChild(root.transform, name);
                if (found != null)
                    return found.gameObject;
            }
            return null;
        }

        private static Transform FindChild(Transform parent, string name)
        {
            if (parent == null)
                return null;
            if (parent.name == name)
                return parent;
            foreach (Transform child in parent)
            {
                Transform found = FindChild(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
