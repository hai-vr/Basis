using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Basis.Setup
{
    public static class BasisSetupIO
    {
        private const string BackupRoot = "BasisSetupBackups";

        public static void ForceGuid(string assetPath, string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return;
            }

            if (AssetDatabase.AssetPathToGUID(assetPath) == guid)
            {
                return;
            }

            string metaFull = ToFullPath(assetPath) + ".meta";
            if (!File.Exists(metaFull))
            {
                return;
            }

            string meta = File.ReadAllText(metaFull);
            string patched = Regex.Replace(meta, "guid: [0-9a-fA-F]{32}", "guid: " + guid);
            if (patched != meta)
            {
                File.WriteAllText(metaFull, patched);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }
        }

        public static void EnsureAssetFolder(string assetFolder)
        {
            assetFolder = assetFolder.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
            {
                return;
            }

            string[] parts = assetFolder.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }

        public static void EnsureParentFolder(string assetPath)
        {
            int slash = assetPath.Replace('\\', '/').LastIndexOf('/');
            if (slash > 0)
            {
                EnsureAssetFolder(assetPath.Substring(0, slash));
            }
        }

        public static bool BackupIfExists(string assetPath)
        {
            string full = ToFullPath(assetPath);
            if (!File.Exists(full))
            {
                return false;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            string dest = Path.Combine(ProjectRoot(), BackupRoot, stamp, assetPath.Replace('\\', '/'));
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            File.Copy(full, dest, true);

            string meta = full + ".meta";
            if (File.Exists(meta))
            {
                File.Copy(meta, dest + ".meta", true);
            }

            return true;
        }

        public static bool WriteTextAsset(string assetPath, string content, BasisSetupMode mode)
        {
            string full = ToFullPath(assetPath);
            bool exists = File.Exists(full);

            if (exists)
            {
                if (mode == BasisSetupMode.EnsureExists)
                {
                    return false;
                }

                if (File.ReadAllText(full) == content)
                {
                    return false;
                }

                BackupIfExists(assetPath);
            }

            EnsureParentFolder(assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        public static bool WriteRawFile(string projectRelativePath, string content, BasisSetupMode mode)
        {
            string full = Path.Combine(ProjectRoot(), projectRelativePath);
            bool exists = File.Exists(full);

            if (exists)
            {
                if (mode == BasisSetupMode.EnsureExists)
                {
                    return false;
                }

                if (File.ReadAllText(full) == content)
                {
                    return false;
                }

                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string dest = Path.Combine(ProjectRoot(), BackupRoot, stamp, projectRelativePath.Replace('\\', '/'));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(full, dest, true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full));
            File.WriteAllText(full, content);
            return true;
        }

        public static T CreateOrUpdateAsset<T>(string assetPath, BasisSetupMode mode, Action<T> configure, out bool changed)
            where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null && mode == BasisSetupMode.EnsureExists)
            {
                changed = false;
                return asset;
            }

            if (asset != null && mode == BasisSetupMode.Update)
            {
                BackupIfExists(assetPath);
                configure(asset);
                EditorUtility.SetDirty(asset);
                changed = true;
                return asset;
            }

            EnsureParentFolder(assetPath);
            asset = ScriptableObject.CreateInstance<T>();
            configure(asset);
            AssetDatabase.CreateAsset(asset, assetPath);
            changed = true;
            return asset;
        }

        public static bool ProjectPathExists(string projectRelPath)
        {
            string full = ToFullPath(projectRelPath);
            return File.Exists(full) || Directory.Exists(full);
        }

        /// <summary>
        /// Mirror a file or folder (with its <c>.meta</c>) from <paramref name="srcFull"/> to
        /// <paramref name="dstFull"/>, replacing whatever is there. Returns the number of files copied.
        /// </summary>
        public static int MirrorWithMeta(string srcFull, string dstFull)
        {
            int count = 0;
            if (Directory.Exists(srcFull))
            {
                if (Directory.Exists(dstFull))
                {
                    Directory.Delete(dstFull, true);
                }

                count += CopyDirectory(srcFull, dstFull);
            }
            else if (File.Exists(srcFull))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dstFull));
                File.Copy(srcFull, dstFull, true);
                count++;
            }
            else
            {
                return 0;
            }

            string srcMeta = srcFull + ".meta";
            if (File.Exists(srcMeta))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dstFull));
                File.Copy(srcMeta, dstFull + ".meta", true);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Copy a baked template (file or folder) into the project. <see cref="BasisSetupMode.EnsureExists"/>
        /// leaves an existing target untouched; <see cref="BasisSetupMode.Update"/> backs it up then replaces
        /// it wholesale so the result matches the template exactly. Returns true if the project changed.
        /// </summary>
        public static bool ApplyFromTemplate(string srcFull, string dstFull, string projectRelPath, BasisSetupMode mode)
        {
            if (!File.Exists(srcFull) && !Directory.Exists(srcFull))
            {
                return false;
            }

            bool dstExists = File.Exists(dstFull) || Directory.Exists(dstFull);
            if (dstExists)
            {
                if (mode == BasisSetupMode.EnsureExists)
                {
                    return false;
                }

                BackupPathIfExists(projectRelPath);
                DeletePath(dstFull);
            }

            MirrorWithMeta(srcFull, dstFull);
            return true;
        }

        public static void DeletePath(string full)
        {
            if (Directory.Exists(full))
            {
                Directory.Delete(full, true);
            }
            else if (File.Exists(full))
            {
                File.Delete(full);
            }

            string meta = full + ".meta";
            if (File.Exists(meta))
            {
                File.Delete(meta);
            }
        }

        public static bool BackupPathIfExists(string projectRelPath)
        {
            string full = ToFullPath(projectRelPath);
            if (Directory.Exists(full))
            {
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                string dest = Path.Combine(ProjectRoot(), BackupRoot, stamp, projectRelPath.Replace('\\', '/'));
                CopyDirectory(full, dest);
                string meta = full + ".meta";
                if (File.Exists(meta))
                {
                    File.Copy(meta, dest + ".meta", true);
                }

                return true;
            }

            return BackupIfExists(projectRelPath);
        }

        private static int CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            int count = 0;
            foreach (string file in Directory.GetFiles(src))
            {
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), true);
                count++;
            }

            foreach (string dir in Directory.GetDirectories(src))
            {
                count += CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
            }

            return count;
        }

        public static string ToFullPath(string assetPath)
        {
            return Path.Combine(ProjectRoot(), assetPath.Replace('/', Path.DirectorySeparatorChar));
        }

        public static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
    }
}
