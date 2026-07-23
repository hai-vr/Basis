using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// A local marker puck shown whenever the camera has left your hand — following, flying, or
/// world/playspace pinned — the same model remote players see for a networked camera
/// (<c>BasisCameraRemotePip</c>). It marks where the camera has gone, and keeps marking it even
/// when the camera body itself is hidden, so a detached camera is always locatable.
/// </summary>
public partial class BasisHandHeldCamera
{
    // Same addressable the network PIP driver instantiates. Its address is the full asset path
    // (see BasisNetworkPIPCameraDriver), and the prefab must stay in com.basis.sdk for it.
    private const string FollowPipPrefabAddress = "Packages/com.basis.sdk/Prefabs/UI/Camera Prefab/BasisCameraRemotePip.prefab";

    /// <summary>Whether to drop the follow marker puck while following. On by default.</summary>
    public bool showFollowPip = true;

    private GameObject followPipInstance;
    private AsyncOperationHandle<GameObject> followPipHandle;
    private bool followPipLoading;
    private BasisPickupInteractable followPipPickup;
    private bool followPipGrabbed;

    /// <summary>True while the player is holding the follow puck — a "selfie stick" grip on the camera.</summary>
    public bool FollowPipGrabbed => followPipGrabbed && followPipInstance != null;

    /// <summary>While grabbed, the puck's transform is where the camera should be.</summary>
    public bool TryGetFollowPipPose(out Vector3 pos, out Quaternion rot)
    {
        if (FollowPipGrabbed)
        {
            followPipInstance.transform.GetPositionAndRotation(out pos, out rot);
            return true;
        }
        pos = default;
        rot = Quaternion.identity;
        return false;
    }

    /// <summary>Toggles the follow marker puck, despawning it immediately when turned off.</summary>
    public void SetShowFollowPip(bool show)
    {
        showFollowPip = show;
        if (!show) DespawnFollowPip();
    }

    /// <summary>Per-frame: spawn the puck while the camera is off in the world and keep it on the camera, else drop it.</summary>
    private void UpdateFollowPip()
    {
        bool shouldShow = showFollowPip && IsDetachedFromHand;

        if (!shouldShow)
        {
            DespawnFollowPip();
            return;
        }

        if (followPipInstance == null)
        {
            SpawnFollowPip();
            return; // Positioned once it finishes loading, and every frame after.
        }

        // While the player holds the puck it is the master — the camera tracks it (see
        // MoveCameraFlying), so leave the transform to the pickup and don't drive it from the camera.
        if (followPipGrabbed) return;

        captureCamera.transform.GetPositionAndRotation(out Vector3 pos, out Quaternion rot);
        followPipInstance.transform.SetPositionAndRotation(pos, rot);
    }

    private void SpawnFollowPip()
    {
        // Async load in flight, or the camera is gone: nothing to do this frame.
        if (followPipLoading || captureCamera == null) return;

        followPipLoading = true;
        followPipHandle = Addressables.LoadAssetAsync<GameObject>(FollowPipPrefabAddress);
        followPipHandle.Completed += handle =>
        {
            followPipLoading = false;

            // Follow may have ended, or the camera been destroyed, while the load ran.
            if (this == null || !showFollowPip || !IsDetachedFromHand || captureCamera == null)
            {
                if (handle.IsValid()) Addressables.Release(handle);
                return;
            }

            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                BasisDebug.LogError("Follow PIP prefab failed to load.", BasisDebug.LogTag.Camera);
                if (handle.IsValid()) Addressables.Release(handle);
                return;
            }

            captureCamera.transform.GetPositionAndRotation(out Vector3 pos, out Quaternion rot);
            followPipInstance = Instantiate(handle.Result, pos, rot);
            followPipInstance.name = "FollowCameraPip";

            // Local-only marker: strip the networked-camera identity so nothing treats it as a
            // real remote PIP. Its own colliders stay off; grabbing goes through the box below.
            if (followPipInstance.TryGetComponent(out BasisCameraRemotePip remotePip)) Destroy(remotePip);
            foreach (Collider existing in followPipInstance.GetComponentsInChildren<Collider>(true))
            {
                existing.enabled = false;
            }

            MakeFollowPipGrabbable(followPipInstance);
        };
    }

    /// <summary>
    /// Adds a grab box + pickup so the puck acts as a selfie stick: while held the camera tracks
    /// it, and releasing hands control back to whatever the camera was doing (auto-follow resumes).
    /// </summary>
    private void MakeFollowPipGrabbable(GameObject pip)
    {
        BoxCollider box = pip.AddComponent<BoxCollider>();
        if (TryGetLocalRendererBounds(pip, out Vector3 center, out Vector3 size))
        {
            box.center = center;
            // Give a small grab margin and a floor so a thin puck is still easy to grab.
            box.size = Vector3.Max(size * 1.2f, Vector3.one * 0.08f);
        }
        else
        {
            box.size = Vector3.one * 0.2f;
        }

        followPipPickup = pip.AddComponent<BasisPickupInteractable>();
        followPipPickup.OnInteractStartEvent.AddListener(_ => followPipGrabbed = true);
        followPipPickup.OnInteractEndEvent.AddListener(_ => followPipGrabbed = false);
    }

    private static bool TryGetLocalRendererBounds(GameObject root, out Vector3 center, out Vector3 size)
    {
        center = Vector3.zero;
        size = Vector3.zero;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return false;

        Bounds world = renderers[0].bounds;
        for (int Index = 1; Index < renderers.Length; Index++) world.Encapsulate(renderers[Index].bounds);

        center = root.transform.InverseTransformPoint(world.center);
        Vector3 scale = root.transform.lossyScale;
        size = new Vector3(
            world.size.x / Mathf.Max(1e-4f, Mathf.Abs(scale.x)),
            world.size.y / Mathf.Max(1e-4f, Mathf.Abs(scale.y)),
            world.size.z / Mathf.Max(1e-4f, Mathf.Abs(scale.z)));
        return true;
    }

    private void DespawnFollowPip()
    {
        followPipGrabbed = false;
        followPipPickup = null;
        if (followPipInstance != null)
        {
            Destroy(followPipInstance);
            followPipInstance = null;
        }
        if (followPipHandle.IsValid())
        {
            Addressables.Release(followPipHandle);
        }
    }
}
