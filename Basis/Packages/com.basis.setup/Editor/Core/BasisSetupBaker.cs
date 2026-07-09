using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Basis.Setup
{
    /// <summary>
    /// Maintainer-side: snapshots the current project's owned config into the package's <c>Templates~</c>
    /// store. Run this in the golden project (the monorepo) whenever the canonical defaults change, then
    /// commit the package. Consumers never bake — they only install/update from the shipped templates.
    /// </summary>
    public static class BasisSetupBaker
    {
        [MenuItem("Basis/Setup/Bake Templates From Project", false, 40)]
        public static void BakeMenu()
        {
            if (!BasisSetupTemplates.IsWritable())
            {
                EditorUtility.DisplayDialog(
                    "Basis Setup — Bake",
                    "com.basis.setup is resolved read-only from the PackageCache:\n" +
                    BasisSetupTemplates.SourceDescription() + "\n\n" +
                    "To bake, consume it as an embedded package (under Packages/) or via a local file: reference so Templates~ is writable.",
                    "OK");
                return;
            }

            int owned = 0;
            foreach (IBasisSetupModule module in BasisSetupRegistry.Modules)
            {
                foreach (string unused in module.OwnedPaths)
                {
                    owned++;
                }
            }

            bool go = EditorUtility.DisplayDialog(
                "Basis Setup — Bake",
                "Snapshot this project's current configuration into the package template store?\n\n" +
                "Destination: " + BasisSetupTemplates.TemplatesRoot() + "\n\n" +
                owned + " owned path(s) will be copied verbatim, overwriting the existing templates. Commit the package afterward to publish the new defaults.",
                "Bake", "Cancel");
            if (go)
            {
                BakeAll();
            }
        }

        public static void BakeAll()
        {
            AssetDatabase.SaveAssets();

            var baked = new List<string>();
            var missing = new List<string>();
            int files = 0;

            foreach (IBasisSetupModule module in BasisSetupRegistry.Modules)
            {
                foreach (string path in module.OwnedPaths)
                {
                    if (!BasisSetupIO.ProjectPathExists(path))
                    {
                        missing.Add(path);
                        continue;
                    }

                    files += BasisSetupTemplates.CopyIntoTemplates(path);
                    baked.Add(path);
                }
            }

            WriteManifest(baked);

            var sb = new StringBuilder();
            sb.AppendLine($"[BasisSetup] Baked {baked.Count} path(s) / {files} file(s) into {BasisSetupTemplates.TemplatesRoot()}");
            if (missing.Count > 0)
            {
                sb.AppendLine("Owned paths not present in this project (skipped — nothing to snapshot):");
                for (int i = 0; i < missing.Count; i++)
                {
                    sb.AppendLine("  " + missing[i]);
                }
            }

            if (missing.Count > 0)
            {
                Debug.LogWarning(sb.ToString());
            }
            else
            {
                Debug.Log(sb.ToString());
            }
        }

        private static void WriteManifest(List<string> baked)
        {
            string root = BasisSetupTemplates.TemplatesRoot();
            Directory.CreateDirectory(root);
            baked.Sort(StringComparer.Ordinal);
            File.WriteAllText(Path.Combine(root, "baked-paths.txt"), string.Join("\n", baked) + "\n");
        }
    }
}
