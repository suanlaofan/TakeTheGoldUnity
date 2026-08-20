using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class NightAtmosphereApplier
{
    private const string RootName = "Night Atmosphere";
    private const string GeneratedFolder = "Assets/Art/Lighting/Generated";
    private const string ProfilePath = GeneratedFolder + "/NightAtmosphereProfile.asset";

    [MenuItem("Tools/Environment/Apply Night Atmosphere")]
    public static void Apply()
    {
        EnsureFolder("Assets/Art");
        EnsureFolder("Assets/Art/Lighting");
        EnsureFolder(GeneratedFolder);

        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
        }

        if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath) != null)
        {
            AssetDatabase.DeleteAsset(ProfilePath);
        }

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Night Atmosphere Profile";

        Tonemapping tonemapping = profile.Add<Tonemapping>();
        tonemapping.active = true;
        tonemapping.mode.Override(TonemappingMode.ACES);

        ColorAdjustments colorAdjustments = profile.Add<ColorAdjustments>();
        colorAdjustments.active = true;
        colorAdjustments.postExposure.Override(0.18f);
        colorAdjustments.contrast.Override(11f);
        colorAdjustments.colorFilter.Override(new Color(0.84f, 0.90f, 1f, 1f));
        colorAdjustments.saturation.Override(-6f);

        WhiteBalance whiteBalance = profile.Add<WhiteBalance>();
        whiteBalance.active = true;
        whiteBalance.temperature.Override(-9f);
        whiteBalance.tint.Override(-2f);

        Vignette vignette = profile.Add<Vignette>();
        vignette.active = true;
        vignette.color.Override(new Color(0.005f, 0.012f, 0.028f, 1f));
        vignette.intensity.Override(0.12f);
        vignette.smoothness.Override(0.38f);

        AssetDatabase.CreateAsset(profile, ProfilePath);

        GameObject atmosphereObject = new GameObject(RootName);
        Volume volume = atmosphereObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f;
        volume.weight = 1f;
        volume.sharedProfile = profile;

        foreach (Light light in Resources.FindObjectsOfTypeAll<Light>())
        {
            if (!light.gameObject.scene.IsValid() || light.type != LightType.Directional)
            {
                continue;
            }

            light.color = new Color(0.62f, 0.72f, 0.98f);
            light.intensity = 0.62f;
            light.shadowStrength = 0.86f;
            light.shadows = LightShadows.Soft;
        }

        foreach (Camera camera in Resources.FindObjectsOfTypeAll<Camera>())
        {
            if (!camera.gameObject.scene.IsValid())
            {
                continue;
            }

            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            UniversalAdditionalCameraData cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData == null)
            {
                cameraData = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            }

            cameraData.renderPostProcessing = true;
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
        RenderSettings.reflectionIntensity = 0.38f;

        DynamicGI.UpdateEnvironment();
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = atmosphereObject;
        Debug.Log("Applied night lighting, fog, and post-processing to the active scene.");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        string name = path.Substring(slash + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }
}
