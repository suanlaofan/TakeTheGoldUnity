using System;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using Wukong.EditorTools;

public static class TakeTheGoldAndroidBuild
{
    private const string ScenePath = StacklineVrSceneBuilder.VrScenePath;

    [MenuItem("Tools/Stackline Classic/Build PICO APK")]

    public static void Build()
    {
        BuildPerformance(false);
    }

    [MenuItem("Tools/Stackline Classic/Build PICO Diagnostics APK")]
    public static void BuildDiagnostics()
    {
        BuildPerformance(true);
    }

    private static void BuildPerformance(bool diagnostics)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new BuildFailedException("Exit Play Mode before building the PICO APK.");
        // PICO's preprocessor reads graphics APIs from the active target, not the
        // BuildPlayerOptions target. Switching also needs a script recompile first.
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            throw new BuildFailedException("Switch the active Build Profile to Android and wait for compilation before building the PICO APK.");
        StacklineLauncherIconSetup.ApplyAndroidIcons();
        StacklineVrSceneBuilder.BuildAndSaveVrScene();
        StacklinePicoBuildProfile.Validate(SceneManager.GetActiveScene());
        var pipeline = StacklinePicoBuildProfile.EnsureAssets();
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputPath = Path.Combine(projectRoot, "outputs", "pico",
            diagnostics ? "TakeTheGold-audio-fx-diagnostics.apk" : "TakeTheGold-audio-fx.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = diagnostics ? BuildOptions.Development : BuildOptions.None
        };

        string previousVersion = PlayerSettings.bundleVersion;
        int previousCode = PlayerSettings.Android.bundleVersionCode;
        bool previousTiming = PlayerSettings.enableFrameTimingStats;
        try
        {
            PlayerSettings.bundleVersion = "1.2.1-audio-fx.1";
            PlayerSettings.Android.bundleVersionCode = Math.Max(6, previousCode + 1);
            PlayerSettings.enableFrameTimingStats = diagnostics;
            using (new StacklinePicoBuildProfile.BuildPipelineScope(pipeline))
            {
                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;
                Debug.Log($"TakeTheGold APK build: {summary.result}; output={outputPath}; buildReportBytes={summary.totalSize}");
                if (summary.result != BuildResult.Succeeded)
                    throw new BuildFailedException($"TakeTheGold APK build failed: {summary.result}");
                string hash;
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(outputPath))
                    hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                var manifest = new PerformanceBuildManifest
                {
                    utc = DateTime.UtcNow.ToString("O"), version = PlayerSettings.bundleVersion,
                    versionCode = PlayerSettings.Android.bundleVersionCode, diagnostics = diagnostics,
                    apk = outputPath, bytes = (ulong)new FileInfo(outputPath).Length,
                    buildReportBytes = summary.totalSize, sha256 = hash,
                    unity = Application.unityVersion, scene = ScenePath,
                    pipeline = StacklinePicoBuildProfile.PipelinePath,
                    renderScale = pipeline.renderScale,
                    deviceValidation = "Not performed by this build. Frame-rate improvement requires target-device profiling."
                };
                Directory.CreateDirectory(StacklinePicoBuildProfile.ReportDirectory);
                File.WriteAllText(Path.Combine(StacklinePicoBuildProfile.ReportDirectory,
                    diagnostics ? "audio-fx-diagnostics-build.json" : "audio-fx-release-build.json"), JsonUtility.ToJson(manifest, true));
                Debug.Log("STACKLINE_PERFORMANCE_BUILD_PASS sha256=" + hash);
            }
        }
        finally
        {
            PlayerSettings.bundleVersion = previousVersion;
            PlayerSettings.Android.bundleVersionCode = previousCode;
            PlayerSettings.enableFrameTimingStats = previousTiming;
            AssetDatabase.SaveAssets();
        }
    }

    [Serializable] private sealed class PerformanceBuildManifest
    {
        public string utc, version, apk, sha256, unity, scene, pipeline, deviceValidation;
        public int versionCode;
        public ulong bytes, buildReportBytes;
        public bool diagnostics;
        public float renderScale;
    }
}
