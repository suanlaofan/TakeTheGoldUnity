#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Wukong.StacklineClassic.EditorTools
{
    /// <summary>Isolated Edit Mode resource/trajectory validation; never edits the game scene.</summary>
    public static class StacklineEffectPoolValidation
    {
        [MenuItem("Tools/Stackline/Validate Effect Pool")]
        public static void Run()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException("Run the isolated pool check outside Play Mode.");
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            GameObject parent = null, arena = null, reference = null;
            Material borrowedMaterial = null;
            StacklineEffectPool pool = null;
            try
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                Require(shader != null, "No supported shader is available.");
                borrowedMaterial = new Material(shader) { name = "Pool Validation Borrowed Gold" };
                parent = new GameObject("Pool Validation Parent");
                parent.transform.SetPositionAndRotation(new Vector3(8f, 3f, -4f), Quaternion.Euler(7f, 31f, 5f));
                parent.transform.localScale = new Vector3(1.4f, 0.9f, 1.7f);
                arena = new GameObject("Pool Validation Arena");
                arena.transform.SetParent(parent.transform, false);
                arena.transform.localPosition = new Vector3(2f, 1f, -3f);
                arena.transform.localRotation = Quaternion.Euler(13f, -24f, 4f);
                arena.transform.localScale = new Vector3(-1.8f, 2.1f, 0.75f);

                pool = new StacklineEffectPool(borrowedMaterial, null,
                    new[] { Color.yellow, Color.red, Color.white, Color.cyan, Color.gray, Color.green }, 0);
                // Inspect only this test's root. Reflection avoids adding mutable scene handles
                // to the public runtime diagnostics API just for an Editor test.
                GameObject poolRoot = (GameObject)typeof(StacklineEffectPool)
                    .GetField("root", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(pool);
                Renderer[] renderers = poolRoot.GetComponentsInChildren<Renderer>(true);
                Require(renderers.Length == 152, "Prewarm capacity differs from the declared 96/48/8 bounds.");
                Require(pool.Validate(out string issue), issue);

                Vector3 localPosition = new Vector3(0.4f, 4f, -0.8f);
                Vector3 localScale = new Vector3(1.3f, 0.52f, 0.45f);
                Vector3 velocity = arena.transform.TransformVector(new Vector3(3.1f, -0.4f, 0f));
                reference = new GameObject("Pool Validation Legacy Pose");
                reference.transform.SetParent(arena.transform, false);
                reference.transform.SetLocalPositionAndRotation(localPosition, Quaternion.identity);
                reference.transform.localScale = localScale;
                reference.transform.SetParent(null, true);
                Matrix4x4 expectedPose = reference.transform.localToWorldMatrix;
                pool.SpawnFragment(arena.transform, localPosition, Quaternion.identity, localScale, velocity);
                Transform fragment = FindActive(renderers, "Falling Gold ");
                Require(fragment != null, "Fragment was not activated.");
                for (int i = 0; i < 16; i++)
                    Require(Mathf.Abs(fragment.localToWorldMatrix[i] - expectedPose[i]) < 0.001f,
                        "Fragment changed the legacy world pose under a rotated/nonuniform/negative-scale arena.");
                Vector3 startPosition = fragment.position;
                pool.Tick(0.25f, -1000f);
                Vector3 expectedPosition = startPosition + velocity * 0.25f + Physics.gravity * (0.5f * 0.25f * 0.25f);
                Require(Vector3.Distance(fragment.position, expectedPosition) < 0.001f,
                    "World-space gravity or launch velocity changed under a scaled arena.");

                int originalRenderers = renderers.Length;
                int originalMaterials = pool.Stats.OwnedMaterials;
                for (int i = 0; i < 100; i++)
                {
                    pool.SpawnPerfect(arena.transform, arena.transform.TransformPoint(localPosition), i % 6);
                    pool.SpawnFragment(arena.transform, localPosition, Quaternion.identity, localScale, velocity);
                    pool.SpawnOutline(arena.transform, localPosition, localScale);
                    pool.Tick(0.02f, -1000f);
                    StacklineEffectPoolStats stats = pool.Stats;
                    Require(stats.ActiveSparks <= stats.SparkCapacity && stats.ActiveFragments <= stats.FragmentCapacity &&
                        stats.ActiveOutlines <= stats.OutlineCapacity && stats.OwnedMaterials == originalMaterials,
                        "Repeated effects grew beyond their prewarmed resource bounds.");
                }
                Require(poolRoot.GetComponentsInChildren<Renderer>(true).Length +
                    arena.GetComponentsInChildren<Renderer>(true).Length == originalRenderers,
                    "Repeated effects created renderers instead of reusing slots.");
                Require(pool.Validate(out issue), issue);
                pool.Tick(5f, -1000f);
                AssertEmpty(pool, "Expiry did not return all active effects.");

                for (int i = 0; i < 20; i++)
                {
                    pool.SpawnPerfect(arena.transform, arena.transform.position, i % 6);
                    pool.SpawnFragment(arena.transform, localPosition, Quaternion.identity, localScale, velocity);
                    pool.SpawnOutline(arena.transform, localPosition, localScale);
                    pool.Clear();
                    AssertEmpty(pool, "Clear did not return all slots.");
                    Require(pool.Validate(out issue), issue);
                    Require(borrowedMaterial != null, "Clear destroyed a borrowed shared material.");
                }
                // A freshly reused slot must expire on its own new lifetime.
                pool.SpawnFragment(arena.transform, localPosition, Quaternion.identity, localScale, velocity);
                pool.Tick(4f, -1000f);
                Require(pool.Stats.ActiveFragments == 1, "A stale lifetime reclaimed a reused fragment.");
                pool.Tick(0.6f, -1000f);
                AssertEmpty(pool, "The fragment exceeded its fixed lifetime.");

                // Simulate scene teardown destroying the arena before controller disposal.
                pool.SpawnOutline(arena.transform, localPosition, localScale);
                UnityEngine.Object.DestroyImmediate(parent);
                pool.Dispose();
                pool.Dispose();
                Require(poolRoot == null, "Dispose left its scene root alive.");
                Require(borrowedMaterial != null, "Dispose destroyed a material owned by the game.");
                Debug.Log("STACKLINE_EFFECT_POOL_PASS: 100 bursts; 20 clears; bounded resources; zero effect physics; " +
                    "world pose/gravity under rotated negative nonuniform scale; independent expiry; safe disposal.");
            }
            finally
            {
                pool?.Dispose();
                if (parent != null) UnityEngine.Object.DestroyImmediate(parent);
                if (reference != null) UnityEngine.Object.DestroyImmediate(reference);
                if (borrowedMaterial != null) UnityEngine.Object.DestroyImmediate(borrowedMaterial);
                UnityEngine.Random.state = randomState;
            }
        }

        private static Transform FindActive(Renderer[] renderers, string prefix)
        {
            foreach (Renderer renderer in renderers)
                if (renderer.gameObject.activeSelf && renderer.name.StartsWith(prefix, StringComparison.Ordinal))
                    return renderer.transform;
            return null;
        }

        private static void AssertEmpty(StacklineEffectPool pool, string message)
        {
            StacklineEffectPoolStats stats = pool.Stats;
            Require(stats.ActiveSparks == 0 && stats.ActiveFragments == 0 && stats.ActiveOutlines == 0, message);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
