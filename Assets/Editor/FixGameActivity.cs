using UnityEditor;
using UnityEngine;

public class FixGameActivity
{
    [MenuItem("Tools/VPS/Switch Graphics to Vulkan")]
    public static void SwitchToVulkan()
    {
        var current = PlayerSettings.GetGraphicsAPIs(BuildTarget.Android);
        Debug.Log($"[VPS] Current Graphics APIs: {string.Join(", ", current)}");
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.Vulkan });
        Debug.Log($"[VPS] Switched to: {string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}");
    }

    [MenuItem("Tools/VPS/Switch Graphics to OpenGLES3")]
    public static void SwitchToGLES3()
    {
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        Debug.Log($"[VPS] Switched to: {string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}");
    }

    [MenuItem("Tools/VPS/Allow HTTP Connections")]
    public static void AllowHttp()
    {
        var current = PlayerSettings.insecureHttpOption;
        Debug.Log($"[VPS] Current insecureHttpOption: {current}");
        PlayerSettings.insecureHttpOption = InsecureHttpOption.AlwaysAllowed;
        Debug.Log($"[VPS] Set insecureHttpOption to: {PlayerSettings.insecureHttpOption}");
    }

    [MenuItem("Tools/VPS/Switch to Classic Activity")]
    public static void SwitchToActivity()
    {
        // Read current setting
        var current = PlayerSettings.Android.applicationEntry;
        Debug.Log($"[VPS] Current Application Entry: {current}");

        // Switch to classic Activity (not GameActivity)
        PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
        Debug.Log($"[VPS] Switched to: {PlayerSettings.Android.applicationEntry}");

        // Also log other relevant settings
        Debug.Log($"[VPS] Min SDK: {PlayerSettings.Android.minSdkVersion}");
        Debug.Log($"[VPS] Target SDK: {PlayerSettings.Android.targetSdkVersion}");
        Debug.Log($"[VPS] Graphics APIs: {string.Join(", ", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}");
    }
}
