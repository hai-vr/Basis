using Unity.XR.OpenVR;
using UnityEditor;

namespace Basis.Setup.Modules
{
    public sealed class BasisOpenVRSettingsSetup : BasisSetupModuleBase
    {
        private const string SettingsPath = "Assets/XR/Settings/OpenVRSettings.asset";
        private const string LoaderPath = "Assets/XR/Loaders/OpenVRLoader.asset";
        private const string ConfigKey = "Unity.XR.OpenVR.Settings";

        public override string Key => "openvr.settings";
        public override string DisplayName => "OpenVR Settings";
        public override string Category => "XR";
        public override int Version => 1;
        public override int Order => 5;

        public override System.Collections.Generic.IEnumerable<string> OwnedPaths => new[] { LoaderPath, SettingsPath };

        protected override bool Exists()
        {
            return base.Exists() && EditorBuildSettings.TryGetConfigObject(ConfigKey, out OpenVRSettings _);
        }

        protected override void Activate()
        {
            OpenVRSettings settings = AssetDatabase.LoadAssetAtPath<OpenVRSettings>(SettingsPath);
            if (settings != null)
            {
                EditorBuildSettings.AddConfigObject(ConfigKey, settings, true);
            }
        }
    }
}
