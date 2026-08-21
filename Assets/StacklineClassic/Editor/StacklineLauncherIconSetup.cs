using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Wukong.EditorTools
{
    /// <summary>
    /// Applies the checked-in Stackline launcher artwork to every Android/PICO icon slot.
    /// Keeping this in the build path prevents a future APK from silently reverting to
    /// Unity's default launcher icon when PlayerSettings are reset or migrated.
    /// </summary>
    public static class StacklineLauncherIconSetup
    {
        private const string ForegroundPath = "Assets/StacklineClassic/Art/UI/StacklineLauncherIcon.png";
        private const string BackgroundPath = "Assets/StacklineClassic/Art/UI/StacklineAdaptiveBackground.png";

        [MenuItem("Tools/Stackline Classic/Configure Android Launcher Icon")]
        public static void ApplyAndroidIcons()
        {
            Texture2D foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(ForegroundPath);
            Texture2D background = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);

            if (foreground == null || background == null)
                throw new BuildFailedException(
                    "Stackline Android launcher icon assets are missing. Expected " +
                    ForegroundPath + " and " + BackgroundPath + ".");

            foreach (PlatformIconKind kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                PlatformIcon[] icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (PlatformIcon icon in icons)
                {
                    if (icon.layerCount <= 1)
                    {
                        // Legacy and round launchers receive the finished supplied artwork.
                        icon.SetTexture(foreground, 0);
                        continue;
                    }

                    // Android adaptive icons expose Background first, then Foreground. Use a
                    // solid dark-teal backing layer so the supplied transparent corners never
                    // reveal a system-default color.
                    var layers = new Texture2D[icon.layerCount];
                    layers[0] = background;
                    for (int layer = 1; layer < layers.Length; layer++)
                        layers[layer] = foreground;
                    icon.SetTextures(layers);
                }

                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }

            AssetDatabase.SaveAssets();
            Debug.Log("Stackline Android/PICO launcher icons configured from supplied artwork.");
        }
    }
}
