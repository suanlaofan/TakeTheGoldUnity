using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Wukong.EditorTools;

public static class TakeTheGoldAndroidBuild
{
    private const string ScenePath = StacklineVrSceneBuilder.VrScenePath;

    [MenuItem("Tools/Stackline Classic/Build PICO APK")]

    public static void Build()
    {
        StacklineLauncherIconSetup.ApplyAndroidIcons();
        StacklineVrSceneBuilder.BuildAndSaveVrScene();
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputPath = Path.Combine(projectRoot, "outputs", "pico", "TakeTheGold.apk");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        Debug.Log($"TakeTheGold APK build: {summary.result}; output={outputPath}; size={summary.totalSize}");

        if (summary.result != BuildResult.Succeeded)
        {
            throw new BuildFailedException($"TakeTheGold APK build failed: {summary.result}");
        }
    }
}
