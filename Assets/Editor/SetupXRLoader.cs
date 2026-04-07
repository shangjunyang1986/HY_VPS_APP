using UnityEditor;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.XR.Management;

public static class SetupXRLoader
{
    [MenuItem("Tools/VPS/Setup ARCore XR")]
    public static void Setup()
    {
        // Step 1: Load the PerBuildTarget object
        string settingsPath = "Assets/XR/XRGeneralSettings.asset";
        var perBT = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(settingsPath);
        if (perBT == null)
        {
            Debug.LogError("[VPS] PerBuildTarget not found");
            return;
        }

        // Step 2: Get or create XRGeneralSettings for Android
        var generalSettings = perBT.SettingsForBuildTarget(BuildTargetGroup.Android);
        if (generalSettings == null)
        {
            Debug.LogError("[VPS] No Android general settings found");
            return;
        }

        // Step 3: Create a fresh XRManagerSettings and add ARCore loader
        var arcoreLoaderType = System.Type.GetType(
            "UnityEngine.XR.ARCore.ARCoreLoader, Unity.XR.ARCore");
        if (arcoreLoaderType == null)
        {
            Debug.LogError("[VPS] ARCoreLoader type not found");
            return;
        }

        // Load existing ARCore loader from Assets/XR/Loaders/
        var existingLoader = AssetDatabase.LoadAssetAtPath<XRLoader>(
            "Assets/XR/Loaders/ARCoreLoader.asset");

        if (existingLoader == null)
        {
            Debug.LogError("[VPS] ARCoreLoader.asset not found in Assets/XR/Loaders/");
            return;
        }

        // Create XRManagerSettings as sub-asset or standalone
        var manager = ScriptableObject.CreateInstance<XRManagerSettings>();
        manager.name = "XRManagerSettings_Android";

        // Save it
        string mgrPath = "Assets/XR/XRManagerSettings_Android.asset";
        AssetDatabase.DeleteAsset(mgrPath);
        AssetDatabase.CreateAsset(manager, mgrPath);

        // Reload after create
        manager = AssetDatabase.LoadAssetAtPath<XRManagerSettings>(mgrPath);

        // Add loader
        bool added = manager.TryAddLoader(existingLoader);
        Debug.Log($"[VPS] TryAddLoader result: {added}");

        // Use SerializedObject to set automaticLoading and automaticRunning
        var so = new SerializedObject(manager);
        so.FindProperty("m_AutomaticLoading").boolValue = true;
        so.FindProperty("m_AutomaticRunning").boolValue = true;

        // Force the loaders list
        var loadersProp = so.FindProperty("m_Loaders");
        if (loadersProp != null)
        {
            loadersProp.ClearArray();
            loadersProp.InsertArrayElementAtIndex(0);
            loadersProp.GetArrayElementAtIndex(0).objectReferenceValue = existingLoader;
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(manager);

        // Step 4: Link manager to generalSettings via SerializedObject
        var gsSO = new SerializedObject(generalSettings);
        var mgrProp = gsSO.FindProperty("m_LoaderManagerInstance");
        if (mgrProp != null)
        {
            mgrProp.objectReferenceValue = manager;
            gsSO.ApplyModifiedProperties();
            EditorUtility.SetDirty(generalSettings);
            Debug.Log("[VPS] Linked XRManagerSettings to XRGeneralSettings");
        }
        else
        {
            Debug.LogError("[VPS] Could not find m_LoaderManagerInstance property");
        }

        // Step 5: Register config object
        EditorBuildSettings.AddConfigObject(
            XRGeneralSettings.k_SettingsKey,
            perBT, true);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Verify
        var verify = perBT.SettingsForBuildTarget(BuildTargetGroup.Android);
        Debug.Log($"[VPS] Verification:");
        Debug.Log($"  General Settings: {(verify != null ? "OK" : "NULL")}");
        if (verify != null)
        {
            Debug.Log($"  Init on start: {verify.InitManagerOnStart}");
            var vmgr = verify.Manager;
            Debug.Log($"  Manager: {(vmgr != null ? "OK" : "NULL")}");
            if (vmgr != null)
            {
                Debug.Log($"  Active loaders: {vmgr.activeLoaders.Count}");
                foreach (var l in vmgr.activeLoaders)
                    Debug.Log($"    Loader: {l.GetType().Name}");
            }
        }
    }
}
