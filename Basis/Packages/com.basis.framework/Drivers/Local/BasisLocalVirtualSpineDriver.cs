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

    [Header("Rotation Speeds (deg/s)")]
    public float NeckRotationSpeed = 40f;
    public float ChestRotationSpeed = 25f;
    public float SpineRotationSpeed = 30f;
    public float HipsRotationSpeed = 40f;

    [Header("Look-Down Anti-Thrust (Chest/Spine)")]
    [Tooltip("Start reducing forward chest translation after this down-look pitch (degrees).")]
    public float LookDownStartDeg = 10f;
    [Tooltip("Max down-look pitch where anti-thrust fully applies (degrees).")]
    public float LookDownMaxDeg = 90;
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
    public float NeckLookDownBackOffset = 0.1f;
    [Tooltip("At max down-look, drop neck down (world -Y) by this amount (m).")]
    public float NeckLookDownDownOffset = 0.01f;
    [Tooltip("Minimum required distance the neck must sit behind the head along head-forward (m).")]
    public float NeckMinDistanceBehindHead = -0.1f;

    [Header("Bone Separation Safety (prevents merging)")]
    [Tooltip("Minimum 3D separation between Neck and Chest (meters).")]
    public float NeckChestMinDistance = 0.04f;
    [Tooltip("Minimum 3D separation between Chest and Spine (meters).")]
    public float ChestSpineMinDistance = 0.05f;
    [Tooltip("Minimum 3D separation between Spine and Hips (meters).")]
    public float SpineHipsMinDistance = 0.06f;
    [Tooltip("How many solver passes to enforce separations each frame (1-3 is plenty).")]
    [Range(1, 4)] public int SeparationSolverPasses = 2;

    // FIX: cap anti-thrust so it never fully cancels forward separation
    [Header("Anti-Thrust Saturation")]
    [Tooltip("Never cancel more than this fraction of the bone’s forward offset.")]
    [Range(0.5f, 0.95f)] public float AntiThrustMaxFractionOfForward = 0.8f;

    // Optional offsets if you want them, but not used directly here
    public Vector3 headOffset;   // Eye-to-head in eye local space
    public Vector3 neckOffset;   // Head-to-neck in head local space
    public Vector3 neckEyeOffset;

    // Internal state for simple critically-damped smoothing per bone
    private Vector3 _velHead, _velNeck, _velChest, _velSpine, _velHips;

    // --- NaN-safe helpers ----------------------------------------------------

    private static bool IsFinite(float f) => !(float.IsNaN(f) || float.IsInfinity(f));
    private static bool IsFinite(Vector3 v) => IsFinite(v.x) && IsFinite(v.y) && IsFinite(v.z);
    private static bool IsFinite(Quaternion q) => IsFinite(q.x) && IsFinite(q.y) && IsFinite(q.z) && IsFinite(q.w);

    private static Quaternion SanitizeRotation(Quaternion q, Quaternion fallback)
    {
        if (!IsFinite(q)) return fallback;
        float mag = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        if (!IsFinite(mag) || mag < 1e-6f) return fallback;
        float inv = 1.0f / mag;
        q.x *= inv; q.y *= inv; q.z *= inv; q.w *= inv;
        return q;
    }

    private static Vector3 SanitizeVector(Vector3 v, Vector3 fallback) => IsFinite(v) ? v : fallback;

    private static void SanitizeVel(ref Vector3 v)
    {
        if (!IsFinite(v)) v = Vector3.zero;
        float maxReasonable = 1000f;
        float mag = v.magnitude;
        if (!IsFinite(mag)) { v = Vector3.zero; return; }
        if (mag > maxReasonable) v = v.normalized * maxReasonable;
    }

    private static float SafeDeltaTime()
    {
        float dt = Time.deltaTime;
        if (!IsFinite(dt) || dt < 0f) dt = 0f;
        if (dt > 1f / 15f) dt = 1f / 15f; // ~66ms
        return dt;
    }

    private static Vector3 SafeSmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime, float deltaTime, float maxSpeed = Mathf.Infinity)
    {
        current = SanitizeVector(current, Vector3.zero);
        target = SanitizeVector(target, current);
        SanitizeVel(ref currentVelocity);

        if (!IsFinite(smoothTime) || smoothTime <= 1e-6f) smoothTime = 0.000001f;
        if (!IsFinite(deltaTime) || deltaTime < 0f) deltaTime = 0f;

        if (!IsFinite(maxSpeed) || maxSpeed <= 0f) maxSpeed = 1000f;
        maxSpeed = Mathf.Min(maxSpeed, 10000f);

        Vector3 delta = target - current;
        float maxDelta = maxSpeed * smoothTime * 4f;
        float dist = delta.magnitude;
        if (IsFinite(dist) && dist > maxDelta)
        {
            delta = delta * (maxDelta / dist);
            target = current + delta;
        }

        Vector3 next = Vector3.SmoothDamp(current, target, ref currentVelocity, smoothTime, maxSpeed, deltaTime);

        if (!IsFinite(next))
        {
            currentVelocity = Vector3.zero;
            next = target;
        }

        return next;
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
        InitializeBoneSafe(Head);
        InitializeBoneSafe(Neck);
        InitializeBoneSafe(Chest);
        InitializeBoneSafe(Spine);
        InitializeBoneSafe(Hips);

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
            return;

        float dt = SafeDeltaTime();

        // Base: head copies HMD orientation
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

        Quaternion headRot = SanitizeRotation(Head.OutGoingData.rotation, Quaternion.identity);

        Quaternion neckYawOnly = YawOnly(headRot);
        Quaternion neckCurrent = SanitizeRotation(Neck.OutGoingData.rotation, neckYawOnly);
        Quaternion neckTargetRot = Quaternion.Slerp(neckYawOnly, headRot, effectiveFollow);
        neckTargetRot = SanitizeRotation(neckTargetRot, neckYawOnly);
        Neck.OutGoingData.rotation = Quaternion.Slerp(neckCurrent, neckTargetRot, dt * Mathf.Max(0f, NeckRotationSpeed));

        // Neck now copies head yaw behavior for downstream yaw distribution
        Quaternion neckEffectiveForChain = SanitizeRotation(Neck.OutGoingData.rotation, neckYawOnly);

        // --- YAW ONLY distribution down the chain ---
        Quaternion chestCur = SanitizeRotation(Chest.OutGoingData.rotation, neckEffectiveForChain);
        Quaternion chestTarget = Quaternion.Slerp(chestCur, neckEffectiveForChain, dt * Mathf.Max(0f, ChestRotationSpeed));
        Chest.OutGoingData.rotation = YawOnly(chestTarget);

        Quaternion spineCur = SanitizeRotation(Spine.OutGoingData.rotation, Chest.OutGoingData.rotation);
        Quaternion spineTarget = Quaternion.Slerp(spineCur, Chest.OutGoingData.rotation, dt * Mathf.Max(0f, SpineRotationSpeed));
        Spine.OutGoingData.rotation = YawOnly(spineTarget);

        Quaternion hipsCur = SanitizeRotation(Hips.OutGoingData.rotation, Spine.OutGoingData.rotation);
        Quaternion hipsTarget = Quaternion.Slerp(hipsCur, Spine.OutGoingData.rotation, dt * Mathf.Max(0f, HipsRotationSpeed));
        Hips.OutGoingData.rotation = YawOnly(hipsTarget);

        // --- Position control ---
        Transform playerTf = BasisLocalPlayer.Instance != null ? BasisLocalPlayer.Instance.transform : null;
        if (playerTf == null) return;

        Matrix4x4 parentMatrix = playerTf.localToWorldMatrix;
        Quaternion playerWorldRotation = SanitizeRotation(playerTf.rotation, Quaternion.identity);

        Vector3 playerForward = playerWorldRotation * Vector3.forward;
        playerForward = SanitizeVector(playerForward, Vector3.forward).normalized;

        // FIX: compute *saturated* pushback per bone so it cannot fully cancel forward separation

        // Neck extra offsets (unchanged)
        Vector3 headForwardWS = (headRot * Vector3.forward).normalized;
        if (!IsFinite(headForwardWS) || headForwardWS.sqrMagnitude < 1e-8f) headForwardWS = playerForward;
        Vector3 neckExtraWS =
            (-headForwardWS * (NeckLookDownBackOffset * downAmt)) +
            (Vector3.down * (NeckLookDownDownOffset * downAmt));

        // Compute chest anti-thrust with saturation
        Vector3 chestBack = Vector3.zero;
        if (Chest != null && Chest.Target != null)
        {
            Quaternion tgtRot = SanitizeRotation(Chest.Target.OutGoingData.rotation, Quaternion.identity);
            Vector3 f = tgtRot * Vector3.forward; f.y = 0f;
            Quaternion chestYaw = (!IsFinite(f) || f.sqrMagnitude < 1e-8f)
                ? Quaternion.LookRotation(playerForward, Vector3.up)
                : Quaternion.LookRotation(f.normalized, Vector3.up);

            Vector3 chestOffsetWS = chestYaw * SanitizeVector(Chest.ScaledOffset, Vector3.zero);

            // forward component along player forward (what anti-thrust counters)
            float chestForwardDist = Mathf.Max(0f, Vector3.Dot(chestOffsetWS, playerForward));
            float desiredBackMag = ChestLookDownBackOffset * downAmt;

            // Do not cancel more than the forward dist minus a safety buffer
            float safety = Mathf.Max(0f, NeckChestMinDistance * 0.9f);
            float capByForward = Mathf.Max(0f, chestForwardDist - safety);

            // Also cap to a fraction so we always keep some forward separation
            float capByFraction = chestForwardDist * Mathf.Clamp01(AntiThrustMaxFractionOfForward);

            float backMag = Mathf.Min(desiredBackMag, capByForward, capByFraction);
            chestBack = -playerForward * backMag;
        }

        // Compute spine anti-thrust with saturation
        Vector3 spineBack = Vector3.zero;
        if (Spine != null && Spine.Target != null)
        {
            Quaternion tgtRot = SanitizeRotation(Spine.Target.OutGoingData.rotation, Quaternion.identity);
            Vector3 f = tgtRot * Vector3.forward; f.y = 0f;
            Quaternion spineYaw = (!IsFinite(f) || f.sqrMagnitude < 1e-8f)
                ? Quaternion.LookRotation(playerForward, Vector3.up)
                : Quaternion.LookRotation(f.normalized, Vector3.up);

            Vector3 spineOffsetWS = spineYaw * SanitizeVector(Spine.ScaledOffset, Vector3.zero);
            float spineForwardDist = Mathf.Max(0f, Vector3.Dot(spineOffsetWS, playerForward));
            float desiredBackMag = SpineLookDownBackOffset * downAmt;

            float safety = Mathf.Max(0f, ChestSpineMinDistance * 0.9f);
            float capByForward = Mathf.Max(0f, spineForwardDist - safety);
            float capByFraction = spineForwardDist * Mathf.Clamp01(AntiThrustMaxFractionOfForward);

            float backMag = Mathf.Min(desiredBackMag, capByForward, capByFraction);
            spineBack = -playerForward * backMag;
        }

        // Head
        ApplyPositionControl(Head, parentMatrix, playerWorldRotation, Vector3.zero, ref _velHead, dt);

        // Neck
        ApplyPositionControl(Neck, parentMatrix, playerWorldRotation, neckExtraWS, ref _velNeck, dt);

        EnforceNeckBehindHeadMinDistance(
            headPos: SanitizeVector(Head.OutGoingData.position, Vector3.zero),
            headForward: headForwardWS,
            neckControl: Neck,
            minBehind: Mathf.Max(0f, NeckMinDistanceBehindHead),
            parentMatrix: parentMatrix,
            playerWorldRotation: playerWorldRotation
        );

        // Chest / Spine / Hips (with saturated anti-thrust)
        ApplyPositionControl(Chest, parentMatrix, playerWorldRotation, chestBack, ref _velChest, dt);
        ApplyPositionControl(Spine, parentMatrix, playerWorldRotation, spineBack, ref _velSpine, dt);
        ApplyPositionControl(Hips, parentMatrix, playerWorldRotation, Vector3.zero, ref _velHips, dt);

        // --- Separation constraints ---
        int passes = Mathf.Clamp(SeparationSolverPasses, 1, 4);
        for (int i = 0; i < passes; i++)
        {
            EnforceMinSeparation(
                upper: Neck,
                lower: Chest,
                minDistance: Mathf.Max(0f, NeckChestMinDistance),
                parentMatrix: parentMatrix,
                playerWorldRotation: playerWorldRotation);

            EnforceMinSeparation(
                upper: Chest,
                lower: Spine,
                minDistance: Mathf.Max(0f, ChestSpineMinDistance),
                parentMatrix: parentMatrix,
                playerWorldRotation: playerWorldRotation);

            EnforceMinSeparation(
                upper: Spine,
                lower: Hips,
                minDistance: Mathf.Max(0f, SpineHipsMinDistance),
                parentMatrix: parentMatrix,
                playerWorldRotation: playerWorldRotation);
        }
    }

    // --- Helpers ---

    private static Quaternion YawOnly(Quaternion q)
    {
        q = SanitizeRotation(q, Quaternion.identity);
        Vector3 f = q * Vector3.forward;
        f.y = 0f;
        if (!IsFinite(f.x) || !IsFinite(f.z) || f.sqrMagnitude < 1e-8f)
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
    /// then applies a NaN-safe critically-damped smoothing before writing to OutGoingData and calling ApplyWorldAndLast.
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

        float smooth = Mathf.Max(0f, PositionDamping);

        Vector3 current = SanitizeVector(boneControl.OutGoingData.position, targetPos);
        SanitizeVel(ref velocity);

        Vector3 next;
        if (smooth > 0f && dt > 0f)
        {
            float smoothTime = 1f / Mathf.Max(1e-4f, smooth);
            next = SafeSmoothDamp(current, desired, ref velocity, smoothTime, dt, 10000f);
        }
        else
        {
            velocity = Vector3.zero;
            next = desired;
        }

        if (!IsFinite(next))
        {
            next = desired;
            velocity = Vector3.zero;
        }

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

        float proj = Vector3.Dot(headPos - neckPos, headForward);
        if (!IsFinite(proj)) proj = minBehind;

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

    /// <summary>
    /// Enforce that 'lower' stays at least 'minDistance' meters away from 'upper'.
    /// Only moves the LOWER bone, then applies.
    /// </summary>
    private void EnforceMinSeparation(
        BasisLocalBoneControl upper,
        BasisLocalBoneControl lower,
        float minDistance,
        Matrix4x4 parentMatrix,
        Quaternion playerWorldRotation)
    {
        if (upper == null || lower == null) return;

        Vector3 upPos = SanitizeVector(upper.OutGoingData.position, Vector3.zero);
        Vector3 loPos = SanitizeVector(lower.OutGoingData.position, upPos);

        if (!IsFinite(upPos) || !IsFinite(loPos) || minDistance <= 0f) return;

        Vector3 delta = loPos - upPos;
        float sqrDist = delta.sqrMagnitude;

        float minSqr = minDistance * minDistance;
        if (!(sqrDist >= minSqr))
        {
            Vector3 dir;
            if (sqrDist < 1e-12f)
            {
                Quaternion upRot = SanitizeRotation(upper.OutGoingData.rotation, Quaternion.identity);
                Vector3 fwd = upRot * Vector3.forward;
                fwd.y = 0f;
                if (!IsFinite(fwd) || fwd.sqrMagnitude < 1e-6f)
                {
                    fwd = (playerWorldRotation * Vector3.forward);
                    if (!IsFinite(fwd) || fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                }
                dir = fwd.normalized;
            }
            else
            {
                dir = delta / Mathf.Sqrt(Mathf.Max(sqrDist, 1e-12f));
            }

            Vector3 corrected = upPos + dir * minDistance;
            if (IsFinite(corrected))
            {
                lower.OutGoingData.position = corrected;
                lower.ApplyWorldAndLast(parentMatrix, playerWorldRotation);
            }
        }
    }
}
