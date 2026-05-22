using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

namespace Basis.Editor
{
    /// <summary>
    /// Creates Basis-owned Addressable groups with local-bundle defaults (LZ4,
    /// local build/load paths). A dedicated group is its own bundle, so loading a
    /// model or language table no longer pulls the shared Foundation/UI bundle
    /// resident.
    /// </summary>
    public static class BasisAddressableGroups
    {
        public static AddressableAssetGroup GetOrCreate(
            AddressableAssetSettings settings,
            string groupName,
            BundledAssetGroupSchema.BundlePackingMode packing)
        {
            AddressableAssetGroup group = settings.FindGroup(groupName);
            if (group == null)
            {
                group = settings.CreateGroup(groupName, false, false, false, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            }

            BundledAssetGroupSchema schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null)
            {
                schema = group.AddSchema<BundledAssetGroupSchema>();
            }
            if (group.GetSchema<ContentUpdateGroupSchema>() == null)
            {
                group.AddSchema<ContentUpdateGroupSchema>();
            }

            schema.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            schema.BundleMode = packing;
            schema.IncludeInBuild = true;
            schema.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            schema.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            return group;
        }
    }
}