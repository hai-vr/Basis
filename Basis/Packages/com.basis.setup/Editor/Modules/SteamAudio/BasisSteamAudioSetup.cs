using System.Collections.Generic;

namespace Basis.Setup.Modules
{
    public sealed class BasisSteamAudioSetup : BasisSetupModuleBase
    {
        public override string Key => "steamaudio.settings";
        public override string DisplayName => "Steam Audio Settings & Materials";
        public override string Category => "Steam Audio";
        public override int Version => 1;

        public override IEnumerable<string> OwnedPaths => new[] { "Assets/Basis/Settings/Resources" };
    }
}
