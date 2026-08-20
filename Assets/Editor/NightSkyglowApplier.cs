using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class NightSkyglowApplier
{
    private const string SkyboxPath = "Assets/Allsky/Night Skyglow/Night Skyglow foghigh/Night Skyglow foghigh.mat";

    [MenuItem("Tools/Environment/Apply Night Skyglow FogHigh Skybox")]
    public static void Apply()
    {
        Material skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
        if (skybox == null)
        {
            Debug.LogError("Could not load skybox material at " + SkyboxPath);
            return;
        }

        RenderSettings.skybox = skybox;
        foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
        {
            if (camera.gameObject.scene.IsValid())
            {
                camera.clearFlags = CameraClearFlags.Skybox;
            }
        }

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("Applied Night Skyglow foghigh skybox to the active scene.");
    }
}
