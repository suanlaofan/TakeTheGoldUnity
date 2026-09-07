using System.Collections.Generic;
using UnityEngine;

namespace Wukong.StacklineClassic
{
    internal static class StacklineGoldVisual
    {
        private const int RingVertexCount = 8;
        private static readonly Color GoldBaseTint = new Color(0.72f, 0.58f, 0.30f, 1f);
        private static Mesh ingotMesh;
        private static int meshOwners;

        internal static Mesh AcquireIngotMesh()
        {
            Mesh mesh = GetIngotMesh();
            meshOwners++;
            return mesh;
        }

        internal static void ReleaseIngotMesh()
        {
            if (meshOwners > 0) meshOwners--;
            if (meshOwners != 0 || ingotMesh == null) return;
            if (Application.isPlaying) Object.Destroy(ingotMesh);
            else Object.DestroyImmediate(ingotMesh);
            ingotMesh = null;
        }

        public static Material CreateTunedMaterial(GameObject goldBarPrefab)
        {
            if (goldBarPrefab == null)
                return null;

            Renderer sourceRenderer = goldBarPrefab.GetComponentInChildren<Renderer>(true);
            if (sourceRenderer == null || sourceRenderer.sharedMaterial == null)
                return null;

            Material material = new Material(sourceRenderer.sharedMaterial)
            {
                name = "Stackline Tuned Gold",
            };

            // glTFast and URP Lit use different property names. Keep the imported textures,
            // but tune both shader variants to the same restrained, physically metallic gold.
            SetColor(material, "baseColorFactor", GoldBaseTint);
            SetColor(material, "_BaseColor", GoldBaseTint);
            if (!material.HasProperty("baseColorFactor") && !material.HasProperty("_BaseColor"))
                material.color = GoldBaseTint;

            SetFloat(material, "metallicFactor", 0.90f);
            SetFloat(material, "_Metallic", 0.90f);
            SetFloat(material, "roughnessFactor", 0.50f);
            SetFloat(material, "_Smoothness", 0.50f);
            SetColor(material, "emissiveFactor", Color.black);
            SetColor(material, "_EmissionColor", Color.black);
            material.DisableKeyword("_EMISSION");
            return material;
        }

        public static Renderer Attach(GameObject blockRoot, GameObject goldBarPrefab, Material goldMaterial,
            int layer)
        {
            if (blockRoot == null || goldBarPrefab == null)
                return null;

            MeshFilter meshFilter = blockRoot.GetComponent<MeshFilter>();
            Renderer renderer = blockRoot.GetComponent<Renderer>();
            Renderer sourceRenderer = goldBarPrefab.GetComponentInChildren<Renderer>(true);
            if (meshFilter == null || renderer == null || sourceRenderer == null || sourceRenderer.sharedMaterial == null)
                return null;

            blockRoot.layer = layer;
            meshFilter.sharedMesh = GetIngotMesh();
            renderer.sharedMaterial = goldMaterial != null ? goldMaterial : sourceRenderer.sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return renderer;
        }

        private static void SetColor(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
                material.SetColor(propertyName, value);
        }

        private static void SetFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
                material.SetFloat(propertyName, value);
        }

        private static Mesh GetIngotMesh()
        {
            if (ingotMesh != null)
                return ingotMesh;

            Vector3[][] rings =
            {
                CreateRing(-0.50f, 0.43f, 0.43f, 0.065f),
                CreateRing(-0.39f, 0.50f, 0.50f, 0.085f),
                CreateRing(0.35f, 0.42f, 0.42f, 0.072f),
                CreateRing(0.50f, 0.34f, 0.34f, 0.060f),
            };

            List<Vector3> vertices = new List<Vector3>(160);
            List<Vector2> uvs = new List<Vector2>(160);
            List<int> triangles = new List<int>(240);

            for (int ring = 0; ring < rings.Length - 1; ring++)
            {
                float v0 = ring / (float)(rings.Length - 1);
                float v1 = (ring + 1) / (float)(rings.Length - 1);
                for (int segment = 0; segment < RingVertexCount; segment++)
                {
                    int next = (segment + 1) % RingVertexCount;
                    int start = vertices.Count;
                    vertices.Add(rings[ring][segment]);
                    vertices.Add(rings[ring + 1][segment]);
                    vertices.Add(rings[ring + 1][next]);
                    vertices.Add(rings[ring][next]);

                    float u0 = segment / (float)RingVertexCount;
                    float u1 = (segment + 1) / (float)RingVertexCount;
                    uvs.Add(new Vector2(u0, v0));
                    uvs.Add(new Vector2(u0, v1));
                    uvs.Add(new Vector2(u1, v1));
                    uvs.Add(new Vector2(u1, v0));

                    triangles.Add(start);
                    triangles.Add(start + 1);
                    triangles.Add(start + 2);
                    triangles.Add(start);
                    triangles.Add(start + 2);
                    triangles.Add(start + 3);
                }
            }

            AddCap(rings[0], false, vertices, uvs, triangles);
            AddCap(rings[rings.Length - 1], true, vertices, uvs, triangles);

            ingotMesh = new Mesh
            {
                name = "Stackline Tapered Gold Ingot",
                hideFlags = HideFlags.DontSave,
            };
            ingotMesh.SetVertices(vertices);
            ingotMesh.SetUVs(0, uvs);
            ingotMesh.SetTriangles(triangles, 0);
            ingotMesh.RecalculateNormals();
            ingotMesh.RecalculateTangents();
            ingotMesh.RecalculateBounds();
            return ingotMesh;
        }

        private static Vector3[] CreateRing(float y, float halfX, float halfZ, float corner)
        {
            return new[]
            {
                new Vector3(-halfX + corner, y, -halfZ),
                new Vector3(halfX - corner, y, -halfZ),
                new Vector3(halfX, y, -halfZ + corner),
                new Vector3(halfX, y, halfZ - corner),
                new Vector3(halfX - corner, y, halfZ),
                new Vector3(-halfX + corner, y, halfZ),
                new Vector3(-halfX, y, halfZ - corner),
                new Vector3(-halfX, y, -halfZ + corner),
            };
        }

        private static void AddCap(Vector3[] ring, bool top, List<Vector3> vertices, List<Vector2> uvs,
            List<int> triangles)
        {
            Vector3 center = new Vector3(0f, ring[0].y, 0f);
            for (int segment = 0; segment < RingVertexCount; segment++)
            {
                int next = (segment + 1) % RingVertexCount;
                int start = vertices.Count;
                vertices.Add(center);
                vertices.Add(ring[segment]);
                vertices.Add(ring[next]);
                uvs.Add(new Vector2(0.5f, 0.5f));
                uvs.Add(new Vector2(ring[segment].x + 0.5f, ring[segment].z + 0.5f));
                uvs.Add(new Vector2(ring[next].x + 0.5f, ring[next].z + 0.5f));

                triangles.Add(start);
                triangles.Add(top ? start + 2 : start + 1);
                triangles.Add(top ? start + 1 : start + 2);
            }
        }
    }
}
