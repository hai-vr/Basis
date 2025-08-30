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

    [Header("Offsets (optional; in each bone's target local frame)")]
    public Vector3 headOffset;
    public Vector3 neckOffset;
    public Vector3 neckEyeOffset;

    [Header("Yaw Filtering")]
    public bool ChestYawOnly = true;
    public bool SpineYawOnly = true;
    public bool HipsYawOnly = true;

    private bool _initialized;

    // Baselines so the torso stays "planted"
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

        Vector3 ChestT = chest.TposeLocalScaled.position;
        _chestBaseY = ChestT.y;
        _chestBaseXZ = new Vector2(ChestT.x, ChestT.z);
        Vector3 SpineT = spine.TposeLocalScaled.position;
        _spineBaseY = SpineT.y;
        _spineBaseXZ = new Vector2(SpineT.x, SpineT.z);
        Vector3 HipsT = hips.TposeLocalScaled.position;
        _hipsBaseY = HipsT.y;
        _hipsBaseXZ = new Vector2(HipsT.x, HipsT.z);

        // Rotations
        head.OutGoingData.rotation = eye.OutGoingData.rotation;
        neck.OutGoingData.rotation = SmoothSlerp(neck.OutGoingData.rotation, head.OutGoingData.rotation, NeckRotationSpeed, dt);

        {
            Quaternion targetChest = SmoothSlerp(chest.OutGoingData.rotation, neck.OutGoingData.rotation, ChestRotationSpeed, dt);
            chest.OutGoingData.rotation = ChestYawOnly ? ExtractYawRotation(targetChest) : targetChest;
        }
        {
            Quaternion targetSpine = SmoothSlerp(spine.OutGoingData.rotation, chest.OutGoingData.rotation, SpineRotationSpeed, dt);
            spine.OutGoingData.rotation = SpineYawOnly ? ExtractYawRotation(targetSpine) : targetSpine;
        }
        {
            Quaternion targetHips = SmoothSlerp(hips.OutGoingData.rotation, spine.OutGoingData.rotation, HipsRotationSpeed, dt);
            hips.OutGoingData.rotation = HipsYawOnly ? ExtractYawRotation(targetHips) : targetHips;
        }

        // World matrices for finalization
        Matrix4x4 parentMatrix = BasisLocalPlayer.Instance.transform.localToWorldMatrix;
        Quaternion rootRotation = parentMatrix.rotation;

        // Positions:
        // Head/Neck: full offsets (include pitch) -> keep eyes/head co-located
        // Torso (Chest/Spine/Hips): lock Y and XZ to baseline -> eliminates back/forward creep from head pitch
        ApplyPositionControl(head, parentMatrix, rootRotation, LockPlanar.None);//head
        ApplyPositionControl(neck, parentMatrix, rootRotation, LockPlanar.None);//neck

        ApplyPositionControl(chest, parentMatrix, rootRotation, LockPlanar.ChestXZ_Y);
        ApplyPositionControl(spine, parentMatrix, rootRotation, LockPlanar.SpineXZ_Y);
        ApplyPositionControl(hips, parentMatrix, rootRotation, LockPlanar.HipsXZ_Y);
    }
    private enum LockPlanar { None, ChestXZ_Y, SpineXZ_Y, HipsXZ_Y }

    private void ApplyPositionControl(BasisLocalBoneControl boneControl, Matrix4x4 parentMatrix, Quaternion rootRotation, LockPlanar lockPlanar)
    {
        // For head/neck we want FULL rotation (pitch+roll+yaw) so the eye and head align.
        // For torso we still compute offset from the target's YAW so pitch doesn't push them.
        Quaternion targetRotFull = boneControl.Target.OutGoingData.rotation;
        Quaternion targetRotYaw = ExtractYawRotation(targetRotFull);

        bool isTorso = (lockPlanar != LockPlanar.None);

        Quaternion rotForOffset = isTorso ? targetRotYaw : targetRotFull;

        Vector3 localOffset = boneControl.ScaledOffset;

        // Torso stability: ignore vertical contribution of offsets so pitch doesn't inject Y
        if (isTorso) localOffset.y = 0f;

        Vector3 offset = rotForOffset * localOffset;
        Vector3 pos = boneControl.Target.OutGoingData.position + offset;

        // Lock world Y and XZ for torso
        switch (lockPlanar)
        {
            case LockPlanar.ChestXZ_Y:
                pos.y = _chestBaseY;
                pos.x = _chestBaseXZ.x;
                pos.z = _chestBaseXZ.y;
                break;
            case LockPlanar.SpineXZ_Y:
                pos.y = _spineBaseY;
                pos.x = _spineBaseXZ.x;
                pos.z = _spineBaseXZ.y;
                break;
            case LockPlanar.HipsXZ_Y:
                pos.y = _hipsBaseY;
                pos.x = _hipsBaseXZ.x;
                pos.z = _hipsBaseXZ.y;
                break;
        }

        boneControl.OutGoingData.position = pos;
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
