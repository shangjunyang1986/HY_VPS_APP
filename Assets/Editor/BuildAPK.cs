using UnityEditor;
using UnityEngine;
using System.IO;

public static class BuildAPK
{
    [MenuItem("Tools/VPS/Build APK")]
    public static void Build()
    {
        // Ensure Android settings are applied
        AndroidBuildSetup.Setup();

        string outputDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Builds");
        if (!Directory.Exists(outputDir))
            Directory.CreateDirectory(outputDir);

        string apkPath = Path.Combine(outputDir, "VPS_AR.apk");

        var buildOptions = new BuildPlayerOptions
        {
            scenes = new[] { "Assets/Scenes/SampleScene.unity" },
            locationPathName = apkPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        Debug.Log($"[VPS] Building APK to: {apkPath}");

        var report = BuildPipeline.BuildPlayer(buildOptions);

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            Debug.Log($"[VPS] APK build succeeded! Size: {report.summary.totalSize / (1024 * 1024)} MB");
            Debug.Log($"[VPS] Output: {apkPath}");
        }
        else
        {
            Debug.LogError($"[VPS] APK build failed: {report.summary.result}");
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error)
                        Debug.LogError($"[VPS Build] {msg.content}");
                }
            }
        }
    }
}
