using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.TransformBinders.BoneControl;
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

    [Header("Rotation Speeds (deg/s)")]
    public float NeckRotationSpeed = 40f;
    public float ChestRotationSpeed = 25f;
    public float SpineRotationSpeed = 30f;
    public float HipsRotationSpeed = 40f;

    [Header("Look-Down Anti-Thrust (Chest/Spine)")]
    [Tooltip("Start reducing forward chest translation after this down-look pitch (degrees).")]
    public float LookDownStartDeg = 10f;
    [Tooltip("Max down-look pitch where anti-thrust fully applies (degrees).")]
    public float LookDownMaxDeg = 100f;
    [Tooltip("How much the chest is held back at max down-look (meters).")]
    public float ChestLookDownBackOffset = 0.05f;
    [Tooltip("How much the spine is held back at max down-look (meters).")]
    public float SpineLookDownBackOffset = 0.03f;

    [Header("Position Damping")]
    [Tooltip("0 = no damping, higher = smoother. Recommended small value like 10-16.")]
    public float PositionDamping = 50f;

    [Header("Neck Look-Down Protection")]
    [Tooltip("Base fraction of head pitch/roll the neck follows (1 = full, 0 = yaw only).")]
    public float NeckPitchFollow = 0.4f;
    [Tooltip("How much to reduce pitch follow at max down-look (0..1, 1 = remove it all).")]
    public float NeckPitchFollowReductionAtMax = 1.0f;
    [Tooltip("At max down-look, push neck back along head -forward by this amount (m).")]
    public float NeckLookDownBackOffset = 0.05f;
    [Tooltip("At max down-look, drop neck down (world -Y) by this amount (m).")]
    public float NeckLookDownDownOffset = 0.01f;
    [Tooltip("Minimum required distance the neck must sit behind the head along head-forward (m).")]
    public float NeckMinDistanceBehindHead = 0;

    // Optional offsets if you want them, but not used directly here
    public Vector3 headOffset;   // Eye-to-head in eye local space
    public Vector3 neckOffset;   // Head-to-neck in head local space
    public Vector3 neckEyeOffset;

    // Internal state for simple critically-damped smoothing per bone
    private Vector3 _velHead, _velNeck, _velChest, _velSpine, _velHips;

    // --- NaN-safe helpers ----------------------------------------------------

    private static bool IsFinite(Vector3 v)
        => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

    private static bool IsFinite(Quaternion q)
        => float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w);

    private static Quaternion SanitizeRotation(Quaternion q, Quaternion fallback)
    {
        // Unity quaternions should be normalized. A zero-length (0,0,0,0) or NaN will explode math.
        if (!IsFinite(q)) return fallback;
        float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (mag < 1e-6f) return fallback;
        q.x /= mag; q.y /= mag; q.z /= mag; q.w /= mag;
        return q;
    }

    private static Vector3 SanitizeVector(Vector3 v, Vector3 fallback)
        => IsFinite(v) ? v : fallback;

    private static void SanitizeVel(ref Vector3 v)
    {
        if (!IsFinite(v)) v = Vector3.zero;
    }

    private static float SafeDeltaTime()
    {
        float dt = Time.deltaTime;
        if (!float.IsFinite(dt) || dt < 0f) dt = 0f;
        return dt;
    }

    // -------------------------------------------------------------------------

    public void Initialize()
    {
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

        WarmStartRotationsAndPositions();

        BasisLocalPlayer.Instance.OnPreSimulateBones += OnSimulateHead;
    }

    private void TryAssignBone(BasisBoneTrackedRole role, out BasisLocalBoneControl bone, bool hasVirtualOverride)
    {
        bone = null;
        var boneDriver = BasisLocalPlayer.Instance.LocalBoneDriver;
        if (boneDriver != null && boneDriver.FindBone(out bone, role) && hasVirtualOverride && bone != null)
        {
            bone.HasVirtualOverride = true;
        }
    }

    private void WarmStartRotationsAndPositions()
    {
        // Ensure all OutGoingData have valid (finite, normalized) rotations and positions before first tick.
        // Use Target as fallback, otherwise identity / zero.
        InitializeBoneSafe(Head);
        InitializeBoneSafe(Neck);
        InitializeBoneSafe(Chest);
        InitializeBoneSafe(Spine);
        InitializeBoneSafe(Hips);

        // Reset velocities to a clean state
        _velHead = Vector3.zero;
        _velNeck = Vector3.zero;
        _velChest = Vector3.zero;
        _velSpine = Vector3.zero;
        _velHips = Vector3.zero;
    }

    private void InitializeBoneSafe(BasisLocalBoneControl bone)
    {
        if (bone == null) return;

        var tgtRot = bone.Target != null ? bone.Target.OutGoingData.rotation : Quaternion.identity;
        var tgtPos = bone.Target != null ? bone.Target.OutGoingData.position : Vector3.zero;

        bone.OutGoingData.rotation = SanitizeRotation(bone.OutGoingData.rotation, SanitizeRotation(tgtRot, Quaternion.identity));
        bone.OutGoingData.position = SanitizeVector(bone.OutGoingData.position, SanitizeVector(tgtPos, Vector3.zero));
    }

    public void DeInitialize()
    {
        if (Neck != null) Neck.HasVirtualOverride = false;
        if (Chest != null) Chest.HasVirtualOverride = false;
        if (Hips != null) Hips.HasVirtualOverride = false;
        if (Spine != null) Spine.HasVirtualOverride = false;

        BasisLocalPlayer.Instance.OnPreSimulateBones -= OnSimulateHead;
    }

    public void OnSimulateHead()
    {
        if (CenterEye == null || Head == null || Neck == null || Chest == null || Spine == null || Hips == null)
            return; // Defensive: required bones missing

        float dt = SafeDeltaTime();

        // Base: head copies HMD orientation (sanitize to avoid invalid quaternions from upstream)
        Quaternion centerEyeRot = SanitizeRotation(CenterEye.OutGoingData.rotation, Quaternion.identity);
        Head.OutGoingData.rotation = centerEyeRot;

        // --- compute look-down amount (0..1) from head forward vector ---
        float headPitchDeg = GetPitchFromForward(Head.OutGoingData.rotation);
        float downAmt = 0f;
        if (headPitchDeg < -LookDownStartDeg)
        {
            downAmt = Mathf.InverseLerp(-LookDownStartDeg, -LookDownMaxDeg, headPitchDeg);
        }
        downAmt = Mathf.Clamp01(downAmt);

        // --- Neck rotation: adaptively reduce pitch/roll follow when looking down ---
        float baseFollow = Mathf.Clamp01(NeckPitchFollow);
        float reduction = Mathf.Clamp01(NeckPitchFollowReductionAtMax) * downAmt;
        float effectiveFollow = Mathf.Clamp01(baseFollow * (1f - reduction));

        Quaternion headRot = Head.OutGoingData.rotation;
        headRot = SanitizeRotation(headRot, Quaternion.identity);

        Quaternion neckYawOnly = YawOnly(headRot);
        Quaternion neckCurrent = SanitizeRotation(Neck.OutGoingData.rotation, neckYawOnly);
        Quaternion neckTargetRot = Quaternion.Slerp(neckYawOnly, headRot, effectiveFollow);
        neckTargetRot = SanitizeRotation(neckTargetRot, neckYawOnly);
        Neck.OutGoingData.rotation = Quaternion.Slerp(neckCurrent, neckTargetRot, dt * Mathf.Max(0f, NeckRotationSpeed));

        // Neck now copies head yaw behavior for downstream yaw distribution
        Quaternion neckEffectiveForChain = SanitizeRotation(Neck.OutGoingData.rotation, neckYawOnly);

        // --- YAW ONLY distribution down the chain ---
        // Chest follows neck yaw with slerped damping, then zero pitch/roll
        Quaternion chestCur = SanitizeRotation(Chest.OutGoingData.rotation, neckEffectiveForChain);
        Quaternion chestTarget = Quaternion.Slerp(chestCur, neckEffectiveForChain, dt * Mathf.Max(0f, ChestRotationSpeed));
        Chest.OutGoingData.rotation = YawOnly(chestTarget);

        // Spine follows chest yaw
        Quaternion spineCur = SanitizeRotation(Spine.OutGoingData.rotation, Chest.OutGoingData.rotation);
        Quaternion spineTarget = Quaternion.Slerp(spineCur, Chest.OutGoingData.rotation, dt * Mathf.Max(0f, SpineRotationSpeed));
        Spine.OutGoingData.rotation = YawOnly(spineTarget);

        // Hips follow spine yaw a bit
        Quaternion hipsCur = SanitizeRotation(Hips.OutGoingData.rotation, Spine.OutGoingData.rotation);
        Quaternion hipsTarget = Quaternion.Slerp(hipsCur, Spine.OutGoingData.rotation, dt * Mathf.Max(0f, HipsRotationSpeed));
        Hips.OutGoingData.rotation = YawOnly(hipsTarget);

        // --- Position control ---
        Transform playerTf = BasisLocalPlayer.Instance != null ? BasisLocalPlayer.Instance.transform : null;
        if (playerTf == null) return;

        Matrix4x4 parentMatrix = playerTf.localToWorldMatrix;
        Quaternion playerWorldRotation = SanitizeRotation(playerTf.rotation, Quaternion.identity);

        // Backward correction for chest/spine along player's forward
        Vector3 playerForward = playerWorldRotation * Vector3.forward;
        playerForward = SanitizeVector(playerForward, Vector3.forward).normalized;

        Vector3 chestBack = -playerForward * (ChestLookDownBackOffset * downAmt);
        Vector3 spineBack = -playerForward * (SpineLookDownBackOffset * downAmt);

        // Head position (no extra)
        ApplyPositionControl(Head, parentMatrix, playerWorldRotation, Vector3.zero, ref _velHead, dt);

        // Neck position with look-down offsets in WORLD space:
        Vector3 headForwardWS = (headRot * Vector3.forward).normalized;
        if (!IsFinite(headForwardWS) || headForwardWS.sqrMagnitude < 1e-8f) headForwardWS = playerForward;

        Vector3 neckExtraWS =
            (-headForwardWS * (NeckLookDownBackOffset * downAmt)) +
            (Vector3.down * (NeckLookDownDownOffset * downAmt));

        ApplyPositionControl(Neck, parentMatrix, playerWorldRotation, neckExtraWS, ref _velNeck, dt);

        // Enforce a minimum separation along head-forward
        EnforceNeckBehindHeadMinDistance(
            headPos: SanitizeVector(Head.OutGoingData.position, Vector3.zero),
            headForward: headForwardWS,
            neckControl: Neck,
            minBehind: Mathf.Max(0f, NeckMinDistanceBehindHead),
            parentMatrix: parentMatrix,
            playerWorldRotation: playerWorldRotation
        );

        // Chest/Spine with anti-thrust offsets
        ApplyPositionControl(Chest, parentMatrix, playerWorldRotation, chestBack, ref _velChest, dt);
        ApplyPositionControl(Spine, parentMatrix, playerWorldRotation, spineBack, ref _velSpine, dt);

        // Hips: no extra
        ApplyPositionControl(Hips, parentMatrix, playerWorldRotation, Vector3.zero, ref _velHips, dt);
    }

    // --- Helpers ---

    private static Quaternion YawOnly(Quaternion q)
    {
        q = SanitizeRotation(q, Quaternion.identity);
        // Compute yaw robustly (avoid Euler on invalid input)
        Vector3 f = q * Vector3.forward;
        f.y = 0f;
        if (!float.IsFinite(f.x) || !float.IsFinite(f.z) || f.sqrMagnitude < 1e-8f)
            return Quaternion.identity;
        f.Normalize();
        return Quaternion.LookRotation(f, Vector3.up);
    }

    /// <summary>
    /// Robust pitch in degrees from the forward vector (negative when looking down).
    /// </summary>
    private static float GetPitchFromForward(Quaternion rot)
    {
        rot = SanitizeRotation(rot, Quaternion.identity);
        Vector3 f = rot * Vector3.forward;
        if (!IsFinite(f) || f.sqrMagnitude < 1e-12f) return 0f;
        f.Normalize();
        float pitchRad = Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f));
        return pitchRad * Mathf.Rad2Deg;
    }

    /// <summary>
    /// Positions the bone at Target.position + yaw(frames) * ScaledOffset, plus an optional world-space extraOffset,
    /// then applies a critically-damped smoothing before writing to OutGoingData and calling ApplyWorldAndLast.
    /// NaN-safe throughout.
    /// </summary>
    private void ApplyPositionControl(
        BasisLocalBoneControl boneControl,
        Matrix4x4 parentMatrix,
        Quaternion playerWorldRotation,
        Vector3 extraWorldOffset,
        ref Vector3 velocity, // for damping
        float dt)
    {
        if (boneControl == null || boneControl.Target == null) return;

        // Yaw-only frame derived from the target’s rotation
        Quaternion targetRot = SanitizeRotation(boneControl.Target.OutGoingData.rotation, Quaternion.identity);
        Vector3 forward = targetRot * Vector3.forward;
        forward.y = 0f;

        Quaternion yawRot;
        if (!IsFinite(forward) || forward.sqrMagnitude < 1e-8f)
        {
            Vector3 pf = playerWorldRotation * Vector3.forward;
            if (!IsFinite(pf) || pf.sqrMagnitude < 1e-8f) pf = Vector3.forward;
            yawRot = Quaternion.LookRotation(pf.normalized, Vector3.up);
        }
        else
        {
            forward.Normalize();
            yawRot = Quaternion.LookRotation(forward, Vector3.up);
        }

        Vector3 scaledOffset = SanitizeVector(boneControl.ScaledOffset, Vector3.zero);
        Vector3 extra = SanitizeVector(extraWorldOffset, Vector3.zero);

        Vector3 targetPos = SanitizeVector(boneControl.Target.OutGoingData.position, Vector3.zero);
        Vector3 desired = targetPos + (yawRot * scaledOffset) + extra;

        // Smooth towards desired in world space (critically damped-like)
        float smooth = Mathf.Max(0f, PositionDamping);
        Vector3 current = SanitizeVector(boneControl.OutGoingData.position, targetPos);

        SanitizeVel(ref velocity);

        Vector3 next = (smooth > 0f && dt > 0f)
            ? Vector3.SmoothDamp(current, desired, ref velocity, 1f / smooth, Mathf.Infinity, dt)
            : desired;

        if (!IsFinite(next)) next = desired; // final guard

        boneControl.OutGoingData.position = next;
        boneControl.ApplyWorldAndLast(parentMatrix, playerWorldRotation);
    }

    /// <summary>
    /// Ensures the neck remains at least 'minBehind' meters behind the head along head-forward.
    /// If the neck creeps in front (toward the face), push it back and apply.
    /// </summary>
    private void EnforceNeckBehindHeadMinDistance(
        Vector3 headPos,
        Vector3 headForward,
        BasisLocalBoneControl neckControl,
        float minBehind,
        Matrix4x4 parentMatrix,
        Quaternion playerWorldRotation)
    {
        if (neckControl == null) return;

        headForward = SanitizeVector(headForward, Vector3.forward);
        if (headForward.sqrMagnitude < 1e-12f) headForward = Vector3.forward;
        headForward.Normalize();

        Vector3 neckPos = SanitizeVector(neckControl.OutGoingData.position, headPos);

        // proj >= minBehind desired
        float proj = Vector3.Dot(headPos - neckPos, headForward);
        if (!float.IsFinite(proj)) proj = minBehind;

        if (proj < minBehind)
        {
            Vector3 targetNeck = headPos - headForward * minBehind;
            if (IsFinite(targetNeck))
            {
                neckControl.OutGoingData.position = targetNeck;
                neckControl.ApplyWorldAndLast(parentMatrix, playerWorldRotation);
            }
        }
    }
}
