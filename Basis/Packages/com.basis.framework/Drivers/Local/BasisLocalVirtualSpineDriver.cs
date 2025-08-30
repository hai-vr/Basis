using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using Basis.Scripts.TransformBinders.BoneControl;
using UnityEngine;

[System.Serializable]
public class BasisLocalVirtualSpineDriver
{
    [Header("Rotation Speeds (deg/sec-equivalent via Slerp dt scaling)")]
    public float NeckRotationSpeed = 40f;
    public float ChestRotationSpeed = 25f;
    public float SpineRotationSpeed = 30f;
    public float HipsRotationSpeed = 20f; // slightly lower to keep hips more upright

    [Header("Offsets (optional; in each bone's target local frame)")]
    public Vector3 headOffset;      // Eye-to-head in eye local space
    public Vector3 neckOffset;      // Head-to-neck in head local space
    public Vector3 neckEyeOffset;

    /// <summary>
    /// Whether to restrict downstream rotations to yaw-only.
    /// Head/Neck typically follow full rotation, while torso tends to yaw-only.
    /// </summary>
    [Header("Yaw Filtering")]
    public bool ChestYawOnly = true;
    public bool SpineYawOnly = true;
    public bool HipsYawOnly = true;

    private bool _initialized;

    public void Initialize()
    {
        if (_initialized) return;

        // Enable virtual overrides for the segments we drive
        TrySetOverride(BasisLocalBoneDriver.HeadControl, true);
        TrySetOverride(BasisLocalBoneDriver.NeckControl, true);
        TrySetOverride(BasisLocalBoneDriver.ChestControl, true);
        TrySetOverride(BasisLocalBoneDriver.SpineControl, true);
        TrySetOverride(BasisLocalBoneDriver.HipsControl, true);

        if (BasisLocalPlayer.Instance != null)
        {
            BasisLocalPlayer.Instance.OnPreSimulateBones += OnSimulateHead;
        }

        _initialized = true;
    }

    public void DeInitialize()
    {
        if (!_initialized) return;

        // Disable our overrides
        TrySetOverride(BasisLocalBoneDriver.HeadControl, false);
        TrySetOverride(BasisLocalBoneDriver.NeckControl, false);
        TrySetOverride(BasisLocalBoneDriver.ChestControl, false);
        TrySetOverride(BasisLocalBoneDriver.SpineControl, false);
        TrySetOverride(BasisLocalBoneDriver.HipsControl, false);

        if (BasisLocalPlayer.Instance != null)
        {
            BasisLocalPlayer.Instance.OnPreSimulateBones -= OnSimulateHead;
        }

        _initialized = false;
    }

    private static void TrySetOverride(BasisLocalBoneControl control, bool enabled)
    {
        if (control != null)
        {
            control.HasVirtualOverride = enabled;
        }
    }

    /// <summary>
    /// Main simulation callback (registered on OnPreSimulateBones).
    /// Propagates head/neck orientation and blends down the spine with decreasing influence.
    /// Positions are updated using each bone's Target and ScaledOffset.
    /// </summary>
    public void OnSimulateHead()
    {
        // Validate presence of player and the required controls before proceeding
        var player = BasisLocalPlayer.Instance;
        if (player == null) return;

        var eye = BasisLocalBoneDriver.EyeControl;
        var head = BasisLocalBoneDriver.HeadControl;
        var neck = BasisLocalBoneDriver.NeckControl;
        var chest = BasisLocalBoneDriver.ChestControl;
        var spine = BasisLocalBoneDriver.SpineControl;
        var hips = BasisLocalBoneDriver.HipsControl;

        if (eye == null || head == null || neck == null || chest == null || spine == null || hips == null)
            return;

        float dt = Time.deltaTime;

        // 1) Head & Neck directly follow the eyes/head respectively (full rotation).
        head.OutGoingData.rotation = eye.OutGoingData.rotation;

        // Smooth neck towards head
        neck.OutGoingData.rotation = SmoothSlerp(neck.OutGoingData.rotation, head.OutGoingData.rotation, NeckRotationSpeed, dt);

        // 2) Chest follows neck with reduced influence (commonly yaw-only to avoid torsion).
        {
            Quaternion targetChest = SmoothSlerp(chest.OutGoingData.rotation, neck.OutGoingData.rotation, ChestRotationSpeed, dt);
            chest.OutGoingData.rotation = ChestYawOnly ? ExtractYawRotation(targetChest) : targetChest;
        }

        // 3) Spine follows chest with even less influence (often yaw-only).
        {
            Quaternion targetSpine = SmoothSlerp(spine.OutGoingData.rotation, chest.OutGoingData.rotation, SpineRotationSpeed, dt);
            spine.OutGoingData.rotation = SpineYawOnly ? ExtractYawRotation(targetSpine) : targetSpine;
        }

        // 4) Hips should remain most upright; blend gently toward the spine (BUGFIX: previously slerped hips->hips).
        {
            Quaternion targetHips = SmoothSlerp(hips.OutGoingData.rotation, spine.OutGoingData.rotation, HipsRotationSpeed, dt);
            hips.OutGoingData.rotation = HipsYawOnly ? ExtractYawRotation(targetHips) : targetHips;
        }

        // World transforms for ApplyWorldAndLast
        Transform rootTransform = player.transform;
        Matrix4x4 parentMatrix = rootTransform.localToWorldMatrix;
        Quaternion rootRotation = rootTransform.rotation;

        // 5) Positions (optional). Uses each bone's Target + ScaledOffset with yaw-only facing of the target.
        ApplyPositionControl(head, parentMatrix, rootRotation);
        ApplyPositionControl(neck, parentMatrix, rootRotation);
        ApplyPositionControl(chest, parentMatrix, rootRotation);
        ApplyPositionControl(spine, parentMatrix, rootRotation);
        ApplyPositionControl(hips, parentMatrix, rootRotation);
    }

    /// <summary>
    /// Applies position based on the Target's yaw-facing + ScaledOffset, then finalizes with ApplyWorldAndLast.
    /// Safely no-ops if Target is null.
    /// </summary>
    private void ApplyPositionControl(BasisLocalBoneControl boneControl, Matrix4x4 parentMatrix, Quaternion rootRotation)
    {
        if (boneControl == null || boneControl.Target == null)
            return;

        // Use the target's yaw orientation to place this bone
        Quaternion targetRotation = boneControl.Target.OutGoingData.rotation;
        Quaternion yawRotation = ExtractYawRotation(targetRotation);

        // If you want to incorporate custom offsets like headOffset/neckOffset/etc,
        // you could detect the specific bone here and apply them. For example:
        // if (ReferenceEquals(boneControl, BasisLocalBoneDriver.HeadControl)) offset += yawRotation * headOffset;
        // For now, we respect each bone's configured ScaledOffset.
        Vector3 offset = yawRotation * boneControl.ScaledOffset;

        boneControl.OutGoingData.position = boneControl.Target.OutGoingData.position + offset;
        boneControl.ApplyWorldAndLast(parentMatrix, rootRotation);
    }

    /// <summary>
    /// Slerp with a deltaTime-scaled factor. Clamps to [0,1] for stability.
    /// </summary>
    private static Quaternion SmoothSlerp(Quaternion current, Quaternion target, float speed, float dt)
    {
        // Convert speed "deg/sec-like" to a normalized interpolation amount per frame.
        // This isn't a strict deg/sec, but a stable and intuitive tuning knob.
        float t = Mathf.Clamp01(dt * Mathf.Max(0f, speed));
        return Quaternion.Slerp(current, target, t);
    }

    /// <summary>
    /// Extracts the yaw-only component of a rotation (around world up).
    /// </summary>
    private static Quaternion ExtractYawRotation(Quaternion rotation)
    {
        // Project forward onto the horizontal plane, then LookRotation with up.
        Vector3 forward = rotation * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 1e-6f)
        {
            // Fallback if pointing straight up/down (degenerate forward): face +Z.
            forward = Vector3.forward;
        }
        forward.Normalize();
        return Quaternion.LookRotation(forward, Vector3.up);
    }
}
