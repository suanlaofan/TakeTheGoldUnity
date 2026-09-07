using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Wukong.StacklineClassic;
using Object = UnityEngine.Object;

namespace Wukong.EditorTools
{
    /// <summary>Reproducible mobile settings, applied only to the disposable PICO scene/assets.</summary>
    public static class StacklinePicoBuildProfile
    {
        public const string Folder = "Assets/StacklineClassic/Settings";
        public const string PipelinePath = Folder + "/StacklinePico_RPAsset.asset";
        public const string RendererPath = Folder + "/StacklinePico_Renderer.asset";
        public const string ReportFolder = "outputs/pico/review/performance-20260907";
        public static string ReportDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "..", ReportFolder));
        public const float RenderScale = 0.90f;
        public const float ShadowDistance = 25f;

        public static UniversalRenderPipelineAsset EnsureAssets()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/StacklineClassic", "Settings");
            CopyIfMissing("Assets/Settings/Mobile_Renderer.asset", RendererPath);
            CopyIfMissing("Assets/Settings/Mobile_RPAsset.asset", PipelinePath);
            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (renderer == null || pipeline == null)
                throw new BuildFailedException("Could not load the Stackline PICO pipeline or renderer.");

            // The mobile renderer has no features; keep that explicit so later source changes
            // cannot silently put SSAO or extra full-screen passes back into the PICO build.
            var rendererObject = new SerializedObject(renderer);
            Required(rendererObject, "m_RendererFeatures").arraySize = 0;
            Required(rendererObject, "m_RendererFeatureMap").arraySize = 0;
            Required(rendererObject, "m_RenderingMode").intValue = 0; // Forward
            Required(rendererObject, "m_DepthPrimingMode").intValue = 0;
            Required(rendererObject, "m_IntermediateTextureMode").intValue = 0; // Auto
            rendererObject.ApplyModifiedPropertiesWithoutUndo();

            var settings = new SerializedObject(pipeline);
            var renderers = Required(settings, "m_RendererDataList");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
            Set(settings, "m_DefaultRendererIndex", 0);
            Set(settings, "m_RequireDepthTexture", false);
            Set(settings, "m_RequireOpaqueTexture", false);
            Set(settings, "m_SupportsHDR", false);
            Set(settings, "m_MSAA", 1); // Hold AA constant for the first performance comparison.
            Set(settings, "m_RenderScale", RenderScale);
            Set(settings, "m_MainLightRenderingMode", 1);
            Set(settings, "m_MainLightShadowsSupported", true);
            Set(settings, "m_MainLightShadowmapResolution", 1024);
            Set(settings, "m_AdditionalLightsRenderingMode", 1); // PerPixel, capped per object.
            Set(settings, "m_AdditionalLightsPerObjectLimit", 2);
            Set(settings, "m_AdditionalLightShadowsSupported", false);
            Set(settings, "m_ShadowDistance", ShadowDistance);
            Set(settings, "m_ShadowCascadeCount", 1);
            Set(settings, "m_SoftShadowsSupported", false);
            Set(settings, "m_ReflectionProbeBlending", false);
            Set(settings, "m_ReflectionProbeBoxProjection", false);
            Set(settings, "m_SupportsLightCookies", false);
            Set(settings, "m_SupportsLightLayers", false);
            Set(settings, "m_UseSRPBatcher", true);
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
            return pipeline;
        }

        public static void ApplyToGeneratedScene(Scene scene, Camera hmdCamera)
        {
            if (scene.path != StacklineVrSceneBuilder.VrScenePath)
                throw new BuildFailedException("PICO optimizations may only modify the generated VR scene.");
            var pipeline = EnsureAssets();
            var cameras = InScene<Camera>(scene);
            foreach (var camera in cameras)
            {
                if (camera != hmdCamera)
                    camera.enabled = false;
            }
            hmdCamera.allowHDR = false;
            var cameraData = hmdCamera.GetComponent<UniversalAdditionalCameraData>();
            if (cameraData != null)
            {
                cameraData.renderPostProcessing = false;
                cameraData.requiresDepthOption = CameraOverrideOption.Off;
                cameraData.requiresColorOption = CameraOverrideOption.Off;
                cameraData.SetRenderer(0);
            }

            var lights = InScene<Light>(scene).Where(l => l.isActiveAndEnabled).ToArray();
            Light main = RenderSettings.sun;
            if (main == null || main.gameObject.scene != scene || !main.isActiveAndEnabled || main.type != LightType.Directional)
                main = lights.Where(l => l.type == LightType.Directional).OrderByDescending(l => l.intensity).FirstOrDefault();
            if (main == null)
                throw new BuildFailedException("The PICO scene has no active directional light.");
            RenderSettings.sun = main;
            foreach (var light in lights)
            {
                light.shadows = light == main ? LightShadows.Hard : LightShadows.None;
                // Preserve authored colors, brightness and placement. Baking/asset LOD needs
                // device profiling and a separate visual pass, rather than deleting the scene.
                EditorUtility.SetDirty(light);
            }

            foreach (var terrain in InScene<Terrain>(scene))
            {
                terrain.heightmapPixelError = Mathf.Max(terrain.heightmapPixelError, 12f);
                terrain.basemapDistance = Mathf.Min(terrain.basemapDistance, 100f);
                if (terrain.terrainData != null && terrain.terrainData.treeInstanceCount > 0)
                    terrain.treeDistance = Mathf.Min(terrain.treeDistance, 150f);
                if (terrain.terrainData != null && terrain.terrainData.detailPrototypes.Length > 0)
                    terrain.detailObjectDistance = Mathf.Min(terrain.detailObjectDistance, 30f);
                terrain.shadowCastingMode = ShadowCastingMode.Off;
                EditorUtility.SetDirty(terrain);
            }

            var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Stackline PICO Performance");
            if (root == null)
            {
                root = new GameObject("Stackline PICO Performance");
                SceneManager.MoveGameObjectToScene(root, scene);
            }
            var runtime = root.GetComponent<StacklinePicoRenderSettings>();
            if (runtime == null) runtime = root.AddComponent<StacklinePicoRenderSettings>();
            runtime.Configure(pipeline);
            EditorUtility.SetDirty(runtime);
            Validate(scene, hmdCamera);
            Directory.CreateDirectory(ReportDirectory);
            File.WriteAllText(Path.Combine(ReportDirectory, "generated-scene.json"), Inspect(scene));
        }

        public static void Validate(Scene scene, Camera expectedCamera = null)
        {
            var cameras = InScene<Camera>(scene).Where(c => c.isActiveAndEnabled).ToArray();
            var listeners = InScene<AudioListener>(scene).Where(c => c.isActiveAndEnabled).ToArray();
            var runtime = InScene<StacklinePicoRenderSettings>(scene);
            var shadows = InScene<Light>(scene).Where(l => l.isActiveAndEnabled && l.shadows != LightShadows.None).ToArray();
            if (cameras.Length != 1 || (expectedCamera != null && cameras[0] != expectedCamera) || listeners.Length != 1)
                throw new BuildFailedException("PICO scene requires one active HMD camera and one AudioListener.");
            if (runtime.Length != 1 || AssetDatabase.GetAssetPath(runtime[0].Pipeline) != PipelinePath)
                throw new BuildFailedException("PICO runtime pipeline binding is missing or incorrect.");
            if (shadows.Length != 1 || shadows[0].type != LightType.Directional || shadows[0].shadows != LightShadows.Hard)
                throw new BuildFailedException("PICO shadow budget requires one hard-shadow directional light.");
            var p = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            var r = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (p == null || r == null || p.supportsHDR || p.supportsCameraDepthTexture || p.supportsCameraOpaqueTexture ||
                p.supportsAdditionalLightShadows || p.shadowCascadeCount != 1 || r.rendererFeatures.Count != 0)
                throw new BuildFailedException("PICO render profile failed its build-time performance checks.");
            Debug.Log("STACKLINE_PICO_PROFILE_PASS: one camera/listener/shadow light; dedicated URP; no SSAO/HDR/extra shadow maps.");
        }

        public static string Inspect(Scene scene)
        {
            var lights = InScene<Light>(scene).Where(l => l.isActiveAndEnabled).ToArray();
            var terrains = InScene<Terrain>(scene);
            var report = new SceneReport
            {
                scene = scene.path, pipeline = PipelinePath, renderScale = RenderScale,
                activeCameras = InScene<Camera>(scene).Count(c => c.isActiveAndEnabled),
                activeLights = lights.Length,
                shadowLights = lights.Count(l => l.shadows != LightShadows.None),
                terrains = terrains.Length,
                actualTreeInstances = terrains.Where(t => t.terrainData != null).Sum(t => t.terrainData.treeInstanceCount),
                activeRenderers = InScene<Renderer>(scene).Count(r => r.enabled && r.gameObject.activeInHierarchy),
                caveat = "Editor component counts, not visible draw calls or measured device GPU costs."
            };
            return JsonUtility.ToJson(report, true);
        }

        public sealed class BuildPipelineScope : IDisposable
        {
            private readonly RenderPipelineAsset graphicsPipeline;
            private readonly RenderPipelineAsset qualityPipeline;
            private readonly string graphicsGuid, qualityGuid;
            private readonly bool hadGraphicsPipeline, hadQualityPipeline;
            public BuildPipelineScope(RenderPipelineAsset pipeline)
            {
                graphicsPipeline = GraphicsSettings.defaultRenderPipeline;
                qualityPipeline = QualitySettings.renderPipeline;
                hadGraphicsPipeline = graphicsPipeline != null;
                hadQualityPipeline = qualityPipeline != null;
                graphicsGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(graphicsPipeline));
                qualityGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(qualityPipeline));
                try
                {
                    GraphicsSettings.defaultRenderPipeline = pipeline;
                    QualitySettings.renderPipeline = pipeline;
                    AssetDatabase.SaveAssets();
                }
                catch
                {
                    // A throwing constructor never reaches using.Dispose(). Restore here too.
                    try { Dispose(); }
                    catch (Exception rollbackError) { Debug.LogException(rollbackError); }
                    throw;
                }
            }
            public void Dispose()
            {
                // BuildPlayer may unload the old, now unreferenced pipeline asset. Resolve
                // persistent assets again instead of restoring a destroyed Unity reference.
                try { GraphicsSettings.defaultRenderPipeline = RestorePipeline(graphicsPipeline, graphicsGuid, hadGraphicsPipeline); }
                finally { QualitySettings.renderPipeline = RestorePipeline(qualityPipeline, qualityGuid, hadQualityPipeline); }
                AssetDatabase.SaveAssets();
                Debug.Log("STACKLINE_BUILD_SETTINGS_RESTORED graphics=" + AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline) +
                          "; quality=" + AssetDatabase.GetAssetPath(QualitySettings.renderPipeline));
            }

            private static RenderPipelineAsset RestorePipeline(RenderPipelineAsset original, string guid, bool wasAssigned)
            {
                var restored = string.IsNullOrEmpty(guid) ? original :
                    AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (wasAssigned && restored == null)
                    throw new BuildFailedException("Cannot restore the original render pipeline asset (GUID " + guid + ").");
                return restored;
            }
        }

        [Serializable] private sealed class SceneReport
        {
            public string scene, pipeline, caveat;
            public float renderScale;
            public int activeCameras, activeLights, shadowLights, terrains, actualTreeInstances, activeRenderers;
        }
        private static T[] InScene<T>(Scene scene) where T : Component =>
            scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).ToArray();
        private static void CopyIfMissing(string source, string destination)
        {
            if (AssetDatabase.LoadMainAssetAtPath(destination) == null && !AssetDatabase.CopyAsset(source, destination))
                throw new BuildFailedException("Cannot create PICO asset from " + source);
        }
        private static SerializedProperty Required(SerializedObject target, string name) =>
            target.FindProperty(name) ?? throw new BuildFailedException("URP property unavailable: " + name);
        private static void Set(SerializedObject target, string name, bool value) => Required(target, name).boolValue = value;
        private static void Set(SerializedObject target, string name, int value) => Required(target, name).intValue = value;
        private static void Set(SerializedObject target, string name, float value) => Required(target, name).floatValue = value;
    }
}
