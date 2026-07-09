using UnityEditor;

namespace Basis.Setup.Modules
{
    /// <summary>
    /// Empty project folders Unity keeps by <c>.meta</c> but git can't carry. Recreated here so a fresh
    /// install matches the golden project's folder layout (these are the folders the old generator's
    /// stray deletions removed).
    /// </summary>
    public sealed class BasisProjectStructureSetup : BasisSetupModuleBase
    {
        private static readonly string[] Folders =
        {
            "Assets/Resources",
            "Assets/TemporaryStorage",
        };

        public override string Key => "project.structure";
        public override string DisplayName => "Project Folders (Resources, TemporaryStorage)";
        public override string Category => "Structure";
        public override int Version => 1;
        public override int Order => -10;

        protected override bool Exists()
        {
            for (int i = 0; i < Folders.Length; i++)
            {
                if (!AssetDatabase.IsValidFolder(Folders[i]))
                {
                    return false;
                }
            }

            return true;
        }

        protected override bool Build(BasisSetupMode mode)
        {
            bool changed = false;
            for (int i = 0; i < Folders.Length; i++)
            {
                if (!AssetDatabase.IsValidFolder(Folders[i]))
                {
                    BasisSetupIO.EnsureAssetFolder(Folders[i]);
                    changed = true;
                }
            }

            return changed;
        }
    }
}
