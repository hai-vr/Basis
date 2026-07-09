using System.Collections.Generic;

namespace Basis.Setup.Modules
{
    public sealed class BasisAndroidPluginsSetup : BasisSetupModuleBase
    {
        public override string Key => "android.plugins";
        public override string DisplayName => "Android Manifest & Gradle Templates";
        public override string Category => "Android";
        public override int Version => 1;

        public override IEnumerable<string> OwnedPaths => new[] { "Assets/Plugins/Android" };
    }
}
