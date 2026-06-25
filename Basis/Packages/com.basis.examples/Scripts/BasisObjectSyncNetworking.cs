using UnityEngine;

/// <summary>
/// DEPRECATED compatibility shim — do not add this to new content.
/// The networked pickup is now <see cref="BasisPickupSyncNetworking"/> (it was moved into the framework
/// and renamed). Legacy content bundles serialized their pickup component as "BasisObjectSyncNetworking"
/// in this (BasisExamples) assembly, so this concrete subclass lets that old content resolve to a real
/// type and behave as a modern synced pickup instead of coming in as a missing script.
/// </summary>
[System.Obsolete("BasisObjectSyncNetworking is deprecated — use BasisPickupSyncNetworking. This shim only exists so legacy content bundles keep loading.")]
public class BasisObjectSyncNetworking : BasisPickupSyncNetworking
{
    protected override void Awake()
    {
        BasisDebug.LogWarningOnce("Legacy content using the deprecated 'BasisObjectSyncNetworking' component was loaded. It still works as a pickup through the compatibility shim, but the content should be re-exported with BasisPickupSyncNetworking.");
        base.Awake();
    }
}
