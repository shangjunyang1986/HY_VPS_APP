using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

public static class EnableARCoreSetup
{
    [MenuItem("Tools/VPS/Enable ARCore XR Loader")]
    public static void EnableARCoreLoader()
    {
        // Get the per-build-target settings container
        var buildTargetSettings = EditorBuildSettings.TryGetConfigObject(
            XRGeneralSettings.k_SettingsKey,
            out XRGeneralSettingsPerBuildTarget perBuildTarget);

        if (!buildTargetSettings || perBuildTarget == null)
        {
            // Create XR settings from scratch
            perBuildTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            if (!AssetDatabase.IsValidFolder("Assets/XR"))
                AssetDatabase.CreateFolder("Assets", "XR");
            AssetDatabase.CreateAsset(perBuildTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perBuildTarget, true);
        }

        var generalSettings = perBuildTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
        if (generalSettings == null)
        {
            generalSettings = ScriptableObject.CreateInstance<XRGeneralSettings>();
            generalSettings.Manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            if (!AssetDatabase.IsValidFolder("Assets/XR"))
                AssetDatabase.CreateFolder("Assets", "XR");
            AssetDatabase.CreateAsset(generalSettings, "Assets/XR/XRGeneralSettings_Android.asset");
            AssetDatabase.CreateAsset(generalSettings.Manager, "Assets/XR/XRManagerSettings_Android.asset");
            perBuildTarget.SetSettingsForBuildTarget(BuildTargetGroup.Android, generalSettings);
        }

        var manager = generalSettings.Manager;
        if (manager == null)
        {
            manager = ScriptableObject.CreateInstance<XRManagerSettings>();
            if (!AssetDatabase.IsValidFolder("Assets/XR"))
                AssetDatabase.CreateFolder("Assets", "XR");
            AssetDatabase.CreateAsset(manager, "Assets/XR/XRManagerSettings_Android.asset");
            generalSettings.Manager = manager;
        }

        // Check if ARCore loader already present
        bool hasARCore = false;
        if (manager.activeLoaders != null)
        {
            foreach (var loader in manager.activeLoaders)
            {
                if (loader != null && loader.GetType().Name == "ARCoreLoader")
                {
                    hasARCore = true;
                    break;
                }
            }
        }

        if (!hasARCore)
        {
            var arcoreLoader = ScriptableObject.CreateInstance<UnityEngine.XR.ARCore.ARCoreLoader>();
            if (!AssetDatabase.IsValidFolder("Assets/XR"))
                AssetDatabase.CreateFolder("Assets", "XR");
            AssetDatabase.CreateAsset(arcoreLoader, "Assets/XR/ARCoreLoader.asset");

            if (manager.TryAddLoader(arcoreLoader))
            {
                EditorUtility.SetDirty(manager);
                EditorUtility.SetDirty(generalSettings);
                EditorUtility.SetDirty(perBuildTarget);
                AssetDatabase.SaveAssets();
                Debug.Log("[VPS] ARCore XR Loader enabled for Android build target.");
            }
            else
            {
                Debug.LogError("[VPS] Failed to add ARCore loader to XR Manager.");
            }
        }
        else
        {
            Debug.Log("[VPS] ARCore XR Loader already enabled.");
        }
    }
}
