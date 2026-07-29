using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Basis.Scripts.BasisSdk;
using GLTFast;
using GLTFast.Export;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the platform-agnostic "Generic" bee section for avatars: the avatar exported as a
/// glTF 2.0 binary (.glb, an open standard glTFast can import at runtime on any platform),
/// encrypted with the same bundle password as the AssetBundle sections. Clients that have no
/// AssetBundle section for their platform fall back to this section instead of failing the
/// load. The humanoid rig and BasisAvatar wiring that glTF cannot express travel as
/// <see cref="BasisGenericAvatarData"/> JSON on the generated section entry.
/// </summary>
public static class BasisGenericAvatarExporter
{
    /// <summary>
    /// Returns the section entry plus the encrypted payload path (staged next to the
    /// per-platform bundles, consumed by the bee combine step). Returns (null, null) when the
    /// avatar cannot be represented (no humanoid animator) — the caller builds without the
    /// generic section, which is exactly the pre-feature behavior.
    /// </summary>
    public static async Task<(BasisBundleGenerated Generated, string EncryptedPath)> ExportEncryptedGlb(BasisAvatar sourceAvatar, BasisAssetBundleObject settings, string password, string stagingDirectory)
    {
        GameObject clone = Object.Instantiate(sourceAvatar.gameObject);
        try
        {
            clone.name = sourceAvatar.gameObject.name;
            BasisAssetBundlePipeline.DestroyEditorOnlyInAvatar(clone);
            BasisAssetBundlePipeline.PostProcessAvatar(clone);
            // Skeleton rebuild and mesh lookup on the importing client are name-based, and
            // glTF nodes have no other stable identity — names must be unique before capture.
            EnsureUniqueTransformNames(clone.transform);
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            if (!clone.TryGetComponent(out BasisAvatar cloneAvatar))
            {
                BasisDebug.LogError("Generic avatar export skipped: clone lost its BasisAvatar component.");
                return (null, null);
            }

            BasisGenericAvatarData avatarData = BasisGenericAvatarData.Capture(cloneAvatar);
            if (avatarData == null)
            {
                return (null, null);
            }

            byte[] glbBytes = await ExportGlb(clone);
            if (glbBytes == null || glbBytes.Length == 0)
            {
                BasisDebug.LogError("Generic avatar export produced no glb data.");
                return (null, null);
            }

            string uniqueId = BasisGenerateUniqueID.GenerateUniqueID();
            var basisPassword = new BasisEncryptionWrapper.BasisPassword { VP = password };
            BasisProgressReport report = new BasisProgressReport();
            byte[] encrypted = await BasisEncryptionWrapper.EncryptToBytesAsync(uniqueId, basisPassword, glbBytes, report);
            if (encrypted == null || encrypted.Length == 0)
            {
                BasisDebug.LogError("Generic avatar export failed to encrypt the glb payload.");
                return (null, null);
            }

            Directory.CreateDirectory(stagingDirectory);
            string encryptedPath = Path.Combine(stagingDirectory, $"{uniqueId}{settings.BasisBundleEncryptedExtension}");
            await File.WriteAllBytesAsync(encryptedPath, encrypted);

            BasisBundleGenerated generated = new BasisBundleGenerated(
                Hash128.Compute(glbBytes).ToString(),
                BasisBundleConnector.GltfAssetMode,
                clone.name + ".glb",
                0,
                true,
                password,
                BasisBundleConnector.GenericPlatform,
                encrypted.LongLength)
            {
                GenericAvatarDataJson = avatarData.ToJson(),
            };

            BasisDebug.Log($"Generic (glTF) avatar section: glb {glbBytes.LongLength} bytes, encrypted {encrypted.LongLength} bytes.");
            return (generated, encryptedPath);
        }
        finally
        {
            if (clone != null)
            {
                Object.DestroyImmediate(clone);
            }
        }
    }

    private static async Task<byte[]> ExportGlb(GameObject clone)
    {
        var exportSettings = new ExportSettings
        {
            Format = GltfFormat.Binary,
            ImageDestination = ImageDestination.MainBuffer,
            // Meshes/materials only — cameras, lights and animation have no place in an
            // avatar fallback and would only widen the payload.
            ComponentMask = ComponentType.Mesh,
        };
        // Inactive nodes and disabled renderers must still be exported: they carry bones and
        // hidden outfit meshes. Their authored off-state is restored after import from
        // BasisGenericAvatarData, since glTF has no active/enabled concept.
        var gameObjectSettings = new GameObjectExportSettings
        {
            OnlyActiveInHierarchy = false,
            DisabledComponents = true,
        };
        GameObjectExport export = new GameObjectExport(exportSettings, gameObjectSettings);
        // The scene name becomes a wrapper GameObject on import; it must not collide with the
        // avatar root node's name, which the loader locates by name.
        if (!export.AddScene(new[] { clone }, "BasisGenericScene"))
        {
            BasisDebug.LogError("Generic avatar export: AddScene failed.");
            return null;
        }
        using MemoryStream stream = new MemoryStream();
        bool success = await export.SaveToStreamAndDispose(stream);
        if (!success)
        {
            BasisDebug.LogError("Generic avatar export: glb serialization failed.");
            return null;
        }
        return stream.ToArray();
    }

    public static void EnsureUniqueTransformNames(Transform root)
    {
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
        Stack<Transform> pending = new Stack<Transform>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            Transform current = pending.Pop();
            // Slashes would corrupt the Transform.Find paths recorded for viseme meshes and
            // node state.
            if (current.name.IndexOf('/') >= 0)
            {
                current.name = current.name.Replace('/', '_');
            }
            if (!seen.Add(current.name))
            {
                int suffix = 2;
                string candidate;
                do
                {
                    candidate = current.name + "_" + suffix;
                    suffix++;
                } while (!seen.Add(candidate));
                current.name = candidate;
            }
            int childCount = current.childCount;
            for (int Index = 0; Index < childCount; Index++)
            {
                pending.Push(current.GetChild(Index));
            }
        }
    }
}
