using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

public static class AndroidBuildSetup
{
    [MenuItem("Tools/VPS/Setup Android Build")]
    public static void Setup()
    {
        // Switch to Android platform
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            Debug.Log("[VPS] Switching build target to Android...");
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        }

        var android = NamedBuildTarget.Android;

        // Player Settings
        PlayerSettings.companyName = "HY_VPS";
        PlayerSettings.productName = "VPS AR";
        PlayerSettings.SetApplicationIdentifier(android, "com.hyvps.ar");

        // Scripting backend: IL2CPP (required for ARM64-only)
        PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);

        // Target architecture: ARM64 only (required for ARCore)
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        // Minimum API level: Android 10 (API 29) for ARCore
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;

        // Graphics API: OpenGLES3 (explicit, avoids Vulkan issues)
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android,
            new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });

        // Disable multi-threaded rendering for AR stability
        PlayerSettings.SetMobileMTRendering(android, false);

        Debug.Log("[VPS] Android build configuration complete:");
        Debug.Log($"  Company: {PlayerSettings.companyName}");
        Debug.Log($"  Product: {PlayerSettings.productName}");
        Debug.Log($"  Bundle ID: com.hyvps.ar");
        Debug.Log($"  Scripting: IL2CPP, ARM64");
        Debug.Log($"  Min SDK: API 29 (Android 10)");
        Debug.Log($"  Graphics: OpenGLES3");
    }
}
