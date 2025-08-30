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

    // === Baseline vertical separations (world-space Y) captured on Initialize ===
    private float _baseHeadNeckSepY;
    private float _baseNeckChestSepY;
    private float _baseChestSpineSepY;
    private float _baseSpineHipsSepY;

    // Small helper to guard nulls
    private static void TrySetOverride(BasisLocalBoneControl control, bool enabled)
    {
        if (control != null) control.HasVirtualOverride = enabled;
    }

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

    private void CacheBaselineSeps()
    {
        var head = BasisLocalBoneDriver.HeadControl;
        var neck = BasisLocalBoneDriver.NeckControl;
        var chest = BasisLocalBoneDriver.ChestControl;
        var spine = BasisLocalBoneDriver.SpineControl;
        var hips = BasisLocalBoneDriver.HipsControl;

        _baseHeadNeckSepY = CaculateYDif(head, neck);
        _baseNeckChestSepY = CaculateYDif(neck, chest);
        _baseChestSpineSepY = CaculateYDif(chest, spine);
        _baseSpineHipsSepY = CaculateYDif(spine, hips);
    }

    private static float CaculateYDif(BasisLocalBoneControl a, BasisLocalBoneControl b)
    {
        return a.Target.TposeLocalScaled.position.y - b.Target.TposeLocalScaled.position.y;
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

        // --- Rotations ---
        head.OutGoingData.rotation = eye.OutGoingData.rotation;

        if (neck != null)
            neck.OutGoingData.rotation = SmoothSlerp(neck.OutGoingData.rotation, head.OutGoingData.rotation, NeckRotationSpeed, dt);

        if (chest != null && neck != null)
        {
            Quaternion targetChest = SmoothSlerp(chest.OutGoingData.rotation, neck.OutGoingData.rotation, ChestRotationSpeed, dt);
            chest.OutGoingData.rotation = ExtractYawRotation(targetChest);
        }

        if (spine != null && chest != null)
        {
            Quaternion targetSpine = SmoothSlerp(spine.OutGoingData.rotation, chest.OutGoingData.rotation, SpineRotationSpeed, dt);
            spine.OutGoingData.rotation = ExtractYawRotation(targetSpine);
        }

        if (hips != null && spine != null)
        {
            Quaternion targetHips = SmoothSlerp(hips.OutGoingData.rotation, spine.OutGoingData.rotation, HipsRotationSpeed, dt);
            hips.OutGoingData.rotation = ExtractYawRotation(targetHips);
        }

        // Capture baselines immediately using current TARGET (tracker-driven) positions.
        CacheBaselineSeps();
        // --- Per-link vertical deltas (world Y), compared to baseline separations ---
        // If head drops toward neck, dHN < 0, and we propagate that downward.
        float dHN = (neck != null) ? (head.Target.OutGoingData.position.y - neck.Target.OutGoingData.position.y) - _baseHeadNeckSepY : 0f;
        float dNC = (chest != null && neck != null) ? (neck.Target.OutGoingData.position.y - chest.Target.OutGoingData.position.y) - _baseNeckChestSepY : 0f;
        float dCS = (spine != null && chest != null) ? (chest.Target.OutGoingData.position.y - spine.Target.OutGoingData.position.y) - _baseChestSpineSepY : 0f;
        float dSH = (hips != null && spine != null) ? (spine.Target.OutGoingData.position.y - hips.Target.OutGoingData.position.y) - _baseSpineHipsSepY : 0f;

        // Cumulative downstream offsets:
        // - neck inherits dHN
        // - chest inherits dHN + dNC
        // - spine inherits dHN + dNC + dCS
        // - hips inherits  dHN + dNC + dCS + dSH
        float offNeck = dHN;
        float offChest = dHN + dNC;
        float offSpine = dHN + dNC + dCS;
        float offHips = dHN + dNC + dCS + dSH;

        // World matrices for finalization
        Matrix4x4 parentMatrix = BasisLocalPlayer.Instance.transform.localToWorldMatrix;

        // --- Positions ---
        // Head/Neck follow eye/head fully (no torso lock); others as configured.
        ApplyPositionControl(head, parentMatrix, torsoLock: false, extraY: 0f);

        if (neck != null)
            ApplyPositionControl(neck, parentMatrix, torsoLock: false, extraY: offNeck);

        if (chest != null)
            ApplyPositionControl(chest, parentMatrix, torsoLock: true, extraY: offChest);

        if (spine != null)
            ApplyPositionControl(spine, parentMatrix, torsoLock: false, extraY: offSpine);

        if (hips != null)
            ApplyPositionControl(hips, parentMatrix, torsoLock: false, extraY: offHips);
    }

    private void ApplyPositionControl(BasisLocalBoneControl boneControl, Matrix4x4 parentMatrix, bool torsoLock, float extraY)
    {
        if (boneControl == null) return;

        Quaternion targetRot = boneControl.Target.OutGoingData.rotation;
        if (torsoLock)
        {
            // yaw-only for torso stability
            targetRot = ExtractYawRotation(targetRot);
        }

        // Per-bone local offset (already scaled)
        Vector3 localOffset = boneControl.ScaledOffset;

        // Torso stability: ignore vertical component of the offset so head pitch won't add/subtract height
        if (torsoLock)
        {
            localOffset.y = 0f;
        }

        Vector3 offset = targetRot * localOffset;

        // Start from TARGET (tracker-driven) position so we FOLLOW trackers in XZ by default.
        Vector3 desired = boneControl.Target.OutGoingData.position + offset;

        if (torsoLock)
        {
            // Keep torso height anchored to its baseline, but add the propagated per-link compression/extension.
            desired.y = boneControl.TposeLocalScaled.position.y + extraY;
        }
        else
        {
            // Non-torso-locked bones: add the propagated extraY on top of the target+offset result.
            desired.y += extraY;
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
