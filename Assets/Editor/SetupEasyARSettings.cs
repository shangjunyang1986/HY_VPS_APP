using UnityEngine;
using UnityEditor;

public static class SetupEasyARSettings
{
    [MenuItem("Tools/VPS/Register EasyAR Settings")]
    public static void Register()
    {
        var settings = AssetDatabase.LoadAssetAtPath<easyar.EasyARSettings>("Assets/Settings/EasyARSettings.asset");
        if (settings == null)
        {
            Debug.LogError("EasyARSettings not found at Assets/Settings/EasyARSettings.asset");
            return;
        }
        EditorBuildSettings.AddConfigObject("EasyAR.Settings", settings, true);
        Debug.Log($"EasyAR Settings registered. License key length: {settings.LicenseKey.Length}");
    }
}
