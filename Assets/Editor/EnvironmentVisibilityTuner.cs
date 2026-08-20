using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class EnvironmentVisibilityTuner
{
    [MenuItem("Tools/Environment/Improve Long-Distance Visibility")]
    public static void Apply()
    {
        Scene scene = SceneManager.GetActiveScene();
        Terrain[] terrains = Resources.FindObjectsOfTypeAll<Terrain>()
            .Where(terrain => terrain.gameObject.scene == scene)
            .ToArray();

        foreach (Terrain terrain in terrains)
        {
            terrain.basemapDistance = 220f;
        }

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.11f, 0.15f, 0.22f);
        RenderSettings.fogStartDistance = 65f;
        RenderSettings.fogEndDistance = 300f;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.13f, 0.17f, 0.27f);
        RenderSettings.ambientEquatorColor = new Color(0.075f, 0.10f, 0.16f);
        RenderSettings.ambientGroundColor = new Color(0.04f, 0.055f, 0.09f);
        RenderSettings.ambientIntensity = 0.95f;

        Light sun = Resources.FindObjectsOfTypeAll<Light>()
            .FirstOrDefault(light => light.gameObject.scene == scene
                && light.type == LightType.Directional
                && light.name == "Sun");
        if (sun != null)
        {
            RenderSettings.sun = sun;
        }

        DynamicGI.UpdateEnvironment();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Improved long-distance environment visibility for "
            + terrains.Length + " terrain object(s).");
    }
}
