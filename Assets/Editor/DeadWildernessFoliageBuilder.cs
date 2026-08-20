using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class DeadWildernessFoliageBuilder
{
    private const string RootName = "Wilderness Vegetation";
    private const string GeneratedFolder = "Assets/Art/Foliage/Generated";
    private const string GrassMeshPath = GeneratedFolder + "/DryGrassClusters.asset";
    private const string TreeMeshPath = GeneratedFolder + "/DeadTrees.asset";
    private const string GrassMaterialPath = GeneratedFolder + "/DryGrass.mat";
    private const string TreeMaterialPath = GeneratedFolder + "/DeadWood.mat";

    [MenuItem("Tools/Environment/Add Random Dead Vegetation")]
    public static void AddVegetation()
    {
        Terrain terrain = Object.FindFirstObjectByType<Terrain>();
        if (terrain == null)
        {
            EditorUtility.DisplayDialog("No Terrain Found", "Create or select a Terrain before adding wilderness vegetation.", "OK");
            return;
        }

        EnsureFolder("Assets/Art");
        EnsureFolder("Assets/Art/Foliage");
        EnsureFolder(GeneratedFolder);
        RemovePreviousGeneratedVegetation();
        DeleteGeneratedAsset(GrassMeshPath);
        DeleteGeneratedAsset(TreeMeshPath);
        DeleteGeneratedAsset(GrassMaterialPath);
        DeleteGeneratedAsset(TreeMaterialPath);

        Random.State previousState = Random.state;
        Random.InitState(98437);

        GameObject root = new GameObject(RootName);
        root.isStatic = true;
        CreateGrass(root.transform, terrain);
        CreateTrees(root.transform, terrain);

        Random.state = previousState;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeGameObject = root;
        Debug.Log("Random dry grass and dead trees added without modifying the terrain.");
    }

    private static void CreateGrass(Transform parent, Terrain terrain)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> colors = new List<Color>();
        const int clumpCount = 340;

        for (int i = 0; i < clumpCount; i++)
        {
            if (!TryFindGroundPoint(terrain, 0.16f, 28f, out Vector3 point, out _))
            {
                continue;
            }

            int blades = Random.Range(3, 6);
            for (int blade = 0; blade < blades; blade++)
            {
                Vector3 offset = new Vector3(Random.Range(-0.24f, 0.24f), 0f, Random.Range(-0.24f, 0.24f));
                float height = Random.Range(0.28f, 0.82f);
                float width = Random.Range(0.018f, 0.055f);
                float yaw = Random.Range(0f, 180f);
                Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                Vector3 lean = Quaternion.Euler(0f, yaw + Random.Range(-45f, 45f), 0f) * Vector3.right * Random.Range(0.04f, 0.18f);
                Color color = Color.Lerp(new Color(0.28f, 0.22f, 0.12f), new Color(0.56f, 0.43f, 0.22f), Random.value);
                AddDoubleSidedBlade(vertices, triangles, colors, point + offset, direction, lean, height, width, color);
            }
        }

        Mesh mesh = CreateMesh("Dry Grass Clusters", vertices, triangles, colors);
        AssetDatabase.CreateAsset(mesh, GrassMeshPath);
        Material material = CreateMaterial("Dry Grass", new Color(0.55f, 0.43f, 0.23f));
        AssetDatabase.CreateAsset(material, GrassMaterialPath);
        CreateMeshObject("Dry Grass", parent, mesh, material);
    }

    private static void CreateTrees(Transform parent, Terrain terrain)
    {
        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();
        List<Color> colors = new List<Color>();
        const int treeCount = 18;

        for (int i = 0; i < treeCount; i++)
        {
            if (!TryFindGroundPoint(terrain, 0.31f, 24f, out Vector3 root, out _))
            {
                continue;
            }

            float height = Random.Range(4.2f, 8.8f);
            float baseRadius = height * Random.Range(0.035f, 0.055f);
            Vector3 lean = new Vector3(Random.Range(-0.12f, 0.12f), 0f, Random.Range(-0.12f, 0.12f)) * height;
            Vector3 trunkTop = root + Vector3.up * height + lean;
            Color wood = Color.Lerp(new Color(0.13f, 0.105f, 0.08f), new Color(0.27f, 0.20f, 0.14f), Random.value);

            AddBranch(vertices, triangles, colors, root, trunkTop, baseRadius, baseRadius * 0.28f, wood);
            int branchCount = Random.Range(4, 7);
            for (int branch = 0; branch < branchCount; branch++)
            {
                float t = Random.Range(0.38f, 0.88f);
                Vector3 start = Vector3.Lerp(root, trunkTop, t);
                float yaw = Random.Range(0f, 360f);
                float length = height * Random.Range(0.22f, 0.40f) * (1f - t * 0.25f);
                Vector3 horizontal = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
                Vector3 end = start + horizontal * length + Vector3.up * Random.Range(0.18f, 0.54f) * length;
                float radius = baseRadius * Mathf.Lerp(0.42f, 0.18f, t);
                AddBranch(vertices, triangles, colors, start, end, radius, radius * 0.3f, wood);

                if (Random.value > 0.28f)
                {
                    Vector3 twigStart = Vector3.Lerp(start, end, Random.Range(0.45f, 0.8f));
                    Vector3 twigDirection = (Quaternion.Euler(0f, yaw + Random.Range(35f, 95f), 0f) * Vector3.right) * (length * Random.Range(0.28f, 0.48f));
                    Vector3 twigEnd = twigStart + twigDirection + Vector3.up * length * Random.Range(0.16f, 0.34f);
                    AddBranch(vertices, triangles, colors, twigStart, twigEnd, radius * 0.42f, radius * 0.1f, wood);
                }
            }
        }

        Mesh mesh = CreateMesh("Dead Trees", vertices, triangles, colors);
        AssetDatabase.CreateAsset(mesh, TreeMeshPath);
        Material material = CreateMaterial("Dead Wood", new Color(0.19f, 0.14f, 0.10f));
        AssetDatabase.CreateAsset(material, TreeMaterialPath);
        CreateMeshObject("Dead Trees", parent, mesh, material);
    }

    private static bool TryFindGroundPoint(Terrain terrain, float safeRadius, float maxSlope, out Vector3 point, out float slope)
    {
        for (int attempt = 0; attempt < 80; attempt++)
        {
            float u = Random.Range(0.04f, 0.96f);
            float v = Random.Range(0.04f, 0.96f);
            Vector2 centered = new Vector2(u - 0.5f, v - 0.5f) * 2f;
            if (centered.magnitude < safeRadius)
            {
                continue;
            }

            slope = terrain.terrainData.GetSteepness(u, v);
            if (slope > maxSlope)
            {
                continue;
            }

            Vector3 terrainPosition = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            float y = terrain.terrainData.GetInterpolatedHeight(u, v) + terrainPosition.y;
            point = new Vector3(terrainPosition.x + u * terrainSize.x, y, terrainPosition.z + v * terrainSize.z);
            return true;
        }

        point = default;
        slope = 0f;
        return false;
    }

    private static void AddDoubleSidedBlade(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 basePoint,
        Vector3 direction,
        Vector3 lean,
        float height,
        float width,
        Color color)
    {
        Vector3 halfWidth = direction.normalized * width;
        Vector3 top = basePoint + Vector3.up * height + lean;
        int start = vertices.Count;
        vertices.Add(basePoint - halfWidth);
        vertices.Add(basePoint + halfWidth);
        vertices.Add(top + halfWidth * 0.22f);
        vertices.Add(top - halfWidth * 0.22f);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color * 1.08f);
        colors.Add(color * 1.08f);
        AddDoubleSidedQuad(triangles, start);
    }

    private static void AddBranch(
        List<Vector3> vertices,
        List<int> triangles,
        List<Color> colors,
        Vector3 start,
        Vector3 end,
        float startRadius,
        float endRadius,
        Color color)
    {
        const int sides = 5;
        Vector3 axis = (end - start).normalized;
        Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.94f ? Vector3.right : Vector3.up;
        Vector3 right = Vector3.Cross(axis, reference).normalized;
        Vector3 forward = Vector3.Cross(right, axis).normalized;
        int first = vertices.Count;

        for (int ring = 0; ring < 2; ring++)
        {
            Vector3 center = ring == 0 ? start : end;
            float radius = ring == 0 ? startRadius : endRadius;
            for (int side = 0; side < sides; side++)
            {
                float angle = side / (float)sides * Mathf.PI * 2f;
                Vector3 point = center + (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)) * radius;
                vertices.Add(point);
                colors.Add(color * Random.Range(0.88f, 1.08f));
            }
        }

        for (int side = 0; side < sides; side++)
        {
            int next = (side + 1) % sides;
            triangles.Add(first + side);
            triangles.Add(first + sides + side);
            triangles.Add(first + sides + next);
            triangles.Add(first + side);
            triangles.Add(first + sides + next);
            triangles.Add(first + next);
        }
    }

    private static void AddDoubleSidedQuad(List<int> triangles, int start)
    {
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
        triangles.Add(start + 2);
        triangles.Add(start + 1);
        triangles.Add(start);
        triangles.Add(start + 3);
        triangles.Add(start + 2);
        triangles.Add(start);
    }

    private static Mesh CreateMesh(string meshName, List<Vector3> vertices, List<int> triangles, List<Color> colors)
    {
        Mesh mesh = new Mesh { name = meshName, indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetColors(colors);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Material CreateMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        Material material = new Material(shader) { name = materialName };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0f);
        return material;
    }

    private static void CreateMeshObject(string objectName, Transform parent, Mesh mesh, Material material)
    {
        GameObject gameObject = new GameObject(objectName);
        gameObject.transform.SetParent(parent);
        gameObject.isStatic = true;
        MeshFilter filter = gameObject.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;
    }

    private static void RemovePreviousGeneratedVegetation()
    {
        GameObject existing = GameObject.Find(RootName);
        if (existing != null)
        {
            Object.DestroyImmediate(existing);
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

    private static void DeleteGeneratedAsset(string path)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
        }
    }
}
