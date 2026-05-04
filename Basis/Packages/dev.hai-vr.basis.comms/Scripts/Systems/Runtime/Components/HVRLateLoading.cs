using System;
using Basis.Scripts.BasisSdk;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Networking;
using UnityEngine;

namespace HVR.Basis.Comms
{
    public class HVRLateLoading : MonoBehaviour
    {
        public MeshRenderer[] meshRenderers = Array.Empty<MeshRenderer>();
        public SkinnedMeshRenderer[] skinnedMeshRenderers = Array.Empty<SkinnedMeshRenderer>();
        public string meshAssetKey;
        public string[] materialAssetKeys = Array.Empty<string>();

        public async void Start()
        {
            var avatar = HVRCommsUtil.GetAvatar(this);
            if (avatar == null)
            {
                BasisDebug.LogError("Avatar not found, late loading will not continue.");
                return;
            }

            if (avatar.BundleAdditionalAssets is not { deferredAssets: { Length: > 0 } })
            {
                BasisDebug.LogError("Avatar has no deferred assets, late loading will not continue.");
                return;
            }

            if (!TryGetPlayer(avatar, out var player))
            {
                BasisDebug.LogError("Could not find player, late loading will not continue.");
                return;
            }

            if (!TryFindKey(avatar.BundleAdditionalAssets, meshAssetKey, out var deferredMesh))
            {
                BasisDebug.LogError($"Deferred asset for mesh {meshAssetKey} not found, late loading will not continue.");
                return;
            }

            var assetMesh = deferredMesh.asset != null
                ? deferredMesh.asset
                : (await BasisLoadHandler.LoadAdditionalAssetInAlreadyLoadedBundle(player.AvatarMetaData, deferredMesh.assetPath, false));
            if (assetMesh is not Mesh mesh)
            {
                BasisDebug.LogError("Deferred asset is not a mesh, late loading will not continue.");
                return;
            }

            foreach (var meshRenderer in meshRenderers)
            {
                if (null == meshRenderer) continue;

                var meshFilter = meshRenderer.GetComponent<MeshFilter>();
                if (meshFilter != null)
                {
                    meshFilter.sharedMesh = mesh;
                }
            }
            foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
            {
                if (null != skinnedMeshRenderer)
                {
                    skinnedMeshRenderer.sharedMesh = mesh;
                }
            }

            for (var materialIndex = 0; materialIndex < materialAssetKeys.Length; materialIndex++)
            {
                var materialAssetKey = materialAssetKeys[materialIndex];
                if (!TryFindKey(avatar.BundleAdditionalAssets, materialAssetKey, out var deferredMaterial))
                {
                    BasisDebug.LogError($"Deferred asset for material {materialAssetKey} not found, will continue anyway.");
                    continue;
                }

                var assetMaterial = deferredMaterial.asset != null
                    ? deferredMaterial.asset
                    : await BasisLoadHandler.LoadAdditionalAssetInAlreadyLoadedBundle(player.AvatarMetaData, deferredMaterial.assetPath, true);
                if (assetMaterial is not Material material)
                {
                    BasisDebug.LogError("Deferred asset is not a mesh. Will continue anyway.");
                    continue;
                }

                BasisDebug.Log($"Late loading material {material.name} for mesh {mesh.name} at index {materialIndex}.");
                foreach (var meshRenderer in meshRenderers)
                {
                    if (null == meshRenderer) continue;

                    if (materialIndex < meshRenderer.sharedMaterials.Length)
                    {
                        var sharedMaterials = meshRenderer.sharedMaterials;
                        sharedMaterials[materialIndex] = material;
                        meshRenderer.sharedMaterials = sharedMaterials;
                    }
                }
                foreach (var skinnedMeshRenderer in skinnedMeshRenderers)
                {
                    if (null == skinnedMeshRenderer) continue;

                    if (materialIndex < skinnedMeshRenderer.sharedMaterials.Length)
                    {
                        var sharedMaterials = skinnedMeshRenderer.sharedMaterials;
                        sharedMaterials[materialIndex] = material;
                        skinnedMeshRenderer.sharedMaterials = sharedMaterials;
                    }
                }
            }
        }

        private bool TryFindKey(BasisBundleAdditionalAssets additionalAssets, string assetKey, out BasisBundleAdditionalAsset result)
        {
            foreach (var deferredAsset in additionalAssets.deferredAssets)
            {
                if (deferredAsset != null && deferredAsset.key == assetKey)
                {
                    result = deferredAsset;
                    return true;
                }
            }

            result = null;
            return false;
        }

        private static bool TryGetPlayer(BasisAvatar avatar, out BasisPlayer player)
        {
            if (avatar.IsOwnedLocally)
            {
                player = BasisLocalPlayer.Instance;
                return true;
            }

            if (avatar.TryGetLinkedPlayer(out var playerId) && BasisNetworkPlayers.GetPlayerById(playerId, out var networkPlayer))
            {
                player = networkPlayer.Player;
                return true;
            }

            player = null;
            return false;
        }
    }
}
