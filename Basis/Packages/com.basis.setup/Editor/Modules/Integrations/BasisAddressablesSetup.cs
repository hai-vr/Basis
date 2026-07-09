using System.Collections.Generic;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;

namespace Basis.Setup.Modules
{
    public sealed class BasisAddressablesSetup : BasisSetupModuleBase
    {
        private const string SettingsPath = "Assets/AddressableAssetsData/AddressableAssetSettings.asset";

        public override string Key => "addressables.settings";
        public override string DisplayName => "Addressables Settings, Groups & Entries";
        public override string Category => "Addressables";
        public override int Version => 1;

        public override IEnumerable<string> OwnedPaths => new[] { "Assets/AddressableAssetsData" };

        protected override void Activate()
        {
            AddressableAssetSettings settings = AssetDatabase.LoadAssetAtPath<AddressableAssetSettings>(SettingsPath);
            if (settings != null)
            {
                AddressableAssetSettingsDefaultObject.Settings = settings;
            }
        }
    }
}
