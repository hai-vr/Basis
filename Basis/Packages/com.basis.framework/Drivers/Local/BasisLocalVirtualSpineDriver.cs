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
    private bool _initialized;

    public void Initialize()
    {
        if (_initialized) return;

        TrySetOverride(BasisLocalBoneDriver.HeadControl, true);
        TrySetOverride(BasisLocalBoneDriver.NeckControl, true);
        TrySetOverride(BasisLocalBoneDriver.ChestControl, true);
        TrySetOverride(BasisLocalBoneDriver.SpineControl, true);
        TrySetOverride(BasisLocalBoneDriver.HipsControl, true);


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

        Quaternion targetChest = SmoothSlerp(chest.OutGoingData.rotation, neck.OutGoingData.rotation, ChestRotationSpeed, dt);
        chest.OutGoingData.rotation = ExtractYawRotation(targetChest);

        Quaternion targetSpine = SmoothSlerp(spine.OutGoingData.rotation, chest.OutGoingData.rotation, SpineRotationSpeed, dt);
        spine.OutGoingData.rotation = ExtractYawRotation(targetSpine);

        Quaternion targetHips = SmoothSlerp(hips.OutGoingData.rotation, spine.OutGoingData.rotation, HipsRotationSpeed, dt);
        hips.OutGoingData.rotation = ExtractYawRotation(targetHips);

        // World matrices for finalization
        Matrix4x4 parentMatrix = BasisLocalPlayer.Instance.transform.localToWorldMatrix;

        // Positions:
        // Head/Neck: full rotation offsets so eyes/head co-locate.
        ApplyPositionControl(head, parentMatrix, false);
        ApplyPositionControl(neck, parentMatrix, false);

        // Torso: follow tracker XZ; optionally lock Y to baseline. (Optionally XZ too, if you really want the old behavior.)
        ApplyPositionControl(chest, parentMatrix, true);
        ApplyPositionControl(spine, parentMatrix, false);
        ApplyPositionControl(hips, parentMatrix, false);
    }
    private void ApplyPositionControl(BasisLocalBoneControl boneControl, Matrix4x4 parentMatrix,bool torsoLock)
    {
        Quaternion Rot = boneControl.Target.OutGoingData.rotation;
        if (torsoLock)
        {
            Rot = ExtractYawRotation(Rot);
        }

        // Choose which offset to use (you can wire these per-bone if desired)
        Vector3 localOffset = boneControl.ScaledOffset;

        // Torso stability: ignore the vertical component of the offset so head pitch won't add/subtract height
        if (torsoLock) localOffset.y = 0f;

        Vector3 offset = Rot * localOffset;

        // Start from the TARGET (tracker-driven) position so we FOLLOW trackers in XZ by default.
        Vector3 desired = boneControl.Target.OutGoingData.position + offset;

        // Apply locks against captured baselines (local space)
        if (torsoLock)
        {
            desired.y = boneControl.TposeLocalScaled.position.y;
        }

        boneControl.OutGoingData.position = desired;
        boneControl.ApplyWorldAndLast(parentMatrix);
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
