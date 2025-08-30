/*
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

    private Transform _root;

    public void Initialize()
    {
        if (_initialized) return;

        TrySetOverride(BasisLocalBoneDriver.HeadControl, true);
        TrySetOverride(BasisLocalBoneDriver.NeckControl, true);
        TrySetOverride(BasisLocalBoneDriver.ChestControl, true);
        TrySetOverride(BasisLocalBoneDriver.SpineControl, true);
        TrySetOverride(BasisLocalBoneDriver.HipsControl, true);

        if (BasisLocalPlayer.Instance != null)
        {
            BasisLocalPlayer.Instance.OnPreSimulateBones += OnSimulateHead;
            _root = BasisLocalPlayer.Instance.transform;

            // Capture torso baselines in world
            var chest = BasisLocalBoneDriver.ChestControl;
            var spine = BasisLocalBoneDriver.SpineControl;
            var hips = BasisLocalBoneDriver.HipsControl;

            if (chest != null)
            {
                Vector3 w = LocalTposeToWorld(chest);
                _chestBaseY = w.y;
                _chestBaseXZ = new Vector2(w.x, w.z);
            }
            if (spine != null)
            {
                Vector3 w = LocalTposeToWorld(spine);
                _spineBaseY = w.y;
                _spineBaseXZ = new Vector2(w.x, w.z);
            }
            if (hips != null)
            {
                Vector3 w = LocalTposeToWorld(hips);
                _hipsBaseY = w.y;
                _hipsBaseXZ = new Vector2(w.x, w.z);
            }
        }

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

        if (BasisLocalPlayer.Instance != null)
        {
            BasisLocalPlayer.Instance.OnPreSimulateBones -= OnSimulateHead;
        }

        _initialized = false;
    }

    private static void TrySetOverride(BasisLocalBoneControl control, bool enabled)
    {
        if (control != null) control.HasVirtualOverride = enabled;
    }

    public void OnSimulateHead()
    {
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
        Transform rootTransform = player.transform;
        Matrix4x4 parentMatrix = rootTransform.localToWorldMatrix;
        Quaternion rootRotation = rootTransform.rotation;

        // Positions:
        // Head/Neck: full offsets (include pitch) -> keep eyes/head co-located
        // Torso (Chest/Spine/Hips): lock Y and XZ to baseline -> eliminates back/forward creep from head pitch
        ApplyPositionControl(head, parentMatrix, rootRotation, LockPlanar.None);
        ApplyPositionControl(neck, parentMatrix, rootRotation, LockPlanar.None);

        ApplyPositionControl(chest, parentMatrix, rootRotation, LockPlanar.ChestXZ_Y);
        ApplyPositionControl(spine, parentMatrix, rootRotation, LockPlanar.SpineXZ_Y);
        ApplyPositionControl(hips, parentMatrix, rootRotation, LockPlanar.HipsXZ_Y);
    }

    private enum LockPlanar { None, ChestXZ_Y, SpineXZ_Y, HipsXZ_Y }

    private void ApplyPositionControl(BasisLocalBoneControl boneControl, Matrix4x4 parentMatrix, Quaternion rootRotation, LockPlanar lockPlanar)
    {
        if (boneControl == null || boneControl.Target == null)
            return;

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

    private Vector3 LocalTposeToWorld(BasisLocalBoneControl control)
    {
        if (_root == null || control == null) return Vector3.zero;
        return _root.localToWorldMatrix.MultiplyPoint3x4(control.TposeLocalScaled.position);
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
*/
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.TransformBinders.BoneControl;
using Unity.Mathematics;
using UnityEngine;
[System.Serializable]
public class BasisLocalVirtualSpineDriver
{
    [SerializeField] public BasisLocalBoneControl CenterEye;
    [SerializeField] public BasisLocalBoneControl Head;
    [SerializeField] public BasisLocalBoneControl Neck;
    [SerializeField] public BasisLocalBoneControl Chest;
    [SerializeField] public BasisLocalBoneControl Spine;
    [SerializeField] public BasisLocalBoneControl Hips;
    [SerializeField] public BasisLocalBoneControl RightShoulder;
    [SerializeField] public BasisLocalBoneControl LeftShoulder;
    [SerializeField] public BasisLocalBoneControl LeftLowerArm;
    [SerializeField] public BasisLocalBoneControl RightLowerArm;
    [SerializeField] public BasisLocalBoneControl LeftLowerLeg;
    [SerializeField] public BasisLocalBoneControl RightLowerLeg;
    [SerializeField] public BasisLocalBoneControl LeftHand;
    [SerializeField] public BasisLocalBoneControl RightHand;
    [SerializeField] public BasisLocalBoneControl LeftFoot;
    [SerializeField] public BasisLocalBoneControl RightFoot;
    public float NeckRotationSpeed = 40;
    public float ChestRotationSpeed = 25;
    public float SpineRotationSpeed = 30;
    public float HipsRotationSpeed = 40;

    public Vector3 headOffset;          // Eye-to-head in eye local space
    public Vector3 neckOffset;          // Head-to-neck in head local space
    public Vector3 neckEyeOffset;
    public void Initialize()
    {
        var boneDriver = BasisLocalPlayer.Instance.LocalBoneDriver;

        TryAssignBone(BasisBoneTrackedRole.CenterEye, out CenterEye, hasVirtualOverride: false);
        TryAssignBone(BasisBoneTrackedRole.Head, out Head, hasVirtualOverride: true);
        TryAssignBone(BasisBoneTrackedRole.Neck, out Neck, hasVirtualOverride: true);
        TryAssignBone(BasisBoneTrackedRole.Chest, out Chest, hasVirtualOverride: true);
        TryAssignBone(BasisBoneTrackedRole.Spine, out Spine, hasVirtualOverride: true);
        TryAssignBone(BasisBoneTrackedRole.Hips, out Hips, hasVirtualOverride: true);

        TryAssignBone(BasisBoneTrackedRole.LeftLowerArm, out LeftLowerArm, hasVirtualOverride: false);
        TryAssignBone(BasisBoneTrackedRole.RightLowerArm, out RightLowerArm, hasVirtualOverride: false);
        TryAssignBone(BasisBoneTrackedRole.LeftLowerLeg, out LeftLowerLeg, hasVirtualOverride: false);
        TryAssignBone(BasisBoneTrackedRole.RightLowerLeg, out RightLowerLeg, hasVirtualOverride: false);
        TryAssignBone(BasisBoneTrackedRole.LeftHand, out LeftHand, hasVirtualOverride: false);
        TryAssignBone(BasisBoneTrackedRole.RightHand, out RightHand, hasVirtualOverride: false);
        TryAssignBone(BasisBoneTrackedRole.LeftFoot, out LeftFoot, hasVirtualOverride: false);
        TryAssignBone(BasisBoneTrackedRole.RightFoot, out RightFoot, hasVirtualOverride: false);

        BasisLocalPlayer.Instance.OnPreSimulateBones += OnSimulateHead;
    }

    private void TryAssignBone(BasisBoneTrackedRole role, out BasisLocalBoneControl bone, bool hasVirtualOverride)
    {
        var boneDriver = BasisLocalPlayer.Instance.LocalBoneDriver;
        if (boneDriver.FindBone(out bone, role) && hasVirtualOverride)
        {
            bone.HasVirtualOverride = true;
        }
    }
    public void DeInitialize()
    {
        if (Neck != null)
        {
            Neck.HasVirtualOverride = false;
        }
        if (Chest != null)
        {
            Chest.HasVirtualOverride = false;
        }
        if (Hips != null)
        {
            Hips.HasVirtualOverride = false;
        }
        if (Spine != null)
        {
            Spine.HasVirtualOverride = false;
        }
        BasisLocalPlayer.Instance.OnPreSimulateBones -= OnSimulateHead;
    }
    public void OnSimulateHead()
    {
        float deltaTime = Time.deltaTime;

        Head.OutGoingData.rotation = CenterEye.OutGoingData.rotation;
        Neck.OutGoingData.rotation = Head.OutGoingData.rotation;

        // Now, apply the spine curve progressively:
        // The chest should not follow the head directly, it should follow the neck but with reduced influence.
        Quaternion targetChestRotation = Quaternion.Slerp(Chest.OutGoingData.rotation, Neck.OutGoingData.rotation, deltaTime * ChestRotationSpeed);
        Vector3 EulerChestRotation = targetChestRotation.eulerAngles;
        Chest.OutGoingData.rotation = Quaternion.Euler(0, EulerChestRotation.y, 0);

        // The hips should stay upright, using chest rotation as a reference
        Quaternion targetSpineRotation = Quaternion.Slerp(Spine.OutGoingData.rotation, Chest.OutGoingData.rotation, deltaTime * SpineRotationSpeed);// Lesser influence for hips to remain more upright
        Vector3 targetSpineRotationEuler = targetSpineRotation.eulerAngles;
        Spine.OutGoingData.rotation = Quaternion.Euler(0, targetSpineRotationEuler.y, 0);

        // The hips should stay upright, using chest rotation as a reference
        Quaternion targetHipsRotation = Quaternion.Slerp(Hips.OutGoingData.rotation, Spine.OutGoingData.rotation, deltaTime * HipsRotationSpeed);// Lesser influence for hips to remain more upright
        Vector3 targetHipsRotationEuler = targetHipsRotation.eulerAngles;
        Hips.OutGoingData.rotation = Quaternion.Euler(0, targetHipsRotationEuler.y, 0);

        Transform transform = BasisLocalPlayer.Instance.transform;
        Matrix4x4 parentMatrix = transform.localToWorldMatrix;
        Quaternion Rotation = transform.rotation;
        // Handle position control for each segment if targets are set (as before)
        ApplyPositionControl(Head, parentMatrix, Rotation);
        ApplyPositionControl(Neck, parentMatrix, Rotation);
        ApplyPositionControl(Chest, parentMatrix, Rotation);
        ApplyPositionControl(Spine, parentMatrix, Rotation);
        ApplyPositionControl(Hips, parentMatrix, Rotation);
    }
    private void ApplyPositionControl(BasisLocalBoneControl boneControl, Matrix4x4 parentMatrix, Quaternion Rotation)
    {
        Quaternion targetRotation = boneControl.Target.OutGoingData.rotation;

        // Extract yaw-only forward vector
        Vector3 forward = targetRotation * Vector3.forward;
        forward.y = 0f;
        forward = forward.normalized;

        Quaternion yawRotation = Quaternion.LookRotation(forward, Vector3.up);
        Vector3 offset = yawRotation * boneControl.ScaledOffset;

        boneControl.OutGoingData.position = boneControl.Target.OutGoingData.position + offset;
        boneControl.ApplyWorldAndLast(parentMatrix, Rotation);

    }
}
