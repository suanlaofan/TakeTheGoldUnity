using System;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace Wukong.StacklineClassic
{
    public readonly struct StacklineEffectPoolStats
    {
        public readonly int ActiveSparks, ActiveFragments, ActiveOutlines;
        public readonly int SparkCapacity, FragmentCapacity, OutlineCapacity, OwnedMaterials;

        internal StacklineEffectPoolStats(int sparks, int fragments, int outlines, int materials)
        {
            ActiveSparks = sparks;
            ActiveFragments = fragments;
            ActiveOutlines = outlines;
            SparkCapacity = StacklineEffectPool.MaxSparks;
            FragmentCapacity = StacklineEffectPool.MaxFragments;
            OutlineCapacity = StacklineEffectPool.MaxOutlines;
            OwnedMaterials = materials;
        }
    }

    /// <summary>
    /// Prewarmed, bounded visual effects. This class owns its root, cube/outline meshes and
    /// palette; it borrows the game's gold mesh/material. Clear returns slots without freeing
    /// shared resources. Dispose must precede release of the borrowed gold resources.
    /// </summary>
    public sealed class StacklineEffectPool : IDisposable
    {
        public const int MaxSparks = 96;
        public const int MaxFragments = 48;
        public const int MaxOutlines = 8;
        private const float SparkLifetime = 1f;
        private const float FragmentLifetime = 4.5f;
        private const float OutlineLifetime = 0.55f;

        private sealed class Slot
        {
            public GameObject Object;
            public Transform Transform;
            public MeshRenderer Renderer;
            public Mesh OutlineMesh;
            public Vector3[] OutlineVertices;
            public Vector3 Velocity, AngularVelocity, InitialScale;
            public float Age, Lifetime;
            public long Sequence;
            public bool Active;
        }

        private readonly GameObject root;
        private readonly Mesh cubeMesh;
        private readonly Material[] palette;
        private readonly Material outlineMaterial;
        private readonly Slot[] sparks = new Slot[MaxSparks];
        private readonly Slot[] fragments = new Slot[MaxFragments];
        private readonly Slot[] outlines = new Slot[MaxOutlines];
        private bool disposed;
        private long nextSequence;

        public StacklineEffectPool(Material goldMaterial, Mesh goldMesh, Color[] colors, int layer)
        {
            root = new GameObject("Stackline Visual Effect Pool");
            root.layer = layer;
            cubeMesh = CreateBoxMesh("Stackline Effect Cube", 1);
            palette = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++)
                palette[i] = CreateMaterial(colors[i], "Stackline Spark Palette " + i);
            outlineMaterial = CreateMaterial(Color.white, "Stackline Perfect Outline");
            for (int i = 0; i < sparks.Length; i++)
                sparks[i] = CreateSlot("Perfect Spark " + i, cubeMesh, palette[0], layer);
            for (int i = 0; i < fragments.Length; i++)
                fragments[i] = CreateSlot("Falling Gold " + i, goldMesh != null ? goldMesh : cubeMesh,
                    goldMaterial, layer);
            for (int i = 0; i < outlines.Length; i++)
            {
                Mesh mesh = CreateBoxMesh("Stackline Outline Mesh " + i, 4);
                mesh.MarkDynamic();
                Slot slot = CreateSlot("Perfect Outline " + i, mesh, outlineMaterial, layer);
                slot.OutlineMesh = mesh;
                slot.OutlineVertices = new Vector3[96];
                outlines[i] = slot;
            }
        }

        public StacklineEffectPoolStats Stats => new StacklineEffectPoolStats(
            CountActive(sparks), CountActive(fragments), CountActive(outlines), disposed ? 0 : palette.Length + 1);

        public void SpawnPerfect(Transform owner, Vector3 worldPosition, int theme)
        {
            if (disposed) return;
            Material material = palette[Mathf.Clamp(theme, 0, palette.Length - 1)];
            for (int i = 0; i < 16; i++)
            {
                Slot slot = Acquire(sparks, SparkLifetime);
                // Match the former cube's owner-local size/orientation, then detach. Gravity
                // and velocity are world-space and must not inherit later arena transforms.
                slot.Transform.SetParent(owner, false);
                slot.Transform.position = worldPosition;
                slot.Transform.localRotation = Quaternion.Inverse(owner.rotation);
                slot.Transform.localScale = Vector3.one * 0.05f;
                slot.Transform.SetParent(root.transform, true);
                slot.Renderer.sharedMaterial = material;
                slot.Velocity = new Vector3(Random.Range(-1.2f, 1.2f), Random.Range(0.7f, 2.1f),
                    Random.Range(-1.2f, 1.2f));
            }
        }

        public void SpawnFragment(Transform space, Vector3 position, Quaternion rotation,
            Vector3 scale, Vector3 worldVelocity)
        {
            if (disposed) return;
            Slot slot = Acquire(fragments, FragmentLifetime);
            slot.Transform.SetParent(space, false);
            slot.Transform.SetLocalPositionAndRotation(position, rotation);
            slot.Transform.localScale = scale;
            // This reproduces the old SetParent(null, true) before Rigidbody simulation,
            // including scaled/rotated arena placement. The pool root always has identity pose.
            slot.Transform.SetParent(root.transform, true);
            slot.Velocity = worldVelocity;
            slot.AngularVelocity = new Vector3(Random.Range(-3.5f, 3.5f), Random.Range(-2.5f, 2.5f),
                Random.Range(-3.5f, 3.5f));
        }

        public void SpawnOutline(Transform arena, Vector3 position, Vector3 size)
        {
            if (disposed) return;
            Slot slot = Acquire(outlines, OutlineLifetime);
            slot.Transform.SetParent(arena, false);
            slot.Transform.SetLocalPositionAndRotation(position + Vector3.up * (size.y * 0.5f + 0.045f),
                Quaternion.identity);
            slot.Transform.localScale = Vector3.one;
            slot.InitialScale = Vector3.one;
            float thickness = Mathf.Clamp(Mathf.Min(size.x, size.z) * 0.018f, 0.025f, 0.055f);
            WriteBox(slot.OutlineVertices, 0, new Vector3(0f, 0f, size.z * 0.5f),
                new Vector3(size.x + thickness * 2f, 0.028f, thickness));
            WriteBox(slot.OutlineVertices, 24, new Vector3(0f, 0f, -size.z * 0.5f),
                new Vector3(size.x + thickness * 2f, 0.028f, thickness));
            WriteBox(slot.OutlineVertices, 48, new Vector3(-size.x * 0.5f, 0f, 0f),
                new Vector3(thickness, 0.028f, size.z + thickness * 2f));
            WriteBox(slot.OutlineVertices, 72, new Vector3(size.x * 0.5f, 0f, 0f),
                new Vector3(thickness, 0.028f, size.z + thickness * 2f));
            slot.OutlineMesh.SetVertices(slot.OutlineVertices);
            slot.OutlineMesh.RecalculateBounds();
        }

        public void Tick(float deltaTime, float minimumWorldY)
        {
            if (disposed || deltaTime <= 0f) return;
            TickFalling(sparks, deltaTime, minimumWorldY, false);
            TickFalling(fragments, deltaTime, minimumWorldY, true);
            for (int i = 0; i < outlines.Length; i++)
            {
                Slot slot = outlines[i];
                if (!slot.Active) continue;
                slot.Age += deltaTime;
                if (slot.Age >= slot.Lifetime) { Release(slot); continue; }
                float progress = Mathf.Clamp01(slot.Age / slot.Lifetime);
                float eased = 1f - Mathf.Pow(1f - progress, 3f);
                slot.Transform.localScale = slot.InitialScale * Mathf.Lerp(1f, 1.16f, eased);
            }
        }

        public void Clear()
        {
            if (disposed) return;
            ClearSlots(sparks);
            ClearSlots(fragments);
            ClearSlots(outlines);
        }

        public bool Validate(out string problem)
        {
            if (disposed) { problem = "Effect pool has been disposed."; return false; }
            if (root == null || cubeMesh == null || outlineMaterial == null)
            { problem = "An owned effect resource is missing."; return false; }
            for (int i = 0; i < palette.Length; i++)
                if (palette[i] == null) { problem = "An effect palette material is missing."; return false; }
            return ValidateSlots(sparks, out problem) && ValidateSlots(fragments, out problem)
                && ValidateSlots(outlines, out problem);
        }

        public void Dispose()
        {
            if (disposed) return;
            Clear(); // Reparents outlines out of the arena before destroying our own root.
            disposed = true;
            DestroyOwned(root);
            for (int i = 0; i < outlines.Length; i++) DestroyOwned(outlines[i].OutlineMesh);
            for (int i = 0; i < palette.Length; i++) DestroyOwned(palette[i]);
            DestroyOwned(outlineMaterial);
            DestroyOwned(cubeMesh);
        }

        private Slot CreateSlot(string name, Mesh mesh, Material material, int layer)
        {
            GameObject obj = new GameObject(name);
            obj.SetActive(false);
            obj.layer = layer;
            obj.transform.SetParent(root.transform, false);
            obj.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = obj.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return new Slot { Object = obj, Transform = obj.transform, Renderer = renderer };
        }

        private Slot Acquire(Slot[] slots, float lifetime)
        {
            Slot chosen = slots[0];
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Active) { chosen = slots[i]; break; }
                if (slots[i].Sequence < chosen.Sequence) chosen = slots[i];
            }
            // Saturation replaces the oldest cosmetic effect, never a gameplay block.
            Release(chosen);
            chosen.Age = 0f;
            chosen.Lifetime = lifetime;
            chosen.Sequence = ++nextSequence;
            chosen.Active = true;
            chosen.Object.SetActive(true);
            return chosen;
        }

        private void Release(Slot slot)
        {
            slot.Active = false;
            slot.Age = 0f;
            slot.Lifetime = 0f;
            slot.Velocity = Vector3.zero;
            slot.AngularVelocity = Vector3.zero;
            // Scene teardown can destroy an arena (and its active outlines) before the
            // controller's OnDestroy. Clear/Dispose remain safe in either destruction order.
            if (slot.Object == null) return;
            slot.Object.SetActive(false);
            slot.Transform.SetParent(root != null ? root.transform : null, false);
            slot.Transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            slot.Transform.localScale = Vector3.one;
        }

        private void TickFalling(Slot[] slots, float delta, float minimumWorldY, bool damped)
        {
            Vector3 gravity = Physics.gravity;
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = slots[i];
                if (!slot.Active) continue;
                slot.Age += delta;
                if (slot.Age >= slot.Lifetime) { Release(slot); continue; }
                // Analytic gravity avoids frame-rate-dependent acceleration on visual pieces.
                slot.Transform.position += slot.Velocity * delta + gravity * (0.5f * delta * delta);
                slot.Velocity += gravity * delta;
                if (damped) slot.Velocity *= Mathf.Exp(-0.01f * delta);
                float angularSpeed = slot.AngularVelocity.magnitude;
                if (angularSpeed > 0f)
                    slot.Transform.rotation = Quaternion.AngleAxis(angularSpeed * delta * Mathf.Rad2Deg,
                        slot.AngularVelocity / angularSpeed) * slot.Transform.rotation;
                if (damped) slot.AngularVelocity *= Mathf.Exp(-0.02f * delta);
                if (slot.Transform.position.y < minimumWorldY) Release(slot);
            }
        }

        private void ClearSlots(Slot[] slots)
        {
            for (int i = 0; i < slots.Length; i++) Release(slots[i]);
        }

        private static int CountActive(Slot[] slots)
        {
            int count = 0;
            for (int i = 0; i < slots.Length; i++) if (slots[i].Active) count++;
            return count;
        }

        private static bool ValidateSlots(Slot[] slots, out string problem)
        {
            for (int i = 0; i < slots.Length; i++)
            {
                Slot slot = slots[i];
                if (slot.Object == null || slot.Renderer == null || slot.Renderer.sharedMaterial == null ||
                    slot.Object.GetComponent<MeshFilter>().sharedMesh == null ||
                    slot.Object.activeSelf != slot.Active || slot.Object.GetComponent<Rigidbody>() != null ||
                    slot.Object.GetComponent<Collider>() != null)
                { problem = "An effect slot has invalid ownership, activity, renderer or physics state."; return false; }
            }
            problem = null;
            return true;
        }

        private static Material CreateMaterial(Color color, string name)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = new Material(shader) { name = name };
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.9f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.5f);
            material.DisableKeyword("_EMISSION");
            return material;
        }

        private static Mesh CreateBoxMesh(string name, int boxes)
        {
            Vector3[] vertices = new Vector3[boxes * 24];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[boxes * 36];
            Vector3[] faceNormals = { Vector3.back, Vector3.forward, Vector3.left, Vector3.right,
                Vector3.down, Vector3.up };
            for (int box = 0; box < boxes; box++)
            {
                WriteBox(vertices, box * 24, Vector3.zero, Vector3.one);
                for (int face = 0; face < 6; face++)
                {
                    int v = box * 24 + face * 4;
                    int t = box * 36 + face * 6;
                    for (int c = 0; c < 4; c++) normals[v + c] = faceNormals[face];
                    uv[v] = Vector2.zero; uv[v + 1] = Vector2.up;
                    uv[v + 2] = Vector2.one; uv[v + 3] = Vector2.right;
                    triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
                    triangles[t + 3] = v; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                }
            }
            Mesh mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            mesh.vertices = vertices; mesh.normals = normals; mesh.uv = uv; mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void WriteBox(Vector3[] v, int i, Vector3 c, Vector3 size)
        {
            Vector3 h = size * 0.5f;
            Vector3 a = c + new Vector3(-h.x, -h.y, -h.z), b = c + new Vector3(-h.x, h.y, -h.z);
            Vector3 d = c + new Vector3(h.x, -h.y, -h.z), e = c + new Vector3(h.x, h.y, -h.z);
            Vector3 f = c + new Vector3(-h.x, -h.y, h.z), g = c + new Vector3(-h.x, h.y, h.z);
            Vector3 j = c + new Vector3(h.x, -h.y, h.z), k = c + new Vector3(h.x, h.y, h.z);
            v[i] = a; v[i + 1] = b; v[i + 2] = e; v[i + 3] = d;
            v[i + 4] = j; v[i + 5] = k; v[i + 6] = g; v[i + 7] = f;
            v[i + 8] = f; v[i + 9] = g; v[i + 10] = b; v[i + 11] = a;
            v[i + 12] = d; v[i + 13] = e; v[i + 14] = k; v[i + 15] = j;
            v[i + 16] = f; v[i + 17] = a; v[i + 18] = d; v[i + 19] = j;
            v[i + 20] = b; v[i + 21] = g; v[i + 22] = k; v[i + 23] = e;
        }

        private static void DestroyOwned(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }
    }
}
