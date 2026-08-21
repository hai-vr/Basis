using Basis.Scripts.BasisSdk.Interactions;
using Basis.Scripts.Drivers;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>How the detached camera is marked while it is off in the world.</summary>
public enum BasisCameraDetachedMarker
{
    /// <summary>No marker.</summary>
    Off = 0,
    /// <summary>A solid puck model — the same one remote players see, and it's grabbable as a selfie stick.</summary>
    Puck = 1,
    /// <summary>A lightweight wireframe camera gizmo; cheaper and non-interactive.</summary>
    Gizmo = 2,
}

/// <summary>
/// A local marker shown whenever the camera has left your hand — following, flying, or
/// world/playspace pinned — so you can see where it has gone, even when the camera body itself
/// is hidden. Either a solid puck (the model remote players see, grabbable as a selfie stick) or
/// a lightweight wireframe gizmo.
/// </summary>
public partial class BasisHandHeldCamera
{
    // Same addressable the network PIP driver instantiates. Its address is the full asset path
    // (see BasisNetworkPIPCameraDriver), and the prefab must stay in com.basis.sdk for it.
    private const string FollowPipPrefabAddress = "Packages/com.basis.sdk/Prefabs/UI/Camera Prefab/BasisCameraRemotePip.prefab";

    /// <summary>Which marker to show while the camera is detached. Puck by default.</summary>
    public BasisCameraDetachedMarker detachedMarker = BasisCameraDetachedMarker.Puck;

    /// <summary>
    /// The layer both detached markers live on, or -1 where the project does not define it.
    /// OverlayUI is what the capture camera culls and what <see cref="ManagedCaptureLayers"/>
    /// keeps out of the Render Layers list, so nothing on it can reach a photo, a 360 or the
    /// video feed — while the player, whose camera does render it, still sees the marker.
    /// </summary>
    public static int MarkerLayer => LayerMask.NameToLayer("OverlayUI");

    /// <summary>
    /// How far out along the lens axis the puck is parked from the capture camera, in metres at
    /// default avatar scale.
    ///
    /// <para>It used to sit exactly on the camera, which is where the prop's own HUD is too, so on
    /// the frame the camera detaches the two are coincident: the puck landed in the middle of the
    /// panel and its grab box took the pointer the buttons under it wanted. The operator is always
    /// behind the lens — the same fact <see cref="TryGetFocusDepth"/> leans on — so parking the
    /// puck out along that axis puts it behind the panel from where they stand, where the panel
    /// hides it and is what a pointer reaches first.</para>
    /// </summary>
    private const float FollowPuckLensOffset = 0.25f;

    /// <summary>Where the puck sits relative to a capture camera at <paramref name="rotation"/>, in world space.</summary>
    private static Vector3 FollowPuckOffset(Quaternion rotation) =>
        rotation * new Vector3(0f, 0f, FollowPuckLensOffset * BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale);

    private GameObject followPipInstance;
    private AsyncOperationHandle<GameObject> followPipHandle;
    private bool followPipLoading;
    private BasisPickupInteractable followPipPickup;
    private bool followPipGrabbed;

    /// <summary>True while the player is holding the follow puck — a "selfie stick" grip on the camera.</summary>
    public bool FollowPipGrabbed => followPipGrabbed && followPipInstance != null;

    /// <summary>While grabbed, the puck's transform is where the camera should be, less the parking offset.</summary>
    public bool TryGetFollowPipPose(out Vector3 pos, out Quaternion rot)
    {
        if (FollowPipGrabbed)
        {
            followPipInstance.transform.GetPositionAndRotation(out pos, out rot);
            // Undo the parking offset, or taking hold of the puck would jump the camera by it.
            pos -= FollowPuckOffset(rot);
            return true;
        }
        pos = default;
        rot = Quaternion.identity;
        return false;
    }

    /// <summary>Sets the detached-marker mode, tearing down whatever the previous mode had spawned.</summary>
    public void SetDetachedMarker(BasisCameraDetachedMarker mode)
    {
        if (detachedMarker == mode) return;
        detachedMarker = mode;
        // Drop the visuals the old mode owned; the next tick rebuilds for the new one.
        if (mode != BasisCameraDetachedMarker.Puck) DespawnFollowPip();
        if (mode != BasisCameraDetachedMarker.Gizmo) HideDetachedGizmo();
    }

    /// <summary>Per-frame: show the chosen marker while the camera is off in the world, else clear both.</summary>
    private void UpdateFollowPip()
    {
        bool detached = IsDetachedFromHand;

        if (!detached || detachedMarker != BasisCameraDetachedMarker.Puck)
        {
            DespawnFollowPip();
        }
        if (!detached || detachedMarker != BasisCameraDetachedMarker.Gizmo)
        {
            HideDetachedGizmo();
        }

        if (!detached) return;

        switch (detachedMarker)
        {
            case BasisCameraDetachedMarker.Puck:
                UpdateFollowPuck();
                break;
            case BasisCameraDetachedMarker.Gizmo:
                UpdateDetachedGizmo();
                break;
        }
    }

    private void UpdateFollowPuck()
    {
        if (followPipInstance == null)
        {
            SpawnFollowPip();
            return; // Positioned once it finishes loading, and every frame after.
        }

        // While the player holds the puck it is the master — the camera tracks it (see
        // MoveCameraFlying), so leave the transform to the pickup and don't drive it from the camera.
        if (followPipGrabbed) return;

        captureCamera.transform.GetPositionAndRotation(out Vector3 pos, out Quaternion rot);
        followPipInstance.transform.SetPositionAndRotation(pos + FollowPuckOffset(rot), rot);
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

            // Follow may have ended, the mode changed, or the camera been destroyed while loading.
            if (this == null || detachedMarker != BasisCameraDetachedMarker.Puck || !IsDetachedFromHand || captureCamera == null)
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
            followPipInstance = Instantiate(handle.Result, pos + FollowPuckOffset(rot), rot);
            followPipInstance.name = "FollowCameraPip";
            RegisterSpawnedObject(followPipInstance);

            // Keep the marker out of the shot. The puck is parked out along the lens axis, square
            // in front of it, so the layer is the only thing keeping the capture from filming it.
            int overlayUi = MarkerLayer;
            if (overlayUi >= 0) SetLayerRecursively(followPipInstance, overlayUi);

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

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        root.layer = layer;
        Transform t = root.transform;
        for (int Index = 0; Index < t.childCount; Index++)
        {
            SetLayerRecursively(t.GetChild(Index).gameObject, layer);
        }
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
            ForgetSpawnedObject(followPipInstance);
            Destroy(followPipInstance);
            followPipInstance = null;
        }
        if (followPipHandle.IsValid())
        {
            Addressables.Release(followPipHandle);
        }
    }

    // ---- Gizmo marker ---------------------------------------------------------------
    // A wireframe camera drawn through BasisGizmoManager: a small lens quad set behind the camera
    // plus four cone lines from its corners up to the lens. Rendered whenever active regardless of
    // the debug gizmo master toggle (BasisGizmoManager.Render is not gated on it), so it works as
    // a marker.
    //
    // Like the puck, it is kept out of the shot by living on MarkerLayer — the capture camera
    // culls it. Sitting behind the lens is not enough on its own: the batch is built at the tail
    // of LateUpdate from wherever the gizmo was last left, while the pose below is written in the
    // before-render pass, so the shot is taken one frame ahead of the geometry and any movement
    // between the two swings the marker into view. A 360 capture sees behind the camera anyway.

    private const float DetachedGizmoDepth = 0.18f;
    private const float DetachedGizmoHalfSize = 0.10f;
    private static readonly Color DetachedGizmoColor = new Color(0.2f, 0.9f, 1f, 1f);

    private int _gizmoQuadId;
    private int[] _gizmoConeIds;
    private bool _gizmoCreated;
    private readonly Vector3[] _gizmoQuad = new Vector3[4];

    private void UpdateDetachedGizmo()
    {
        if (captureCamera == null) { HideDetachedGizmo(); return; }

        captureCamera.transform.GetPositionAndRotation(out Vector3 apex, out Quaternion rot);
        float scale = BasisHeightDriver.AvatarToDefaultRatioScaledWithAvatarScale;
        float depth = DetachedGizmoDepth * scale;
        float half = DetachedGizmoHalfSize * scale;

        // Drawn behind the lens (negative Z in camera space) so it reads as a small camera icon
        // opening back toward the viewer rather than a cone across the subject. What keeps it out
        // of the shot is the layer, not the placement — see the note above.
        _gizmoQuad[0] = apex + rot * new Vector3(-half, -half, -depth);
        _gizmoQuad[1] = apex + rot * new Vector3(half, -half, -depth);
        _gizmoQuad[2] = apex + rot * new Vector3(half, half, -depth);
        _gizmoQuad[3] = apex + rot * new Vector3(-half, half, -depth);

        if (!_gizmoCreated)
        {
            // A project without the marker layer hands back -1, which SetGizmoLayer reads as
            // "stay on the shared gizmo layer" — still a usable marker, just visible to the shot.
            int markerLayer = MarkerLayer;
            BasisGizmoManager.CreateLineGizmo("CameraDetachedGizmo", out _gizmoQuadId, _gizmoQuad, 0.004f, DetachedGizmoColor, loop: true);
            BasisGizmoManager.SetGizmoLayer(_gizmoQuadId, markerLayer);
            _gizmoConeIds = new int[4];
            for (int Index = 0; Index < 4; Index++)
            {
                BasisGizmoManager.CreateLineGizmo("CameraDetachedGizmo", out _gizmoConeIds[Index], apex, _gizmoQuad[Index], 0.003f, DetachedGizmoColor);
                BasisGizmoManager.SetGizmoLayer(_gizmoConeIds[Index], markerLayer);
            }
            _gizmoCreated = true;
            return;
        }

        BasisGizmoManager.SetGizmoActive(_gizmoQuadId, true);
        BasisGizmoManager.UpdateLineGizmo(_gizmoQuadId, _gizmoQuad);
        for (int Index = 0; Index < 4; Index++)
        {
            BasisGizmoManager.SetGizmoActive(_gizmoConeIds[Index], true);
            BasisGizmoManager.UpdateLineGizmo(_gizmoConeIds[Index], apex, _gizmoQuad[Index]);
        }
    }

    /// <summary>Parks the gizmo lines (kept for reuse). Idempotent.</summary>
    private void HideDetachedGizmo()
    {
        if (!_gizmoCreated) return;
        BasisGizmoManager.SetGizmoActive(_gizmoQuadId, false);
        for (int Index = 0; Index < _gizmoConeIds.Length; Index++)
        {
            BasisGizmoManager.SetGizmoActive(_gizmoConeIds[Index], false);
        }
    }

    /// <summary>Destroys the gizmo lines outright. Called from teardown.</summary>
    private void DestroyDetachedGizmo()
    {
        if (!_gizmoCreated) return;
        BasisGizmoManager.DestroyGizmo(_gizmoQuadId);
        for (int Index = 0; Index < _gizmoConeIds.Length; Index++)
        {
            BasisGizmoManager.DestroyGizmo(_gizmoConeIds[Index]);
        }
        _gizmoCreated = false;
    }
}
