using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class FrontCliffBuilder
{
    private const string GeneratedFolder = "Assets/Art/Terrain/Generated";
    private const string CliffMeshPath = GeneratedFolder + "/FrontCliffMesh.asset";
    private const string RockMeshPath = GeneratedFolder + "/FrontCliffRock.asset";
    private const string MaterialPath = GeneratedFolder + "/FrontCliff.mat";
    // The existing tileable stone map reads better on a large wall than the
    // Ange preview sphere, whose black background would tile into dark circles.
    private const string StoneAlbedoPath = "Assets/Art/Terrain/Source/Stone_Albedo.png";
    private const string StoneNormalPath = "Assets/Art/Terrain/Source/Stone_Normal.png";
    private const string CliffRootName = "Front Cliff Escarpment";
    private static readonly float[] TierBreaks = { 0f, 0.12f, 0.26f, 0.4f, 0.58f, 0.73f, 0.88f, 1f };
    private static readonly float[] TierOffsets = { 0.25f, 1.2f, 4.8f, 2.2f, 6.6f, 3.4f, 7.4f, 2.6f };

    [MenuItem("Tools/Environment/Build Front Cliff")]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        Terrain terrain = Resources.FindObjectsOfTypeAll<Terrain>()
            .FirstOrDefault(candidate => candidate.gameObject.scene == scene
                && candidate.name == "Wilderness Terrain");
        if (terrain == null)
        {
            Debug.LogError("Wilderness Terrain was not found in the active scene.");
            return;
        }

        EnsureFolder(GeneratedFolder);
        Transform environment = Resources.FindObjectsOfTypeAll<Transform>()
            .FirstOrDefault(candidate => candidate.gameObject.scene == scene
                && candidate.name == "Environment");
        if (environment == null)
        {
            environment = terrain.transform.parent;
        }

        GameObject previous = Resources.FindObjectsOfTypeAll<GameObject>()
            .FirstOrDefault(candidate => candidate.scene == scene
                && candidate.name == CliffRootName);
        if (previous != null)
        {
            UnityEngine.Object.DestroyImmediate(previous);
        }

        TerrainData data = terrain.terrainData;
        // The requested green-circled edge is the terrain's +X boundary.
        // Keep this explicit so rebuilding from a different Scene View angle
        // cannot silently place the escarpment on another side.
        bool boundaryOnX = true;
        float sideSign = 1f;

        Mesh cliffMesh = BuildCliffMesh(terrain, boundaryOnX, sideSign);
        Mesh rockMesh = BuildRockMesh();
        ReplaceAsset(cliffMesh, CliffMeshPath);
        ReplaceAsset(rockMesh, RockMeshPath);
        Material cliffMaterial = CreateOrUpdateMaterial();

        GameObject root = new GameObject(CliffRootName);
        root.transform.SetParent(environment, true);

        GameObject wall = new GameObject("Continuous Cliff Wall");
        wall.transform.SetParent(root.transform, false);
        MeshFilter meshFilter = wall.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(CliffMeshPath);
        MeshRenderer meshRenderer = wall.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = cliffMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.On;
        meshRenderer.receiveShadows = true;
        MeshCollider collider = wall.AddComponent<MeshCollider>();
        collider.sharedMesh = meshFilter.sharedMesh;

        Mesh sharedRockMesh = AssetDatabase.LoadAssetAtPath<Mesh>(RockMeshPath);
        CreateTopRubble(root.transform, terrain, boundaryOnX, sideSign, sharedRockMesh, cliffMaterial);
        CreateFaceOutcrops(root.transform, terrain, boundaryOnX, sideSign, sharedRockMesh, cliffMaterial);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();

        string boundary = boundaryOnX
            ? (sideSign > 0f ? "+X" : "-X")
            : (sideSign > 0f ? "+Z" : "-Z");
        Debug.Log("Built the front cliff along the " + boundary + " terrain boundary.");
    }

    private static Mesh BuildCliffMesh(Terrain terrain, bool boundaryOnX, float sideSign)
    {
        const int horizontalSegments = 72;
        const int verticalSegments = 14;
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        Vector3 along = boundaryOnX ? Vector3.forward : Vector3.right;
        Vector3 outward = boundaryOnX
            ? Vector3.right * sideSign
            : Vector3.forward * sideSign;
        float length = boundaryOnX ? data.size.z : data.size.x;
        float boundaryCoordinate = boundaryOnX
            ? origin.x + (sideSign > 0f ? data.size.x : 0f)
            : origin.z + (sideSign > 0f ? data.size.z : 0f);
        float bottomHeight = origin.y - 42f;

        // Keep one vertex per grid intersection so lighting flows across the wall.
        var vertices = new List<Vector3>((horizontalSegments + 1) * (verticalSegments + 1));
        var uvs = new List<Vector2>(vertices.Capacity);

        for (int row = 0; row <= verticalSegments; row++)
        {
            float t = row / (float)verticalSegments;
            for (int column = 0; column <= horizontalSegments; column++)
            {
                float along01 = column / (float)horizontalSegments;
                float sampleU = boundaryOnX ? (sideSign > 0f ? 1f : 0f) : along01;
                float sampleV = boundaryOnX ? along01 : (sideSign > 0f ? 1f : 0f);
                float topHeight = data.GetInterpolatedHeight(sampleU, sampleV) + origin.y;

                float broadNoise = Mathf.PerlinNoise(along01 * 3.7f + 4.7f, 8.2f) - 0.5f;
                float striation = Mathf.PerlinNoise(along01 * 10.5f + 2.6f, t * 2.6f + 5.1f) - 0.5f;
                float fineNoise = Mathf.PerlinNoise(along01 * 27.7f + 2.6f, t * 11.4f + 5.1f) - 0.5f;
                float interiorWeight = Mathf.Sin(t * Mathf.PI);
                // These terms only vary along the cliff length, so the major
                // fractures run vertically from the rim down to the foot.
                float verticalFracture = Mathf.PerlinNoise(along01 * 12.5f + 1.4f, 3.7f) - 0.5f;
                float verticalRidge = Mathf.Sin(along01 * Mathf.PI * 7.2f + broadNoise * 2.2f);
                // A few uneven terraces make the vertical drop connect into
                // the terrain instead of reading as one straight slab.
                float tierVariation = Mathf.Clamp(0.86f + verticalFracture * 0.68f + verticalRidge * 0.12f, 0.62f, 1.12f);
                float outwardDistance = EvaluateTierProfile(t) * tierVariation
                    + verticalFracture * 2.6f * interiorWeight
                    + verticalRidge * 1.1f * interiorWeight
                    + striation * 0.45f * interiorWeight
                    + fineNoise * 0.35f * interiorWeight;
                float ledge = verticalRidge * 1.25f * interiorWeight;
                float height = Mathf.Lerp(topHeight, bottomHeight, t)
                    + ledge
                    + striation * 2.4f * interiorWeight
                    + fineNoise * 1.15f * interiorWeight;
                float alongJitter = (broadNoise * 2.2f + fineNoise * 0.8f) * interiorWeight;

                Vector3 point = along * (length * along01 + alongJitter)
                    + outward * outwardDistance;
                if (boundaryOnX)
                {
                    point.x += boundaryCoordinate;
                    point.z += origin.z;
                }
                else
                {
                    point.x += origin.x;
                    point.z += boundaryCoordinate;
                }
                point.y = height;
                vertices.Add(point);
                uvs.Add(new Vector2(along01 * (length / 7.5f), t * 5.6f));
            }
        }

        int stride = horizontalSegments + 1;
        var triangles = new List<int>(horizontalSegments * verticalSegments * 6);
        Vector3 sampleNormal = Vector3.Cross(vertices[stride] - vertices[0], vertices[1] - vertices[0]);
        bool flipWinding = Vector3.Dot(sampleNormal, outward) < 0f;
        for (int row = 0; row < verticalSegments; row++)
        {
            for (int column = 0; column < horizontalSegments; column++)
            {
                int gridA = row * stride + column;
                int gridB = gridA + 1;
                int gridC = gridA + stride;
                int gridD = gridC + 1;
                // Alternate the diagonal to break up long, artificial bands.
                bool alternate = ((row + column) & 1) == 0;
                if (flipWinding)
                {
                    if (alternate)
                    {
                        triangles.Add(gridA); triangles.Add(gridB); triangles.Add(gridC);
                        triangles.Add(gridB); triangles.Add(gridD); triangles.Add(gridC);
                    }
                    else
                    {
                        triangles.Add(gridA); triangles.Add(gridB); triangles.Add(gridD);
                        triangles.Add(gridA); triangles.Add(gridD); triangles.Add(gridC);
                    }
                }
                else
                {
                    if (alternate)
                    {
                        triangles.Add(gridA); triangles.Add(gridC); triangles.Add(gridB);
                        triangles.Add(gridB); triangles.Add(gridC); triangles.Add(gridD);
                    }
                    else
                    {
                        triangles.Add(gridA); triangles.Add(gridD); triangles.Add(gridB);
                        triangles.Add(gridA); triangles.Add(gridC); triangles.Add(gridD);
                    }
                }
            }
        }

        Mesh mesh = new Mesh { name = "Front Cliff Mesh" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static float EvaluateTierProfile(float t)
    {
        for (int index = 1; index < TierBreaks.Length; index++)
        {
            if (t <= TierBreaks[index])
            {
                float localT = Mathf.InverseLerp(TierBreaks[index - 1], TierBreaks[index], t);
                localT = Mathf.SmoothStep(0f, 1f, localT);
                return Mathf.Lerp(TierOffsets[index - 1], TierOffsets[index], localT);
            }
        }

        return TierOffsets[TierOffsets.Length - 1];
    }

    private static Mesh BuildRockMesh()
    {
        const int sides = 9;
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var uvs = new List<Vector2>();
        float[] heights = { -0.46f, -0.08f, 0.34f };
        float[] radii = { 0.58f, 1f, 0.68f };

        for (int ring = 0; ring < heights.Length; ring++)
        {
            for (int side = 0; side < sides; side++)
            {
                float angle = side / (float)sides * Mathf.PI * 2f;
                float irregularity = 0.82f + Mathf.PerlinNoise(side * 1.71f + ring * 3.2f, 4.6f) * 0.34f;
                vertices.Add(new Vector3(
                    Mathf.Cos(angle) * radii[ring] * irregularity,
                    heights[ring],
                    Mathf.Sin(angle) * radii[ring] * irregularity));
                uvs.Add(new Vector2(side / (float)sides, ring / (float)(heights.Length - 1)));
            }
        }

        int bottomCenter = vertices.Count;
        vertices.Add(new Vector3(0f, -0.55f, 0f));
        uvs.Add(new Vector2(0.5f, 0f));
        int topCenter = vertices.Count;
        vertices.Add(new Vector3(0f, 0.56f, 0f));
        uvs.Add(new Vector2(0.5f, 1f));

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
            triangles.Add(bottomCenter); triangles.Add(side); triangles.Add(next);
            int topRing = (heights.Length - 1) * sides;
            triangles.Add(topCenter); triangles.Add(topRing + next); triangles.Add(topRing + side);
        }

        Mesh mesh = new Mesh { name = "Front Cliff Rock" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void CreateTopRubble(
        Transform parent,
        Terrain terrain,
        bool boundaryOnX,
        float sideSign,
        Mesh rockMesh,
        Material material)
    {
        const int rockCount = 10;
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        var random = new System.Random(7319);
        GameObject rubble = new GameObject("Cliff Edge Rubble");
        rubble.transform.SetParent(parent, false);

        for (int index = 0; index < rockCount; index++)
        {
            float along01 = (index + 0.35f + (float)random.NextDouble() * 0.3f) / rockCount;
            float sampleU = boundaryOnX ? (sideSign > 0f ? 1f : 0f) : along01;
            float sampleV = boundaryOnX ? along01 : (sideSign > 0f ? 1f : 0f);
            float height = data.GetInterpolatedHeight(sampleU, sampleV) + origin.y;
            float outwardOffset = 0.35f + (float)random.NextDouble() * 2.8f;
            Vector3 position;
            if (boundaryOnX)
            {
                position = new Vector3(
                    origin.x + (sideSign > 0f ? data.size.x : 0f) + sideSign * outwardOffset,
                    height - 1.8f,
                    origin.z + data.size.z * along01);
            }
            else
            {
                position = new Vector3(
                    origin.x + data.size.x * along01,
                    height - 1.8f,
                    origin.z + (sideSign > 0f ? data.size.z : 0f) + sideSign * outwardOffset);
            }

            GameObject rock = new GameObject("Edge Rock " + (index + 1).ToString("00"));
            rock.transform.SetParent(rubble.transform, false);
            rock.transform.position = position;
            rock.transform.rotation = Quaternion.Euler(
                (float)random.NextDouble() * 18f - 9f,
                (float)random.NextDouble() * 360f,
                (float)random.NextDouble() * 20f - 10f);
            rock.transform.localScale = new Vector3(
                4.0f + (float)random.NextDouble() * 5.2f,
                2.6f + (float)random.NextDouble() * 3.8f,
                3.5f + (float)random.NextDouble() * 4.8f);
            rock.AddComponent<MeshFilter>().sharedMesh = rockMesh;
            MeshRenderer renderer = rock.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
    }

    private static void CreateFaceOutcrops(
        Transform parent,
        Terrain terrain,
        bool boundaryOnX,
        float sideSign,
        Mesh rockMesh,
        Material material)
    {
        const int rockCount = 8;
        TerrainData data = terrain.terrainData;
        Vector3 origin = terrain.transform.position;
        var random = new System.Random(1847);
        GameObject outcrops = new GameObject("Cliff Face Outcrops");
        outcrops.transform.SetParent(parent, false);

        for (int index = 0; index < rockCount; index++)
        {
            float along01 = 0.025f + (float)random.NextDouble() * 0.95f;
            float depth01 = 0.18f + (float)random.NextDouble() * 0.66f;
            float sampleU = boundaryOnX ? (sideSign > 0f ? 1f : 0f) : along01;
            float sampleV = boundaryOnX ? along01 : (sideSign > 0f ? 1f : 0f);
            float topHeight = data.GetInterpolatedHeight(sampleU, sampleV) + origin.y;
            float height = Mathf.Lerp(topHeight, origin.y - 42f, depth01)
                + ((float)random.NextDouble() - 0.5f) * 2.8f;
            float outwardOffset = Mathf.Lerp(1.1f, 4.8f, Mathf.Pow(depth01, 0.9f))
                + ((float)random.NextDouble() - 0.5f) * 1.6f
                - 1.4f;

            Vector3 position;
            if (boundaryOnX)
            {
                position = new Vector3(
                    origin.x + (sideSign > 0f ? data.size.x : 0f) + sideSign * outwardOffset,
                    height,
                    origin.z + data.size.z * along01);
            }
            else
            {
                position = new Vector3(
                    origin.x + data.size.x * along01,
                    height,
                    origin.z + (sideSign > 0f ? data.size.z : 0f) + sideSign * outwardOffset);
            }

            GameObject rock = new GameObject("Face Rock " + (index + 1).ToString("00"));
            rock.transform.SetParent(outcrops.transform, false);
            rock.transform.position = position;
            rock.transform.rotation = Quaternion.Euler(
                (float)random.NextDouble() * 34f - 17f,
                (float)random.NextDouble() * 360f,
                (float)random.NextDouble() * 32f - 16f);
            rock.transform.localScale = new Vector3(
                4.8f + (float)random.NextDouble() * 6.4f,
                3.8f + (float)random.NextDouble() * 5.4f,
                3.2f + (float)random.NextDouble() * 4.4f);
            rock.AddComponent<MeshFilter>().sharedMesh = rockMesh;
            MeshRenderer renderer = rock.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
    }

    private static Material CreateOrUpdateMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (material == null)
        {
            material = new Material(shader) { name = "Front Cliff" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        Texture2D albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(StoneAlbedoPath);
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(StoneNormalPath);
        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_BumpMap", normal);
        material.SetColor("_BaseColor", new Color(0.62f, 0.58f, 0.54f, 1f));
        material.SetFloat("_BumpScale", 0.85f);
        material.SetFloat("_Smoothness", 0.18f);
        material.EnableKeyword("_NORMALMAP");
        material.enableInstancing = true;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void ReplaceAsset(Mesh mesh, string path)
    {
        if (AssetDatabase.LoadAssetAtPath<Mesh>(path) != null)
        {
            AssetDatabase.DeleteAsset(path);
        }
        AssetDatabase.CreateAsset(mesh, path);
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
