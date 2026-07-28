using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
public static class TemporaryStorageHandler
{
    public static string SavePrefabToTemporaryStorage(GameObject prefab, BasisAssetBundleObject settings, ref bool wasModified, out string uniqueID)
    {
        EnsureDirectoryExists(settings.TemporaryStorage);
        uniqueID = BasisGenerateUniqueID.GenerateUniqueID();
        string prefabPath = Path.Combine(settings.TemporaryStorage, $"{uniqueID}.prefab");
        prefab = PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        wasModified = true;
        return prefabPath;
    }
    // SaveScene lived here, but it handed the build the scene's original path while only the bundle
    // name was unique, which is what let two worlds built from one scene collide. Scenes are now
    // staged by BasisSceneBuildName instead.
    public static void EnsureDirectoryExists(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
    }
    public static void ClearTemporaryStorage(string tempStoragePath)
    {
        if (Directory.Exists(tempStoragePath))
        {
            Directory.Delete(tempStoragePath, true);
        }
    }
}
