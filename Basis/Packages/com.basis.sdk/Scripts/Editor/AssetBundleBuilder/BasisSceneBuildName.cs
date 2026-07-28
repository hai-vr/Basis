using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
/// <summary>
/// Gives the scene a unique file name for the duration of a bundle build, the same way every prefab
/// build gets a unique prefab name.
///
/// A scene bundle names its serialized files after the scene file ("BuildPlayer-MyWorld"), and Unity
/// refuses to load two bundles that contain identically named files, so two worlds built from one
/// scene can never both be loaded. Renaming rather than copying is deliberate: a rename keeps the
/// scene's asset guid, and Unity keys baked rendering data off that guid. A copy gets a fresh guid,
/// which silently unhooks the adaptive probe volume cell lookup on its baking set.
/// </summary>
public sealed class BasisSceneBuildName : IDisposable
{
    private const string PendingRestoreKey = "Basis.SceneBuildName.PendingRestore";

    public string ScenePath { get; private set; }
    public string UniqueID { get; private set; }

    private string BuildPath;
    private string OriginalName;

    /// <summary>
    /// Saves the scene and renames it for the build. Returns null if the scene cannot be staged, in
    /// which case nothing has been renamed.
    /// </summary>
    public static BasisSceneBuildName Assign(Scene scene)
    {
        if (!EditorSceneManager.SaveScene(scene))
        {
            Debug.LogError("Could not save the scene, it must be saved before it can be built.");
            return null;
        }
        string originalPath = scene.path;
        if (string.IsNullOrEmpty(originalPath))
        {
            Debug.LogError("The scene has no asset path, it must be saved to the project before it can be built.");
            return null;
        }

        string generatedID = BasisGenerateUniqueID.GenerateUniqueID();
        string error = AssetDatabase.RenameAsset(originalPath, generatedID);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"Could not give the scene a unique build name: {error}");
            return null;
        }

        BasisSceneBuildName buildName = new BasisSceneBuildName
        {
            OriginalName = Path.GetFileNameWithoutExtension(originalPath),
            BuildPath = $"{Path.GetDirectoryName(originalPath).Replace('\\', '/')}/{generatedID}.unity",
            UniqueID = generatedID
        };
        buildName.ScenePath = buildName.BuildPath;

        // The scene lives in the author's project while it is renamed, so leave a breadcrumb that
        // survives an editor crash and put the name back on the next startup if we never got to.
        EditorPrefs.SetString(PendingRestoreKey, $"{buildName.BuildPath}|{buildName.OriginalName}");
        return buildName;
    }

    public void Dispose()
    {
        if (BuildPath == null)
        {
            return;
        }
        Restore(BuildPath, OriginalName);
        BuildPath = null;
        EditorPrefs.DeleteKey(PendingRestoreKey);
    }

    [InitializeOnLoadMethod]
    private static void RestoreAfterInterruptedBuild()
    {
        string pending = EditorPrefs.GetString(PendingRestoreKey, string.Empty);
        EditorPrefs.DeleteKey(PendingRestoreKey);
        string[] parts = pending.Split('|');
        if (parts.Length == 2 && File.Exists(parts[0]))
        {
            Debug.LogWarning($"A scene bundle build was interrupted while {parts[1]} was renamed for building. Putting the name back.");
            Restore(parts[0], parts[1]);
        }
    }

    private static void Restore(string buildPath, string originalName)
    {
        string error = AssetDatabase.RenameAsset(buildPath, originalName);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogError($"Could not rename the scene back to {originalName}, it is still named after the build id at {buildPath}: {error}");
        }
    }
}
