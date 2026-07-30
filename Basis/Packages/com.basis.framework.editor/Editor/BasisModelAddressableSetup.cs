using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace Basis.Editor
{
    /// <summary>
    /// Moves Basis ML model assets into dedicated Addressable groups so each is
    /// bundled apart from the shared Foundation/UI content. MediaPipe task models
    /// pack separately (face/hand/pose load and release independently); OpenLipSync's
    /// model + config pack together. Runs from the menu and from an importer that
    /// watches the MediaPipe models folder.
    /// </summary>
    public static class BasisModelAddressableSetup
    {
        private const string MediaPipeFolder = "Packages/com.basis.mediapipe/Models";
        private const string MediaPipeGroup = "Basis MediaPipe Models";
        private const string OpenLipSyncPrefix = "Packages/com.basisvr.openlipsync/";
        private const string OpenLipSyncGroup = "Basis OpenLipSync";

        [MenuItem("Basis/Build/Addressables/Organize Model Groups", false, 341)]
        public static void OrganizeMenu()
        {
            int moved = Organize(logEach: true);
            if (moved > 0)
            {
                AssetDatabase.SaveAssets();
                Debug.Log($"[BasisAddressables] Moved {moved} model asset(s) into dedicated groups. Rebuild Addressables content (Window > Asset Management > Addressables > Groups > Build > New Build > Default Build Script) for player builds.");
            }
            else
            {
                Debug.Log("[BasisAddressables] Model assets already organized.");
            }
        }

        private static int Organize(bool logEach)
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
            {
                Debug.LogError("[BasisAddressables] AddressableAssetSettings not found. Open Window > Asset Management > Addressables > Groups to create one.");
                return 0;
            }

            int moved = 0;
            moved += RegisterFolder(settings, MediaPipeFolder, MediaPipeGroup,
                BundledAssetGroupSchema.BundlePackingMode.PackSeparately, logEach);
            moved += MoveByAddressPrefix(settings, OpenLipSyncPrefix, OpenLipSyncGroup,
                BundledAssetGroupSchema.BundlePackingMode.PackTogether, logEach);

            if (moved > 0)
            {
                settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            }
            return moved;
        }

        private static int RegisterFolder(AddressableAssetSettings settings, string folder,
            string groupName, BundledAssetGroupSchema.BundlePackingMode packing, bool logEach)
        {
            if (!Directory.Exists(folder))
            {
                return 0;
            }

            List<string> guids = new List<string>();
            string[] files = Directory.GetFiles(folder, "*.bytes");
            for (int i = 0; i < files.Length; i++)
            {
                string unityPath = files[i].Replace('\\', '/');
                if (!unityPath.EndsWith(".bytes", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                string guid = AssetDatabase.AssetPathToGUID(unityPath);
                if (!string.IsNullOrEmpty(guid))
                {
                    guids.Add(guid);
                }
            }

            return MoveGuids(settings, guids, groupName, packing, logEach);
        }

        private static int MoveByAddressPrefix(AddressableAssetSettings settings, string addressPrefix,
            string groupName, BundledAssetGroupSchema.BundlePackingMode packing, bool logEach)
        {
            List<string> guids = new List<string>();
            foreach (AddressableAssetGroup g in settings.groups)
            {
                if (g == null)
                {
                    continue;
                }
                foreach (AddressableAssetEntry e in g.entries)
                {
                    if (e != null && !string.IsNullOrEmpty(e.address)
                        && e.address.StartsWith(addressPrefix, StringComparison.Ordinal))
                    {
                        guids.Add(e.guid);
                    }
                }
            }

            return MoveGuids(settings, guids, groupName, packing, logEach);
        }

        private static int MoveGuids(AddressableAssetSettings settings, List<string> guids,
            string groupName, BundledAssetGroupSchema.BundlePackingMode packing, bool logEach)
        {
            if (guids.Count == 0)
            {
                return 0;
            }

            AddressableAssetGroup group = BasisAddressableGroups.GetOrCreate(settings, groupName, packing);
            int moved = 0;
            for (int i = 0; i < guids.Count; i++)
            {
                AddressableAssetEntry existing = settings.FindAssetEntry(guids[i]);
                if (existing != null && existing.parentGroup == group)
                {
                    continue;
                }

                AddressableAssetEntry entry = settings.CreateOrMoveEntry(guids[i], group, false, false);
                if (entry == null)
                {
                    continue;
                }

                moved++;
                if (logEach)
                {
                    Debug.Log($"[BasisAddressables] {entry.address} -> \"{groupName}\"");
                }
            }
            return moved;
        }

        private class Importer : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] importedAssets, string[] deletedAssets,
                string[] movedAssets, string[] movedFromAssetPaths)
            {
                if (Touches(importedAssets) || Touches(movedAssets))
                {
                    Organize(logEach: false);
                }
            }

            private static bool Touches(string[] paths)
            {
                if (paths == null)
                {
                    return false;
                }
                for (int i = 0; i < paths.Length; i++)
                {
                    string p = paths[i];
                    if (!string.IsNullOrEmpty(p)
                        && p.Replace('\\', '/').StartsWith(MediaPipeFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                return false;
            }
        }
    }
}