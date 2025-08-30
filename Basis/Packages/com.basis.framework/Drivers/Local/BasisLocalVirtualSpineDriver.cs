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
    public float HipsRotationSpeed = 20f;

    [Header("Torso Position Locking")]
    [Tooltip("If true, lock torso Y to its baseline height to avoid pitch-injected vertical drift.")]
    public bool LockTorsoYToBaseline = true;
    [Tooltip("If true, also lock torso XZ to baseline (NOT recommended with VR trackers).")]
    public bool LockTorsoXZToBaseline = false;

    private bool _initialized;

    // Baselines captured once (local space, T-pose by default or current target when requested)
    private float _chestBaseY;
    private float _spineBaseY;
    private float _hipsBaseY;

    private Vector2 _chestBaseXZ;
    private Vector2 _spineBaseXZ;
    private Vector2 _hipsBaseXZ;

    public void Initialize()
    {
        if (_initialized) return;

        TrySetOverride(BasisLocalBoneDriver.HeadControl, true);
        TrySetOverride(BasisLocalBoneDriver.NeckControl, true);
        TrySetOverride(BasisLocalBoneDriver.ChestControl, true);
        TrySetOverride(BasisLocalBoneDriver.SpineControl, true);
        TrySetOverride(BasisLocalBoneDriver.HipsControl, true);

        // Capture baselines ONCE from T-pose so we have reference heights.
        CaptureBaselines();
       // BasisLocalPlayer.Instance.OnAvatarSwitched += CaptureBaseLine();
        BasisLocalPlayer.Instance.OnPreSimulateBones += OnSimulateHead;
        _initialized = true;
    }
    public void DeInitialize()
    {
        if (!_initialized) return;

        TrySetOverride(BasisLocalBoneDriver.HeadControl, false);
        TrySetOverride(BasisLocalBoneDriver.NeckControl, false);
        TrySetOverride(BasisLocalBoneDriver.ChestControl, false);
        TrySetOverride(BasisLocalBoneDriver.SpineControl, false);
        TrySetOverride(BasisLocalBoneDriver.HipsControl, false);

        BasisLocalPlayer.Instance.OnPreSimulateBones -= OnSimulateHead;
        _initialized = false;
    }

    /// <summary>
    /// Call this if you want to recenter baseline heights/XZ (e.g., after recalibration).
    /// If fromTPose=false, uses the CURRENT TARGET local pose instead of T-pose.
    /// </summary>
    public void CaptureBaselines()
    {
        var chest = BasisLocalBoneDriver.ChestControl;
        var spine = BasisLocalBoneDriver.SpineControl;
        var hips = BasisLocalBoneDriver.HipsControl;

        Vector3 ChestL = chest.TposeLocalScaled.position;
        Vector3 SpineL = spine.TposeLocalScaled.position;
        Vector3 HipsL = hips.TposeLocalScaled.position;

        _chestBaseY = ChestL.y;
        _spineBaseY = SpineL.y;
        _hipsBaseY = HipsL.y;

        _chestBaseXZ = new Vector2(ChestL.x, ChestL.z);
        _spineBaseXZ = new Vector2(SpineL.x, SpineL.z);
        _hipsBaseXZ = new Vector2(HipsL.x, HipsL.z);
    }

    private static void TrySetOverride(BasisLocalBoneControl control, bool enabled)
    {
        if (control != null) control.HasVirtualOverride = enabled;
    }

    public void OnSimulateHead()
    {
        var eye = BasisLocalBoneDriver.EyeControl;
        var head = BasisLocalBoneDriver.HeadControl;
        var neck = BasisLocalBoneDriver.NeckControl;
        var chest = BasisLocalBoneDriver.ChestControl;
        var spine = BasisLocalBoneDriver.SpineControl;
        var hips = BasisLocalBoneDriver.HipsControl;

        float dt = Time.deltaTime;

        // Rotations
        head.OutGoingData.rotation = eye.OutGoingData.rotation;
        neck.OutGoingData.rotation = SmoothSlerp(neck.OutGoingData.rotation, head.OutGoingData.rotation, NeckRotationSpeed, dt);

        {
            Quaternion targetChest = SmoothSlerp(chest.OutGoingData.rotation, neck.OutGoingData.rotation, ChestRotationSpeed, dt);
            chest.OutGoingData.rotation = ExtractYawRotation(targetChest);
        }
        {
            Quaternion targetSpine = SmoothSlerp(spine.OutGoingData.rotation, chest.OutGoingData.rotation, SpineRotationSpeed, dt);
            spine.OutGoingData.rotation = ExtractYawRotation(targetSpine);
        }
        {
            Quaternion targetHips = SmoothSlerp(hips.OutGoingData.rotation, spine.OutGoingData.rotation, HipsRotationSpeed, dt);
            hips.OutGoingData.rotation = ExtractYawRotation(targetHips);
        }

        // World matrices for finalization
        Matrix4x4 parentMatrix = BasisLocalPlayer.Instance.transform.localToWorldMatrix;
        Quaternion rootRotation = parentMatrix.rotation;

        // Positions:
        // Head/Neck: full rotation offsets so eyes/head co-locate.
        ApplyPositionControl(head, parentMatrix, rootRotation, TorsoLock.None);
        ApplyPositionControl(neck, parentMatrix, rootRotation, TorsoLock.None);

        // Torso: follow tracker XZ; optionally lock Y to baseline. (Optionally XZ too, if you really want the old behavior.)
        ApplyPositionControl(chest, parentMatrix, rootRotation, SelectLock(TorsoSegment.Chest));
        ApplyPositionControl(spine, parentMatrix, rootRotation, SelectLock(TorsoSegment.Spine));
        ApplyPositionControl(hips, parentMatrix, rootRotation, SelectLock(TorsoSegment.Hips));
    }

    private enum TorsoSegment { Chest, Spine, Hips }
    private enum TorsoLock { None, LockY, LockXZ_Y }

    private TorsoLock SelectLock(TorsoSegment seg)
    {
        if (LockTorsoXZToBaseline) return TorsoLock.LockXZ_Y;
        if (LockTorsoYToBaseline) return TorsoLock.LockY;
        return TorsoLock.None;
    }

    private void ApplyPositionControl(BasisLocalBoneControl boneControl, Matrix4x4 parentMatrix, Quaternion rootRotation, TorsoLock torsoLock)
    {
        Quaternion targetRotFull = boneControl.Target.OutGoingData.rotation;
        Quaternion targetRotYaw = ExtractYawRotation(targetRotFull);

        bool isTorso = (torsoLock != TorsoLock.None);

        // For torso offsets, use yaw-only so pitch doesn't inject forward/back offsets.
        Quaternion rotForOffset = isTorso ? targetRotYaw : targetRotFull;

        // Choose which offset to use (you can wire these per-bone if desired)
        Vector3 localOffset = boneControl.ScaledOffset;

        // Torso stability: ignore the vertical component of the offset so head pitch won't add/subtract height
        if (isTorso) localOffset.y = 0f;

        Vector3 offset = rotForOffset * localOffset;

        // Start from the TARGET (tracker-driven) position so we FOLLOW trackers in XZ by default.
        Vector3 desired = boneControl.Target.OutGoingData.position + offset;

        // Apply locks against captured baselines (local space)
        if (isTorso)
        {
            switch (torsoLock)
            {
                case TorsoLock.LockY:
                    // Lock only Y, keep live XZ from the tracker
                    if (boneControl == BasisLocalBoneDriver.ChestControl) desired.y = _chestBaseY;
                    else if (boneControl == BasisLocalBoneDriver.SpineControl) desired.y = _spineBaseY;
                    else if (boneControl == BasisLocalBoneDriver.HipsControl) desired.y = _hipsBaseY;
                    break;

                case TorsoLock.LockXZ_Y:
                    // Old behavior (NOT recommended with trackers): lock full planar position to baseline
                    if (boneControl == BasisLocalBoneDriver.ChestControl)
                    {
                        desired.y = _chestBaseY;
                        desired.x = _chestBaseXZ.x;
                        desired.z = _chestBaseXZ.y;
                    }
                    else if (boneControl == BasisLocalBoneDriver.SpineControl)
                    {
                        desired.y = _spineBaseY;
                        desired.x = _spineBaseXZ.x;
                        desired.z = _spineBaseXZ.y;
                    }
                    else if (boneControl == BasisLocalBoneDriver.HipsControl)
                    {
                        desired.y = _hipsBaseY;
                        desired.x = _hipsBaseXZ.x;
                        desired.z = _hipsBaseXZ.y;
                    }
                    break;
            }
        }

        boneControl.OutGoingData.position = desired;
        boneControl.ApplyWorldAndLast(parentMatrix, rootRotation);
    }

    private static Quaternion SmoothSlerp(Quaternion current, Quaternion target, float speed, float dt)
    {
        float t = Mathf.Clamp01(dt * Mathf.Max(0f, speed));
        return Quaternion.Slerp(current, target, t);
    }

    private static Quaternion ExtractYawRotation(Quaternion rotation)
    {
        Vector3 f = rotation * Vector3.forward;
        f.y = 0f;
        if (f.sqrMagnitude < 1e-6f) f = Vector3.forward;
        f.Normalize();
        return Quaternion.LookRotation(f, Vector3.up);
    }
}
