using System.IO;
using UnityEditor.PackageManager;

namespace Basis.Setup
{
    /// <summary>
    /// Resolves the package's <c>Templates~</c> store (the trailing <c>~</c> keeps Unity from importing it,
    /// so the canonical <c>.asset</c>/<c>.meta</c> files sit there untouched) and copies verbatim in both
    /// directions: project → templates (bake) and templates → project (install/update).
    /// </summary>
    public static class BasisSetupTemplates
    {
        public const string PackageName = "com.basis.setup";
        public const string TemplatesFolder = "Templates~";

        private static PackageInfo Package => PackageInfo.FindForAssembly(typeof(BasisSetupTemplates).Assembly);

        public static string PackageRoot()
        {
            PackageInfo info = Package;
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
            {
                return info.resolvedPath;
            }

            return Path.GetFullPath(Path.Combine("Packages", PackageName));
        }

        public static string TemplatesRoot()
        {
            return Path.Combine(PackageRoot(), TemplatesFolder);
        }

        /// <summary>
        /// Bake needs to write into the package. Only possible when the package is embedded under
        /// <c>Packages/</c> or referenced via a local <c>file:</c> path — never from the read-only
        /// PackageCache copy a git/registry dependency resolves to.
        /// </summary>
        public static bool IsWritable()
        {
            PackageInfo info = Package;
            if (info == null)
            {
                return true;
            }

            return info.source == PackageSource.Embedded || info.source == PackageSource.Local;
        }

        public static string SourceDescription()
        {
            PackageInfo info = Package;
            return info == null ? "(unresolved)" : $"{info.source} @ {info.resolvedPath}";
        }

        public static string TemplatePathFor(string projectRelPath)
        {
            return Path.Combine(TemplatesRoot(), projectRelPath.Replace('/', Path.DirectorySeparatorChar));
        }

        public static bool TemplateExists(string projectRelPath)
        {
            string full = TemplatePathFor(projectRelPath);
            return File.Exists(full) || Directory.Exists(full);
        }

        /// <summary>Bake: copy an owned project path (file or folder, plus <c>.meta</c>) into the template store.</summary>
        public static int CopyIntoTemplates(string projectRelPath)
        {
            string src = BasisSetupIO.ToFullPath(projectRelPath);
            string dst = TemplatePathFor(projectRelPath);
            return BasisSetupIO.MirrorWithMeta(src, dst);
        }

        /// <summary>Install/update: copy the template for an owned path back into the project, honouring <paramref name="mode"/>.</summary>
        public static bool CopyFromTemplates(string projectRelPath, BasisSetupMode mode)
        {
            string src = TemplatePathFor(projectRelPath);
            string dst = BasisSetupIO.ToFullPath(projectRelPath);
            return BasisSetupIO.ApplyFromTemplate(src, dst, projectRelPath, mode);
        }
    }
}
