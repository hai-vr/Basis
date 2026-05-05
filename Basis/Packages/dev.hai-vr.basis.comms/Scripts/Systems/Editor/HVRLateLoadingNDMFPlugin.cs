#if HVR_NDMF_IS_INSTALLED
using System;
using System.Collections.Generic;
using System.Linq;
using Basis.Scripts.BasisSdk;
using HVR.Basis.Comms.Editor;
using nadena.dev.ndmf;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(HVRLateLoadingNDMFPlugin))]
namespace HVR.Basis.Comms.Editor
{
    [RunsOnPlatforms("org.basisvr.basis-framework")]
    public class HVRLateLoadingNDMFPlugin : Plugin<HVRLateLoadingNDMFPlugin>
    {
        protected override void Configure()
        {
            InPhase(BuildPhase.Optimizing)
                .Run("Add late loading", AddLateLoading);
        }

        private void AddLateLoading(BuildContext context)
        {
            var lateLoadingComponents = context.AvatarRootObject.GetComponentsInChildren<HVRLateLoading>(true);
            if (lateLoadingComponents.Length == 0) return;

            var applicableRenderers = lateLoadingComponents
                .SelectMany(lateLoading => lateLoading.GetComponentsInChildren<Renderer>(true))
                .Where(renderer => renderer is SkinnedMeshRenderer or MeshRenderer)
                .Distinct()
                .Where(renderer => !renderer.gameObject.activeInHierarchy)
                .ToList();
            if (applicableRenderers.Count == 0) return;

            foreach (var lateLoadingComponent in lateLoadingComponents)
            {
                Object.DestroyImmediate(lateLoadingComponent);
            }

            var applicableMeshes = applicableRenderers
                .Select(renderer => renderer is SkinnedMeshRenderer smr ? smr.sharedMesh : renderer.GetComponent<MeshFilter>().sharedMesh)
                .Where(mesh => mesh != null)
                .Distinct()
                .ToList();
            var applicableMaterials = applicableRenderers
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .Distinct()
                .ToList();

            var meshToAdditional = new Dictionary<Mesh, BasisBundleAdditionalAsset>();
            for (var index = 0; index < applicableMeshes.Count; index++)
            {
                var mesh = applicableMeshes[index];
                meshToAdditional.Add(mesh, new BasisBundleAdditionalAsset
                {
                    key = $"HVR.Basis.Comms.HVRLateLoading.Mesh-{index}",
                    asset = mesh,
                });
            }

            var materialToAdditional = new Dictionary<Material, BasisBundleAdditionalAsset>();
            for (var index = 0; index < applicableMaterials.Count; index++)
            {
                var material = applicableMaterials[index];
                materialToAdditional.Add(material, new BasisBundleAdditionalAsset
                {
                    key = $"HVR.Basis.Comms.HVRLateLoading.Material-{index}",
                    asset = material,
                });
            }

            foreach (var renderer in applicableRenderers)
            {
                var lateLoading = renderer.gameObject.AddComponent<HVRLateLoading>();

                var sharedMaterials = renderer.sharedMaterials;
                lateLoading.materialAssetKeys = sharedMaterials
                    .Select(material => materialToAdditional[material]?.key ?? "")
                    .ToArray();
                for (var index = 0; index < sharedMaterials.Length; index++)
                {
                    sharedMaterials[index] = null;
                }
                renderer.sharedMaterials = sharedMaterials;

                if (renderer is SkinnedMeshRenderer smr)
                {
                    var mesh = smr.sharedMesh;
                    var meshAssetKey = meshToAdditional[mesh]?.key;
                    lateLoading.meshAssetKey = meshAssetKey ?? "";
                    smr.sharedMesh = null;

                    lateLoading.skinnedMeshRenderers = new[] { smr };
                }
                else
                {
                    var meshFilter = renderer.GetComponent<MeshFilter>();
                    var mesh = meshFilter.sharedMesh;
                    var meshAssetKey = meshToAdditional[mesh]?.key;
                    lateLoading.meshAssetKey = meshAssetKey ?? "";
                    meshFilter.sharedMesh = null;

                    lateLoading.meshRenderers = new[] { renderer as MeshRenderer };
                }
            }

            var avatar = context.AvatarRootObject.GetComponent<BasisAvatar>();
            EnsureInitialized(avatar);

            avatar.BundleAdditionalAssets.deferredAssets = avatar.BundleAdditionalAssets.deferredAssets
                .Concat(meshToAdditional.Values)
                .Concat(materialToAdditional.Values)
                .ToArray();
        }

        private static void EnsureInitialized(BasisAvatar avatar)
        {
            if (avatar.BundleAdditionalAssets == null)
            {
                avatar.BundleAdditionalAssets = new BasisBundleAdditionalAssets
                {
                    deferredAssets = Array.Empty<BasisBundleAdditionalAsset>()
                };
            }
            else if (avatar.BundleAdditionalAssets.deferredAssets == null)
            {
                avatar.BundleAdditionalAssets.deferredAssets = Array.Empty<BasisBundleAdditionalAsset>();
            }
        }
    }
}
#endif
