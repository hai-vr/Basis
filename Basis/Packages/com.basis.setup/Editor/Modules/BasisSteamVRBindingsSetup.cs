using System.Collections.Generic;

namespace Basis.Setup.Modules
{
    public sealed class BasisSteamVRBindingsSetup : BasisSetupModuleBase
    {
        public override string Key => "steamvr.bindings";
        public override string DisplayName => "SteamVR Action & Binding Files";
        public override string Category => "SteamVR";
        public override int Version => 1;

        public override IEnumerable<string> OwnedPaths => new[] { "Assets/StreamingAssets/SteamVR" };
    }
}
