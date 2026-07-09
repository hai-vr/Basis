using System.Collections.Generic;

namespace Basis.Setup.Modules
{
    public sealed class BasisHighlightSetup : BasisSetupModuleBase
    {
        public override string Key => "highlight.settings";
        public override string DisplayName => "Highlight Settings (Desktop / Android)";
        public override string Category => "Highlight";
        public override int Version => 1;

        public override IEnumerable<string> OwnedPaths => new[] { "Assets/Basis/Settings/Highlight" };
    }
}
