using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class MesaTerrainBuilder
{
    private const string ScenePath = "Assets/Scenes/MesaTerrainScene.unity";
    private const string GeneratedFolder = "Assets/Art/Terrain/MesaGenerated";
    private const string TerrainDataPath = GeneratedFolder + "/MesaTerrain.asset";
    private const string GrassLayerPath = GeneratedFolder + "/MesaGrass.terrainlayer";
    private const string DirtLayerPath = GeneratedFolder + "/MesaDirtTerra.terrainlayer";
    private const string RockLayerPath = GeneratedFolder + "/MesaRock.terrainlayer";
    private const string BoulderMeshPath = GeneratedFolder + "/MesaBoulder.asset";
    private const string FrontCliffMaterialPath = "Assets/Art/Terrain/Generated/FrontCliff.mat";
    private const string DirtAlbedoPath = "Assets/Art/Terrain/Source/Dirt_Albedo.jpg";
    private const string DirtNormalPath = "Assets/Art/Terrain/Source/Dirt_Normal.jpg";
    private const string StoneAlbedoPath = "Assets/Art/Terrain/Source/Stone_Albedo.png";
    private const string StoneNormalPath = "Assets/Art/Terrain/Source/Stone_Normal.png";

    [MenuItem("Tools/Environment/Build Mesa Terrain")]
    public static void Build()
    {
        EnsureFolder(GeneratedFolder);
        EditorSceneManager.SaveOpenScenes();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        DeleteAsset(TerrainDataPath);
        DeleteAsset(GrassLayerPath);
        DeleteAsset(DirtLayerPath);
        DeleteAsset(RockLayerPath);
        DeleteAsset(BoulderMeshPath);

        TerrainData terrainData = CreateTerrainData();
        AssetDatabase.CreateAsset(terrainData, TerrainDataPath);

        TerrainLayer grassLayer = CreateTerrainLayer(
            "Mesa Grass",
            DirtAlbedoPath,
            DirtNormalPath,
            new Vector2(15f, 15f),
            0.18f);
        // Keep this as an understated ground surface, not a visible grass-card texture.
        grassLayer.diffuseRemapMin = new Color(0.055f, 0.12f, 0.025f, 1f);
        grassLayer.diffuseRemapMax = new Color(0.39f, 0.57f, 0.16f, 1f);

        TerrainLayer dirtTerraLayer = CreateTerrainLayer(
            "Mesa DirtTerra",
            DirtAlbedoPath,
            DirtNormalPath,
            new Vector2(12f, 12f),
            0.20f);
        dirtTerraLayer.diffuseRemapMin = new Color(0.10f, 0.075f, 0.032f, 1f);
        dirtTerraLayer.diffuseRemapMax = new Color(0.46f, 0.34f, 0.16f, 1f);

        TerrainLayer rockLayer = CreateTerrainLayer(
            "Mesa Rock",
            StoneAlbedoPath,
            StoneNormalPath,
            new Vector2(8f, 8f),
            0.12f);
        rockLayer.diffuseRemapMin = new Color(0.16f, 0.17f, 0.16f, 1f);
        rockLayer.diffuseRemapMax = new Color(0.66f, 0.65f, 0.58f, 1f);

        AssetDatabase.CreateAsset(grassLayer, GrassLayerPath);
        AssetDatabase.CreateAsset(dirtTerraLayer, DirtLayerPath);
        AssetDatabase.CreateAsset(rockLayer, RockLayerPath);
        terrainData.terrainLayers = new[] { grassLayer, dirtTerraLayer, rockLayer };
        PaintTerrain(terrainData);

        GameObject environment = new GameObject("Mesa Environment");
        GameObject terrainObject = Terrain.CreateTerrainGameObject(terrainData);
        terrainObject.name = "Mesa Terrain";
        terrainObject.transform.SetParent(environment.transform, false);
        terrainObject.transform.position = new Vector3(-90f, -3f, -90f);

        Terrain terrain = terrainObject.GetComponent<Terrain>();
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = 3f;
        terrain.basemapDistance = 240f;
        terrain.shadowCastingMode = ShadowCastingMode.On;

        Mesh boulderMesh = BuildBoulderMesh();
        AssetDatabase.CreateAsset(boulderMesh, BoulderMeshPath);
        Material boulderMaterial = LoadFrontCliffMaterial();
        CreateRimBoulders(environment.transform, terrain, boulderMesh, boulderMaterial);
        CreateLighting(environment.transform);
        CreatePreviewCamera(environment.transform);
        ConfigureAtmosphere();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddToBuildSettings(ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Selection.activeGameObject = terrainObject;
        SceneView.lastActiveSceneView?.FrameSelected();
        Debug.Log("Built the reference-style mesa terrain in " + ScenePath);
    }

    private static TerrainData CreateTerrainData()
    {
        const int resolution = 257;
        const float worldSize = 180f;
        TerrainData data = new TerrainData
        {
            name = "Mesa Terrain",
            heightmapResolution = resolution,
            alphamapResolution = 256,
            baseMapResolution = 512,
            size = new Vector3(worldSize, 72f, worldSize)
        };

        float[,] heights = new float[resolution, resolution];
        for (int z = 0; z < resolution; z++)
        {
            float nz = (z / (resolution - 1f) - 0.5f) * 2f;
            for (int x = 0; x < resolution; x++)
            {
                float nx = (x / (resolution - 1f) - 0.5f) * 2f;
                float radial = Mathf.Sqrt((nx * 0.95f) * (nx * 0.95f) + (nz * 0.88f) * (nz * 0.88f));
                float angle = Mathf.Atan2(nz, nx);
                float shapeNoise = FractalNoise(nx * 1.35f + 4.6f, nz * 1.35f + 9.2f) * 0.075f;
                shapeNoise += Mathf.Sin(angle * 3f + 0.7f) * 0.026f;
                float distance = radial + shapeNoise;

                float topMask = 1f - SmoothThreshold(0.44f, 0.51f, distance);
                float upperShelf = SmoothThreshold(0.50f, 0.535f, distance)
                    * (1f - SmoothThreshold(0.575f, 0.625f, distance));
                float lowerShelf = SmoothThreshold(0.61f, 0.665f, distance)
                    * (1f - SmoothThreshold(0.73f, 0.81f, distance));
                float outerSlope = SmoothThreshold(0.68f, 1.04f, distance);
                float cliffBand = SmoothThreshold(0.43f, 0.50f, distance)
                    * (1f - SmoothThreshold(0.79f, 0.94f, distance));

                float secondaryDistance = Mathf.Sqrt(
                    ((nx + 0.47f) * 1.22f) * ((nx + 0.47f) * 1.22f)
                    + ((nz + 0.18f) * 1.05f) * ((nz + 0.18f) * 1.05f));
                float secondaryShelf = 1f - SmoothThreshold(0.23f, 0.34f, secondaryDistance);

                float broadNoise = FractalNoise(nx * 2.8f + 15.2f, nz * 2.8f + 3.4f);
                float detailNoise = Mathf.PerlinNoise(nx * 13f + 21.5f, nz * 13f + 6.8f) - 0.5f;
                float verticalStriation = Mathf.Sin((nx * 0.94f + nz * 0.24f) * Mathf.PI * 13f
                    + Mathf.PerlinNoise(nx * 4.2f + 2f, nz * 4.2f + 8f) * 2.6f);

                float height = 0.055f
                    + topMask * (0.79f + broadNoise * 0.026f)
                    + upperShelf * (0.27f + broadNoise * 0.018f)
                    + lowerShelf * (0.13f + broadNoise * 0.012f)
                    + outerSlope * 0.065f;
                height = Mathf.Max(height, 0.055f + secondaryShelf * (0.34f + broadNoise * 0.018f));
                height += cliffBand * (verticalStriation * 0.014f + detailNoise * 0.04f);
                height += detailNoise * 0.012f * (topMask + outerSlope * 0.4f);
                heights[z, x] = Mathf.Clamp01(height);
            }
        }

        data.SetHeights(0, 0, heights);
        return data;
    }

    private static void PaintTerrain(TerrainData data)
    {
        int resolution = data.alphamapResolution;
        float[,,] maps = new float[resolution, resolution, 3];

        for (int z = 0; z < resolution; z++)
        {
            float v = z / (resolution - 1f);
            for (int x = 0; x < resolution; x++)
            {
                float u = x / (resolution - 1f);
                float nx = (u - 0.5f) * 2f;
                float nz = (v - 0.5f) * 2f;
                float radial = Mathf.Sqrt((nx * 0.95f) * (nx * 0.95f) + (nz * 0.88f) * (nz * 0.88f));
                float slope = data.GetSteepness(u, v);
                float topGrass = 1f - SmoothThreshold(0.43f, 0.61f, radial);
                float cliffBand = SmoothThreshold(0.42f, 0.56f, radial)
                    * (1f - SmoothThreshold(0.84f, 1f, radial));
                float steepRock = Mathf.InverseLerp(18f, 47f, slope);
                float breakup = Mathf.PerlinNoise(u * 10.4f + 4.1f, v * 10.4f + 8.6f);
                float rock = Mathf.Clamp01(steepRock * 0.92f + cliffBand * 0.50f + breakup * 0.08f - topGrass * 0.66f);
                float dirtTerra = Mathf.Clamp01(SmoothThreshold(0.53f, 0.9f, radial) * 0.72f
                    + Mathf.InverseLerp(9f, 25f, slope) * 0.25f
                    + (breakup - 0.55f) * 0.16f);
                dirtTerra *= 1f - rock;
                float grass = Mathf.Clamp01(1f - rock - dirtTerra);
                maps[z, x, 0] = grass;
                maps[z, x, 1] = dirtTerra;
                maps[z, x, 2] = rock;
            }
        }

        data.SetAlphamaps(0, 0, maps);
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
            normalScale = 0.75f,
            metallic = 0f,
            smoothness = smoothness
        };
    }

    private static Mesh BuildBoulderMesh()
    {
        const int sides = 8;
        float[] heights = { -0.5f, -0.15f, 0.35f };
        float[] radii = { 0.72f, 1f, 0.55f };
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        for (int ring = 0; ring < heights.Length; ring++)
        {
            for (int side = 0; side < sides; side++)
            {
                float angle = side / (float)sides * Mathf.PI * 2f;
                float irregularity = 0.84f + Mathf.PerlinNoise(side * 1.7f + ring * 2.4f, 4.2f) * 0.26f;
                vertices.Add(new Vector3(
                    Mathf.Cos(angle) * radii[ring] * irregularity,
                    heights[ring],
                    Mathf.Sin(angle) * radii[ring] * irregularity));
            }
        }

        int bottom = vertices.Count;
        vertices.Add(new Vector3(0f, -0.6f, 0f));
        int top = vertices.Count;
        vertices.Add(new Vector3(0f, 0.55f, 0f));

        for (int ring = 0; ring < heights.Length - 1; ring++)
        {
            for (int side = 0; side < sides; side++)
            {
                int next = (side + 1) % sides;
                int a = ring * sides + side;
                int b = ring * sides + next;
                int c = (ring + 1) * sides + side;
                int d = (ring + 1) * sides + next;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
        }

        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            int topRing = (heights.Length - 1) * sides;
            triangles.Add(bottom); triangles.Add(next); triangles.Add(side);
            triangles.Add(top); triangles.Add(topRing + side); triangles.Add(topRing + next);
        }

        Mesh mesh = new Mesh { name = "Mesa Boulder" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void CreateRimBoulders(Transform parent, Terrain terrain, Mesh mesh, Material material)
    {
        const int rockCount = 42;
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        var random = new System.Random(9182);
        GameObject root = new GameObject("Mesa Rim Boulders");
        root.transform.SetParent(parent, false);

        for (int index = 0; index < rockCount; index++)
        {
            float angle = (float)random.NextDouble() * Mathf.PI * 2f;
            float radius = 0.49f + (float)random.NextDouble() * 0.21f;
            float nx = Mathf.Cos(angle) * radius;
            float nz = Mathf.Sin(angle) * radius;
            float u = Mathf.Clamp01(nx * 0.5f + 0.5f);
            float v = Mathf.Clamp01(nz * 0.5f + 0.5f);
            float height = data.GetInterpolatedHeight(u, v) + origin.y;

            GameObject rock = new GameObject("Mesa Boulder " + (index + 1).ToString("00"));
            rock.transform.SetParent(root.transform, false);
            rock.transform.position = new Vector3(
                origin.x + (u * data.size.x),
                height + 0.15f,
                origin.z + (v * data.size.z));
            rock.transform.rotation = Quaternion.Euler(
                (float)random.NextDouble() * 20f - 10f,
                (float)random.NextDouble() * 360f,
                (float)random.NextDouble() * 20f - 10f);
            float scale = 1.3f + (float)random.NextDouble() * 2.7f;
            rock.transform.localScale = new Vector3(
                scale * (0.75f + (float)random.NextDouble() * 0.35f),
                scale * (0.55f + (float)random.NextDouble() * 0.3f),
                scale * (0.75f + (float)random.NextDouble() * 0.35f));
            rock.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = rock.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
    }

    private static Material LoadFrontCliffMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(FrontCliffMaterialPath);
        if (material == null)
        {
            throw new InvalidOperationException("FrontCliff.mat must exist before building the mesa terrain.");
        }

        return material;
    }

    private static void CreateLighting(Transform parent)
    {
        GameObject sunObject = new GameObject("Mesa Sun");
        sunObject.transform.SetParent(parent, false);
        sunObject.transform.rotation = Quaternion.Euler(46f, -34f, 0f);
        Light sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.86f, 0.65f);
        sun.intensity = 1.25f;
        sun.shadows = LightShadows.Soft;
    }

    private static void CreatePreviewCamera(Transform parent)
    {
        GameObject cameraObject = new GameObject("Mesa Preview Camera");
        cameraObject.transform.SetParent(parent, false);
        cameraObject.transform.position = new Vector3(122f, 78f, -150f);
        cameraObject.transform.LookAt(new Vector3(0f, 25f, 6f));
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.17f, 0.21f, 0.18f);
        camera.fieldOfView = 44f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 300f;
        cameraObject.AddComponent<AudioListener>();
    }

    private static void ConfigureAtmosphere()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.20f, 0.24f, 0.20f);
        RenderSettings.fogStartDistance = 95f;
        RenderSettings.fogEndDistance = 260f;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.34f, 0.37f, 0.32f);
        RenderSettings.reflectionIntensity = 0.35f;
    }

    private static float FractalNoise(float x, float z)
    {
        float first = Mathf.PerlinNoise(x, z) - 0.5f;
        float second = (Mathf.PerlinNoise(x * 2.1f + 7.2f, z * 2.1f + 4.3f) - 0.5f) * 0.42f;
        float third = (Mathf.PerlinNoise(x * 4.6f + 18.3f, z * 4.6f + 9.1f) - 0.5f) * 0.18f;
        return first + second + third;
    }

    private static float SmoothThreshold(float edge0, float edge1, float value)
    {
        return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(edge0, edge1, value));
    }

    private static void AddToBuildSettings(string scenePath)
    {
        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.All(scene => scene.path != scenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
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

    private static void DeleteAsset(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
        }
    }
}
