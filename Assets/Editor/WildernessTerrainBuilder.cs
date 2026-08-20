using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class WildernessTerrainBuilder
{
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";
    private const string GeneratedFolder = "Assets/Art/Terrain/Generated";
    private const string TerrainDataPath = GeneratedFolder + "/WildernessTerrain.asset";
    private const string DirtLayerPath = GeneratedFolder + "/DirtTerrainLayer.terrainlayer";
    private const string StoneLayerPath = GeneratedFolder + "/StoneTerrainLayer.terrainlayer";
    private const string DirtAlbedoPath = "Assets/Art/Terrain/Source/Dirt_Albedo.jpg";
    private const string DirtNormalPath = "Assets/Art/Terrain/Source/Dirt_Normal.jpg";
    private const string StoneAlbedoPath = "Assets/Art/Terrain/Source/Stone_Albedo.png";
    private const string StoneNormalPath = "Assets/Art/Terrain/Source/Stone_Normal.png";

    [MenuItem("Tools/Environment/Build Wilderness Terrain")]
    public static void Build()
    {
        EnsureFolder("Assets/Art");
        EnsureFolder("Assets/Art/Terrain");
        EnsureFolder(GeneratedFolder);
        ConfigureNormalMap(DirtNormalPath);
        ConfigureNormalMap(StoneNormalPath);

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            Object.DestroyImmediate(root);
        }

        DeleteGeneratedAsset(TerrainDataPath);
        DeleteGeneratedAsset(DirtLayerPath);
        DeleteGeneratedAsset(StoneLayerPath);

        TerrainData terrainData = CreateTerrainData();
        AssetDatabase.CreateAsset(terrainData, TerrainDataPath);

        TerrainLayer dirtLayer = CreateTerrainLayer(
            "Dirt",
            DirtAlbedoPath,
            DirtNormalPath,
            new Vector2(10f, 10f),
            0f);
        TerrainLayer stoneLayer = CreateTerrainLayer(
            "Stone",
            StoneAlbedoPath,
            StoneNormalPath,
            new Vector2(12f, 12f),
            0f);
        AssetDatabase.CreateAsset(dirtLayer, DirtLayerPath);
        AssetDatabase.CreateAsset(stoneLayer, StoneLayerPath);

        terrainData.terrainLayers = new[] { dirtLayer, stoneLayer };
        PaintTerrain(terrainData);

        GameObject environment = new GameObject("Environment");
        GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
        terrainObject.name = "Wilderness Terrain";
        terrainObject.transform.SetParent(environment.transform);
        terrainObject.transform.position = new Vector3(-80f, -4f, -80f);

        Terrain terrain = terrainObject.GetComponent<Terrain>();
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 8f;
        terrain.basemapDistance = 220f;
        terrain.shadowCastingMode = ShadowCastingMode.On;
        terrain.reflectionProbeUsage = ReflectionProbeUsage.Off;

        CreateLighting(environment.transform);
        CreatePreviewCamera(terrain);
        CreatePlayerSpawn(terrain);
        ConfigureAtmosphere();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = terrainObject;
        Debug.Log("Wilderness terrain created in " + ScenePath);
    }

    [MenuItem("Tools/Environment/Capture Wilderness Preview")]
    public static void CapturePreview()
    {
        Camera camera = Object.FindFirstObjectByType<Camera>();
        if (camera == null)
        {
            Debug.LogError("A preview camera is required before capturing the wilderness terrain.");
            return;
        }

        const int width = 1280;
        const int height = 720;
        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;

        camera.targetTexture = renderTexture;
        camera.Render();
        RenderTexture.active = renderTexture;

        Texture2D preview = new Texture2D(width, height, TextureFormat.RGB24, false);
        preview.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        preview.Apply();

        string previewPath = GeneratedFolder + "/WildernessPreview.png";
        System.IO.File.WriteAllBytes(previewPath, preview.EncodeToPNG());

        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        RenderTexture.ReleaseTemporary(renderTexture);
        Object.DestroyImmediate(preview);

        AssetDatabase.ImportAsset(previewPath, ImportAssetOptions.ForceUpdate);
        Debug.Log("Wilderness preview saved to " + previewPath);
    }

    private static TerrainData CreateTerrainData()
    {
        const int resolution = 257;
        const float worldSize = 160f;
        TerrainData data = new TerrainData
        {
            heightmapResolution = resolution,
            alphamapResolution = 256,
            baseMapResolution = 512,
            size = new Vector3(worldSize, 26f, worldSize)
        };

        float[,] heights = new float[resolution, resolution];
        for (int z = 0; z < resolution; z++)
        {
            float nz = (z / (resolution - 1f) - 0.5f) * 2f;
            for (int x = 0; x < resolution; x++)
            {
                float nx = (x / (resolution - 1f) - 0.5f) * 2f;
                float squareRadius = Mathf.Max(Mathf.Abs(nx), Mathf.Abs(nz));
                float radial = Mathf.Sqrt(nx * nx + nz * nz);

                float edge = Mathf.Pow(Mathf.SmoothStep(0.14f, 1f, squareRadius), 1.5f);
                float basin = 1f - Mathf.SmoothStep(0.06f, 0.48f, radial);
                float broadNoise = FractalNoise(nx * 2.15f + 12.7f, nz * 2.15f + 4.2f);
                float detailNoise = Mathf.PerlinNoise(nx * 10.5f + 31.4f, nz * 10.5f + 18.6f) - 0.5f;

                float height = 0.155f + edge * 0.47f - basin * 0.052f;
                height += broadNoise * 0.105f * (1f - basin * 0.82f);
                height += detailNoise * 0.026f * (0.35f + edge * 0.65f);

                height += Hill(nx, nz, -0.72f, 0.52f, 0.24f, 0.12f);
                height += Hill(nx, nz, 0.67f, 0.58f, 0.28f, 0.10f);
                height += Hill(nx, nz, -0.61f, -0.70f, 0.26f, 0.08f);
                height += Hill(nx, nz, 0.74f, -0.60f, 0.22f, 0.11f);

                // Keep the central play area gently uneven while preserving the surrounding basin.
                float playArea = 1f - Mathf.SmoothStep(0.18f, 0.34f, radial);
                float centerHeight = 0.105f + detailNoise * 0.005f;
                height = Mathf.Lerp(height, centerHeight, playArea * 0.78f);
                heights[z, x] = Mathf.Clamp01(height);
            }
        }

        data.SetHeights(0, 0, heights);
        return data;
    }

    private static float FractalNoise(float x, float z)
    {
        float first = Mathf.PerlinNoise(x, z) - 0.5f;
        float second = (Mathf.PerlinNoise(x * 2.15f + 8.3f, z * 2.15f + 3.7f) - 0.5f) * 0.48f;
        float third = (Mathf.PerlinNoise(x * 4.8f + 19.1f, z * 4.8f + 11.2f) - 0.5f) * 0.2f;
        return first + second + third;
    }

    private static float Hill(float x, float z, float centerX, float centerZ, float radius, float amplitude)
    {
        float dx = x - centerX;
        float dz = z - centerZ;
        float distanceSquared = dx * dx + dz * dz;
        return Mathf.Exp(-distanceSquared / (radius * radius)) * amplitude;
    }

    private static TerrainLayer CreateTerrainLayer(
        string layerName,
        string albedoPath,
        string normalPath,
        Vector2 tileSize,
        float smoothness)
    {
        return new TerrainLayer
        {
            name = layerName,
            diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath),
            normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath),
            tileSize = tileSize,
            normalScale = 0.8f,
            metallic = 0f,
            smoothness = smoothness
        };
    }

    private static void PaintTerrain(TerrainData data)
    {
        int resolution = data.alphamapResolution;
        float[,,] maps = new float[resolution, resolution, 2];

        for (int z = 0; z < resolution; z++)
        {
            float v = z / (resolution - 1f);
            for (int x = 0; x < resolution; x++)
            {
                float u = x / (resolution - 1f);
                float nx = (u - 0.5f) * 2f;
                float nz = (v - 0.5f) * 2f;
                float radial = Mathf.Sqrt(nx * nx + nz * nz);
                float normalizedHeight = data.GetInterpolatedHeight(u, v) / data.size.y;
                float slope = data.GetSteepness(u, v);
                float breakup = Mathf.PerlinNoise(u * 8.4f + 5.3f, v * 8.4f + 9.7f);

                float highRock = Mathf.InverseLerp(0.34f, 0.63f, normalizedHeight) * 0.62f;
                float steepRock = Mathf.InverseLerp(17f, 38f, slope) * 0.72f;
                float centerDirt = 1f - Mathf.SmoothStep(0.24f, 0.44f, radial);
                float stone = Mathf.Clamp01(highRock + steepRock + breakup * 0.12f - centerDirt * 0.72f);
                maps[z, x, 0] = 1f - stone;
                maps[z, x, 1] = stone;
            }
        }

        data.SetAlphamaps(0, 0, maps);
    }

    private static void CreateLighting(Transform parent)
    {
        GameObject lightObject = new GameObject("Sun");
        lightObject.transform.SetParent(parent);
        lightObject.transform.rotation = Quaternion.Euler(43f, -32f, 0f);

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(0.82f, 0.76f, 0.65f);
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;
    }

    private static void CreatePreviewCamera(Terrain terrain)
    {
        float ground = terrain.terrainData.GetInterpolatedHeight(0.5f, 0.24f) + terrain.transform.position.y;
        float center = terrain.terrainData.GetInterpolatedHeight(0.5f, 0.5f) + terrain.transform.position.y;

        GameObject cameraObject = new GameObject("Preview Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0f, ground + 6.2f, -42f);
        cameraObject.transform.LookAt(new Vector3(0f, center + 2.2f, 10f));

        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.35f, 0.38f, 0.35f);
        camera.fieldOfView = 66f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 220f;
        cameraObject.AddComponent<AudioListener>();
    }

    private static void CreatePlayerSpawn(Terrain terrain)
    {
        float center = terrain.terrainData.GetInterpolatedHeight(0.5f, 0.5f) + terrain.transform.position.y;
        GameObject playerSpawn = new GameObject("PlayerSpawn");
        playerSpawn.transform.position = new Vector3(0f, center, 0f);
    }

    private static void ConfigureAtmosphere()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.36f, 0.39f, 0.36f);
        RenderSettings.fogStartDistance = 42f;
        RenderSettings.fogEndDistance = 150f;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.30f, 0.32f, 0.29f);
        RenderSettings.reflectionIntensity = 0.45f;
    }

    private static void ConfigureNormalMap(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.textureType == TextureImporterType.NormalMap)
        {
            return;
        }

        importer.textureType = TextureImporterType.NormalMap;
        importer.SaveAndReimport();
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

    private static void DeleteGeneratedAsset(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
        }
    }
}
