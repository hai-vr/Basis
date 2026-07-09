using System.Collections.Generic;
using UnityEditor;
using UnityEngine.Rendering;

namespace Basis.Setup.Modules
{
    public sealed class BasisRenderingDefaultsSetup : BasisSetupModuleBase
    {
        private const string Folder = "Assets/Basis/Settings/Unity Rendering Defaults";
        private const string DefaultProfilePath = Folder + "/DefaultVolumeProfile.asset";

        public override string Key => "rendering.defaults";
        public override string DisplayName => "URP Renderers, Volume Profiles & Lighting";
        public override string Category => "Rendering";
        public override int Version => 1;
        public override int Order => 10;

        public override IEnumerable<string> OwnedPaths => new[] { Folder };

        protected override void Activate()
        {
            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DefaultProfilePath);
            if (profile == null)
            {
                return;
            }

            try
            {
                if (VolumeManager.instance.globalDefaultProfile == null)
                {
                    VolumeManager.instance.SetGlobalDefaultProfile(profile);
                }
            }
            catch
            {
            }
        }
    }
}
