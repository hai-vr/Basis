using System.Collections.Generic;

namespace Basis.Setup.Modules
{
    public sealed class BasisLinkXmlSetup : BasisSetupModuleBase
    {
        public override string Key => "linker.linkxml";
        public override string DisplayName => "IL2CPP link.xml";
        public override string Category => "Linker";
        public override int Version => 1;

        public override IEnumerable<string> OwnedPaths => new[] { "Assets/Basis/link.xml" };
    }
}
