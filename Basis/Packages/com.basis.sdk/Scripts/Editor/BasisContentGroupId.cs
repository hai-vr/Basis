using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// Ensures a piece of content carries a stable <see cref="BasisBundleDescription.ContentGroupId"/>
/// before it is built, generating and persisting one on the authored asset the first time. The id
/// must survive rebuilds, so it is written to the source prefab when the content is a prefab
/// instance; plain scene objects persist it with the scene instead.
/// </summary>
public static class BasisContentGroupId
{
    public static void EnsurePersistent(BasisContentBase content)
    {
        if (content == null || content.BasisBundleDescription == null) return;
        if (!string.IsNullOrEmpty(content.BasisBundleDescription.ContentGroupId)) return;

        if (PrefabUtility.IsPartOfPrefabInstance(content))
        {
            BasisContentBase source = PrefabUtility.GetCorrespondingObjectFromSource(content);
            if (source != null && source.BasisBundleDescription != null && !PrefabUtility.IsPartOfImmutablePrefab(source))
            {
                if (string.IsNullOrEmpty(source.BasisBundleDescription.ContentGroupId))
                {
                    source.BasisBundleDescription.ContentGroupId = BasisGenerateUniqueID.GenerateUniqueID();
                    EditorUtility.SetDirty(source);
                    AssetDatabase.SaveAssetIfDirty(source);
                }

                content.BasisBundleDescription.ContentGroupId = source.BasisBundleDescription.ContentGroupId;
                return;
            }
        }

        content.BasisBundleDescription.ContentGroupId = BasisGenerateUniqueID.GenerateUniqueID();
        EditorUtility.SetDirty(content);

        if (EditorUtility.IsPersistent(content))
        {
            AssetDatabase.SaveAssetIfDirty(content);
        }
        else if (content.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(content.gameObject.scene);
        }
    }
}
