using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using System;
using UnityEngine;

/// <summary>
/// Procedural foot placement with velocity-predicted stepping.
/// Nearly all parameters are derived from T-pose calibration data so the system
/// automatically adapts to any avatar's proportions.
/// </summary>
[Serializable]
public class BasisLocalFootDriver
{
    // ───────── Minimal tuning (proportional multipliers, not absolute values) ─────────

    [Header("Ground Detection")]
    private LayerMask groundLayers;

    [Header("Prediction")]
    [Tooltip("How far ahead (in step-durations) to predict the step target.")]
    [SerializeField, Range(0.3f, 2.0f)] private float predictionFactor = 0.9f;
    [Tooltip("Velocity bias on ideal position (fraction of velocity applied as forward offset).")]
    [SerializeField, Range(0.0f, 0.5f)] private float velocityBiasFactor = 0.18f;

    [Header("Smoothing")]
    [SerializeField, Range(5f, 60f)] private float plantedLerpSpeed = 40f;
    [SerializeField, Range(5f, 40f)] private float rotationLerpSpeed = 16f;

    [Header("Foot Rotation Limits")]
    [Tooltip("Max slope tilt (ankle roll/pitch).")]
    [SerializeField, Range(0f, 60f)] private float maxFootTiltDegrees = 35f;
    [Tooltip("Max yaw deviation from body forward (toe-out / toe-in). Humans ~15-20 deg.")]
    [SerializeField, Range(0f, 45f)] private float maxFootYawDegrees = 18f;

    // ───────── Calibrated proportions (computed, visible for debugging) ─────────

    [Header("Calibrated (read-only, from T-pose)")]
    [SerializeField] private float stanceWidth;
    [SerializeField] private float hipToFoot;
    [SerializeField] private float leftLegLen;
    [SerializeField] private float rightLegLen;
    [SerializeField] private float leftThighLen;
    [SerializeField] private float leftShinLen;
    [SerializeField] private float rightThighLen;
    [SerializeField] private float rightShinLen;
    [SerializeField] private float footLength;       // toe-to-heel
    [SerializeField] private float ankleHeight;      // foot-to-ground in T-pose

    // ───────── Base (unscaled) measurements for scale adaptation ─────────

    private float baseStanceWidth;
    private float baseHipToFoot;
    private float baseLeftThighLen, baseLeftShinLen, baseLeftLegLen;
    private float baseRightThighLen, baseRightShinLen, baseRightLegLen;
    private float baseFootLength;
    private float baseAnkleHeight;

    // ───────── Derived step parameters (computed from proportions) ─────────

    [Header("Derived Step Parameters (read-only)")]
    [SerializeField] private float stepTriggerDist;
    [SerializeField] private float strideScale;
    [SerializeField] private float stepHeightCalc;
    [SerializeField] private float stepDurSlow;
    [SerializeField] private float stepDurFast;
    [SerializeField] private float raySphereRadius;
    [SerializeField] private float footHeightOffset;
    [SerializeField] private float fastSpeedRef;

    // ───────── Runtime ─────────

    private Transform avatarTransform;
    private Transform hips;
    private FootState left;
    private FootState right;
    private float rayCastRange;

    private Vector3 prevHeadPos;
    private Vector3 smoothedVelocity;
    private float prevHeadYaw;
    private bool wasDisabled;
    private float lastKnownGroundY;
    private Vector3 smoothedBodyFwd;
    private Vector3 smoothedBodyRight;

    public static float SplayWhenCrouchedPercentage = 1f;
    public bool IsInitialized { get; private set; }

    /// <summary>
    /// Call when the foot driver is about to re-engage after being disabled (e.g., locomotion ended).
    /// Picks up foot positions from where the animation currently has them so there's no snap.
    /// </summary>
    /// <summary>
    /// Called when transitioning from animation to foot IK.
    /// Picks up from where animation has the feet so there's no pop.
    /// </summary>
    public void NotifyReEngaging()
    {
        var lf = BasisLocalBoneDriver.LeftFootControl.OutgoingWorldData;
        left.currentPos = left.plantedPos = lf.position;
        left.currentRot = left.plantedRot = lf.rotation;
        left.phase = Phase.Planted;
        var rf = BasisLocalBoneDriver.RightFootControl.OutgoingWorldData;
        right.currentPos = right.plantedPos = rf.position;
        right.currentRot = right.plantedRot = rf.rotation;
        right.phase = Phase.Planted;
    }
    public Vector3 LeftFootPosition => left.currentPos;
    public Quaternion LeftFootRotation => left.currentRot;
    public Vector3 RightFootPosition => right.currentPos;
    public Quaternion RightFootRotation => right.currentRot;
    public Vector3 LeftKneeHint => left.kneeHint;
    public Vector3 RightKneeHint => right.kneeHint;

    // ───────── Per-foot ─────────

    private enum Phase { Planted, Stepping }

    [Serializable]
    private class FootState
    {
        public string name;
        public Transform bone, thigh, shin;
        public int sideSign;
        public float thighLen, shinLen, legLength;

        public Phase phase;
        public Vector3 plantedPos;
        public Quaternion plantedRot;
        public Vector3 stepStartPos, stepTargetPos;
        public Quaternion stepTargetRot;
        public float stepTimer, stepDur;

        public Vector3 idealPos, filteredNormal;
        public Vector3 currentPos;
        public Quaternion currentRot;
        public Vector3 kneeHint;

        public FootState(string n, Transform b, int s)
        { name = n; bone = b; sideSign = s; filteredNormal = Vector3.up; phase = Phase.Planted; }
    }

    // ═══════════════════════════════════════════════════════════
    //  INIT — measure everything from calibration
    // ═══════════════════════════════════════════════════════════

    public void InitializeVariables()
    {
        BasisLocalPlayer.OnPlayersHeightChangedNextFrame -= OnHeightChanged;

        avatarTransform = BasisLocalPlayer.Instance.AvatarTransform;
        var mapping = BasisLocalAvatarDriver.Mapping;
        hips = mapping.Hips;

        var lf = mapping.leftFoot;
        var rf = mapping.rightFoot;
        left = new FootState("Left", lf, -1);
        right = new FootState("Right", rf, +1);

        left.thigh = mapping.HasLeftUpperLeg ? mapping.LeftUpperLeg : (lf != null ? lf.parent?.parent : null);
        left.shin = mapping.HasLeftLowerLeg ? mapping.LeftLowerLeg : (lf != null ? lf.parent : null);
        right.thigh = mapping.HasRightUpperLeg ? mapping.RightUpperLeg : (rf != null ? rf.parent?.parent : null);
        right.shin = mapping.HasRightLowerLeg ? mapping.RightLowerLeg : (rf != null ? rf.parent : null);

        // Use the same collision layers as the character controller
        var cc = BasisLocalPlayer.Instance.LocalCharacterDriver.characterController;
        int ccLayer = cc.gameObject.layer;
        // Build mask of all layers that collide with the character controller's layer
        int mask = 0;
        for (int i = 0; i < 32; i++)
        {
            if (!Physics.GetIgnoreLayerCollision(ccLayer, i))
            {
                mask |= (1 << i);
            }
        }

        groundLayers = mask;

        // ── 1. Measure avatar from calibration T-pose ──
        MeasureFromCalibration(mapping);
        StoreBaseMeasurements();

        // ── 2. Derive ALL step parameters from measurements ──
        DeriveStepParameters();

        // ── 3. Apply to foot states ──
        left.thighLen = leftThighLen; left.shinLen = leftShinLen; left.legLength = leftLegLen;
        right.thighLen = rightThighLen; right.shinLen = rightShinLen; right.legLength = rightLegLen;

        rayCastRange = Mathf.Max(leftLegLen, rightLegLen) + 0.3f;

        InitPose(left);
        InitPose(right);

        var hc = BasisLocalBoneDriver.HeadControl;
        if (hc == null)
        {
        }
        else
        {
            prevHeadPos = hc.OutgoingWorldData.position;
            prevHeadYaw = HeadYaw();
        }

        smoothedVelocity = Vector3.zero;
        lastKnownGroundY = hips.position.y - hipToFoot;
        smoothedBodyFwd = avatarTransform.forward;
        smoothedBodyRight = Vector3.Cross(Vector3.up, smoothedBodyFwd).normalized;
        BasisLocalPlayer.OnPlayersHeightChangedNextFrame += OnHeightChanged;
        IsInitialized = true;
    }

    private void MeasureFromCalibration(BasisTransformMapping mapping)
    {
        var tpose = mapping.TposeFromRoot;

        Vector3 tH = default, tLUL = default, tRUL = default, tLLL = default, tRLL = default;
        Vector3 tLF = default, tRF = default, tLT = default, tRT = default;

        bool hasHips =TryTP(tpose, HumanBodyBones.Hips, out tH);
        bool hasLUL = TryTP(tpose, HumanBodyBones.LeftUpperLeg, out tLUL);
        bool hasRUL = TryTP(tpose, HumanBodyBones.RightUpperLeg, out tRUL);
        bool hasLLL = TryTP(tpose, HumanBodyBones.LeftLowerLeg, out tLLL);
        bool hasRLL = TryTP(tpose, HumanBodyBones.RightLowerLeg, out tRLL);
        bool hasLF = TryTP(tpose, HumanBodyBones.LeftFoot, out tLF);
        bool hasRF = TryTP(tpose, HumanBodyBones.RightFoot, out tRF);
        bool hasLT = TryTP(tpose, HumanBodyBones.LeftToes, out tLT);
        bool hasRT = TryTP(tpose, HumanBodyBones.RightToes, out tRT);

        // ── Stance width ──
        if (hasLF && hasRF)
        {
            Vector3 d = tRF - tLF; d.y = 0f;
            stanceWidth = d.magnitude;
        }
        else
        {
            FallbackStanceWidth();
        }

        // ── Hip to foot ──
        if (hasHips && hasLF && hasRF)
        {
            hipToFoot = Mathf.Abs(tH.y - (tLF.y + tRF.y) * 0.5f);
        }
        else
        {
            FallbackHipToFoot();
        }

        // ── Leg segment lengths ──
        if (hasLUL && hasLLL && hasLF)
        { leftThighLen = Vector3.Distance(tLUL, tLLL); leftShinLen = Vector3.Distance(tLLL, tLF); }
        else if (hasHips && hasLF)
        { float t = Vector3.Distance(tH, tLF); leftThighLen = t * 0.55f; leftShinLen = t * 0.45f; }
        else FallbackLegLens(true);
        leftLegLen = leftThighLen + leftShinLen;

        if (hasRUL && hasRLL && hasRF)
        { rightThighLen = Vector3.Distance(tRUL, tRLL); rightShinLen = Vector3.Distance(tRLL, tRF); }
        else if (hasHips && hasRF)
        { float t = Vector3.Distance(tH, tRF); rightThighLen = t * 0.55f; rightShinLen = t * 0.45f; }
        else FallbackLegLens(false);
        rightLegLen = rightThighLen + rightShinLen;

        // ── Foot length (toe to heel) ──
        if (hasLF && hasLT && hasRF && hasRT)
            footLength = (Vector3.Distance(tLF, tLT) + Vector3.Distance(tRF, tRT)) * 0.5f;
        else if (hasLF && hasLT)
            footLength = Vector3.Distance(tLF, tLT);
        else if (hasRF && hasRT)
            footLength = Vector3.Distance(tRF, tRT);
        else
            footLength = hipToFoot * 0.15f; // ~15% of leg height is a reasonable foot length

        // ── Ankle height (distance from foot bone to ground plane in T-pose) ──
        if (hasLF && hasRF)
        {
            // In T-pose, the lowest point is roughly foot Y minus a small offset
            // The foot bone sits at the ankle, ground is at Y=0 in root space
            float avgFootY = (tLF.y + tRF.y) * 0.5f;
            ankleHeight = Mathf.Max(0.01f, avgFootY);
        }
        else
        {
            ankleHeight = hipToFoot * 0.05f;
        }

        // Sanity
        stanceWidth = Mathf.Max(0.04f, stanceWidth);
        hipToFoot = Mathf.Max(0.15f, hipToFoot);
        leftLegLen = Mathf.Max(0.15f, leftLegLen);
        rightLegLen = Mathf.Max(0.15f, rightLegLen);
        footLength = Mathf.Max(0.02f, footLength);
        ankleHeight = Mathf.Max(0.005f, ankleHeight);
    }

    /// <summary>
    /// Derive every step/raycast parameter from the measured proportions.
    /// No magic numbers — everything scales with the avatar's body.
    /// </summary>
    private void DeriveStepParameters()
    {
        float avgLeg = (leftLegLen + rightLegLen) * 0.5f;
        float avgShin = (leftShinLen + rightShinLen) * 0.5f;

        // Ray sphere radius: ~half the foot width, approximated as footLength * 0.3
        raySphereRadius = Mathf.Clamp(footLength * 0.3f, 0.02f, 0.12f);

        // Foot height offset: ankle bone sits above the ground by ankleHeight,
        // but the IK target should be at ground + sole thickness
        footHeightOffset = Mathf.Clamp(ankleHeight * 0.2f, 0.005f, 0.05f);

        // Step trigger: when planted foot drifts this far from ideal, take a step.
        // ~8% of leg length — tight so feet don't lag behind the body.
        stepTriggerDist = Mathf.Clamp(avgLeg * 0.08f, 0.04f, 0.18f);

        // Stride scale with speed: how much extra trigger distance per m/s of speed.
        // ~6% of leg length — keeps strides proportional but responsive.
        strideScale = Mathf.Clamp(avgLeg * 0.06f, 0.02f, 0.12f);

        // Step height: ~18% of shin length gives a visible lift without being cartoonish.
        stepHeightCalc = Mathf.Clamp(avgShin * 0.18f, 0.03f, 0.20f);

        // Step duration from leg-as-pendulum: T = pi * sqrt(L/g).
        // Real human walk step is ~0.5s, but VR needs snappier to keep up with head.
        // Use 30% of pendulum period for slow, 18% for fast.
        float pendulum = Mathf.PI * Mathf.Sqrt(avgLeg / 9.81f);
        stepDurSlow = Mathf.Clamp(pendulum * 0.30f, 0.10f, 0.30f);
        stepDurFast = Mathf.Clamp(pendulum * 0.18f, 0.06f, 0.18f);

        // Speed at which step duration reaches its minimum:
        // comfortable walk speed scales with leg length (~1.2 * sqrt(leg * g))
        fastSpeedRef = Mathf.Clamp(1.2f * Mathf.Sqrt(avgLeg * 9.81f), 1.0f, 3.5f);
    }

    // ═══════════════════════════════════════════════════════════
    //  SCALE ADAPTATION
    // ═══════════════════════════════════════════════════════════

    private void StoreBaseMeasurements()
    {
        baseStanceWidth = stanceWidth;
        baseHipToFoot = hipToFoot;
        baseLeftThighLen = leftThighLen;
        baseLeftShinLen = leftShinLen;
        baseLeftLegLen = leftLegLen;
        baseRightThighLen = rightThighLen;
        baseRightShinLen = rightShinLen;
        baseRightLegLen = rightLegLen;
        baseFootLength = footLength;
        baseAnkleHeight = ankleHeight;
    }

    private void ApplyScaleToMeasurements(float scale)
    {
        stanceWidth = baseStanceWidth * scale;
        hipToFoot = baseHipToFoot * scale;
        leftThighLen = baseLeftThighLen * scale;
        leftShinLen = baseLeftShinLen * scale;
        leftLegLen = baseLeftLegLen * scale;
        rightThighLen = baseRightThighLen * scale;
        rightShinLen = baseRightShinLen * scale;
        rightLegLen = baseRightLegLen * scale;
        footLength = baseFootLength * scale;
        ankleHeight = baseAnkleHeight * scale;

        DeriveStepParameters();

        if (left != null)
        {
            left.thighLen = leftThighLen;
            left.shinLen = leftShinLen;
            left.legLength = leftLegLen;
        }
        if (right != null)
        {
            right.thighLen = rightThighLen;
            right.shinLen = rightShinLen;
            right.legLength = rightLegLen;
        }

        rayCastRange = Mathf.Max(leftLegLen, rightLegLen) + 0.3f;
    }

    private void OnHeightChanged(BasisHeightDriver.HeightModeChange mode)
    {
        if (!IsInitialized) return;
        ApplyScaleToMeasurements(BasisHeightDriver.ScaledToMatchValue);
    }

    // ═══════════════════════════════════════════════════════════
    //  SIMULATE
    // ═══════════════════════════════════════════════════════════

    public void Simulate(float dt)
    {
        if (!IsInitialized || dt <= 0f) return;

        // ── Locomotion from head ──
        Vector3 headPos = BasisLocalBoneDriver.HeadControl.OutgoingWorldData.position;

        Vector3 rawVel = (headPos - prevHeadPos) / dt;
        rawVel.y = 0f;
        prevHeadPos = headPos;

        // Asymmetric smoothing: near-instant response when decelerating, slower when accelerating.
        // Stopping must kill the velocity bias immediately or feet overshoot.
        bool decelerating = rawVel.sqrMagnitude < smoothedVelocity.sqrMagnitude;
        float vAlpha = 1f - Mathf.Exp(-(decelerating ? 50f : 10f) * dt);
        smoothedVelocity = Vector3.Lerp(smoothedVelocity, rawVel, vAlpha);
        prevHeadYaw = HeadYaw();

        float speed = smoothedVelocity.magnitude;

        // Smooth body forward/right to prevent snappy foot repositioning during head turns.
        // Slower smoothing when stationary so small head oscillations don't move feet.
        Vector3 rawFwd = BodyForward();
        float fwdRate = speed > 0.1f ? 6f : 2.5f; // faster tracking when moving, sluggish when still
        float fwdAlpha = 1f - Mathf.Exp(-fwdRate * dt);
        smoothedBodyFwd = Vector3.Slerp(smoothedBodyFwd, rawFwd, fwdAlpha).normalized;
        if (smoothedBodyFwd.sqrMagnitude < 0.001f) smoothedBodyFwd = rawFwd;
        smoothedBodyRight = Vector3.Cross(Vector3.up, smoothedBodyFwd).normalized;
        if (smoothedBodyRight.sqrMagnitude < 0.001f) smoothedBodyRight = avatarTransform.right;

        Vector3 bodyFwd = smoothedBodyFwd;
        Vector3 bodyRight = smoothedBodyRight;

        // Raw (unsmoothed) right direction for side enforcement — must be instantaneous
        // so feet can't cross even during fast spins when the smooth direction lags.
        Vector3 rawRight = Vector3.Cross(Vector3.up, rawFwd).normalized;
        if (rawRight.sqrMagnitude < 0.001f) rawRight = bodyRight;

        // ── Ideal positions ──
        // In real gait the planted foot is behind the hips and the stepping foot
        // lands well ahead. We split the velocity bias: the foot that is currently
        // planted gets a SMALLER forward offset (it's trailing) while the foot
        // that needs to move gets a LARGER one (it should lead).
        Vector3 hipsXZ = new Vector3(hips.position.x, 0f, hips.position.z);
        Vector3 velDir = new Vector3(smoothedVelocity.x, 0f, smoothedVelocity.z);

        // Find ground — if raycast misses (mid-air), use hip-relative estimate.
        // Always keep feet at hipToFoot below hips so they track with jumps/falls.
        float groundY;
        bool airborne;
        if (Physics.Raycast(hips.position, Vector3.down, out RaycastHit ch, rayCastRange, groundLayers, QueryTriggerInteraction.Ignore))
        {
            groundY = ch.point.y;
            lastKnownGroundY = groundY;
            airborne = false;
        }
        else
        {
            // Airborne: feet stay at hipToFoot below hips (natural dangling position)
            groundY = hips.position.y - hipToFoot;
            airborne = true;
        }

        // Movement direction: use velocity direction (not body forward) so backward
        // and strafing motion biases feet in the correct direction.
        // Only use body forward as fallback when velocity is near zero.
        Vector3 moveDir = velDir.sqrMagnitude > 0.01f ? velDir.normalized : bodyFwd;

        // Base center with bias along movement direction, clamped to leg reach.
        float avgLeg = (leftLegLen + rightLegLen) * 0.5f;
        float maxOffset = avgLeg * 0.35f;
        float baseBias = Mathf.Min(speed * velocityBiasFactor, maxOffset);
        Vector3 center = new Vector3(hipsXZ.x, groundY, hipsXZ.z) + moveDir * baseBias;
        float halfStance = stanceWidth * 0.5f;

        // Lead offset for the stepping foot, also along movement direction
        float leadAmount = Mathf.Min(speed * velocityBiasFactor * 0.6f, maxOffset * 0.5f);
        Vector3 leadOffset = moveDir * leadAmount;

        float leftDist = HDist(left.plantedPos, center - bodyRight * halfStance);
        float rightDist = HDist(right.plantedPos, center + bodyRight * halfStance);

        if (leftDist >= rightDist)
        {
            left.idealPos = center - bodyRight * halfStance + leadOffset;
            right.idealPos = center + bodyRight * halfStance;
        }
        else
        {
            left.idealPos = center - bodyRight * halfStance;
            right.idealPos = center + bodyRight * halfStance + leadOffset;
        }

        // ── Prevent cross-legging: use RAW (instantaneous) right direction ──
        // The smoothed direction lags during spins — raw catches crossover immediately.
        Vector3 hipsGround = new Vector3(hipsXZ.x, groundY, hipsXZ.z);
        EnforceSide(ref left.idealPos, hipsGround, rawRight, -1, halfStance * 0.3f);
        EnforceSide(ref right.idealPos, hipsGround, rawRight, +1, halfStance * 0.3f);

        // ── Step parameters for this frame ──
        float speedT = Mathf.Clamp01(speed / fastSpeedRef);
        // When stationary, use a larger threshold so tiny head rotations don't trigger steps.
        // The smoothed body forward absorbs small oscillations, but the stance offset
        // still shifts ideals laterally. A wider deadzone prevents false triggers.
        float idleBoost = speed < 0.05f ? stepTriggerDist * 0.5f : 0f;
        float threshold = stepTriggerDist + speed * strideScale + idleBoost;
        float stepDur = Mathf.Lerp(stepDurSlow, stepDurFast, speedT);

        // ── Vertical correction ──
        if (airborne)
        {
            // Airborne: feet track hips every frame (no threshold, no jitter)
            float airY = groundY;
            if (left.phase == Phase.Planted) left.currentPos.y = left.plantedPos.y = airY;
            if (right.phase == Phase.Planted) right.currentPos.y = right.plantedPos.y = airY;
        }
        else
        {
            // Grounded: snap feet that are stuck at a stale height (spawn, landing)
            float maxVerticalDrift = hipToFoot * 0.25f;
            if (left.phase == Phase.Planted && Mathf.Abs(left.plantedPos.y - left.idealPos.y) > maxVerticalDrift)
                left.currentPos.y = left.plantedPos.y = left.idealPos.y;
            if (right.phase == Phase.Planted && Mathf.Abs(right.plantedPos.y - right.idealPos.y) > maxVerticalDrift)
                right.currentPos.y = right.plantedPos.y = right.idealPos.y;
        }

        // ── Update feet (pass raw forward for rotation, smoothed for placement) ──
        UpdateFoot(left, right, bodyFwd, rawFwd, speed, threshold, stepDur, dt);
        UpdateFoot(right, left, bodyFwd, rawFwd, speed, threshold, stepDur, dt);

        // ── Knee hints: compute target then smooth toward it ──
        float avgThigh = (leftThighLen + rightThighLen) * 0.5f;
        float avgLeg2 = (leftLegLen + rightLegLen) * 0.5f;
        Vector3 hp = hips.position;

        // How bent are the legs? 0 = standing straight, 1 = fully crouched
        float hipFootDist = Mathf.Abs(hp.y - (left.currentPos.y + right.currentPos.y) * 0.5f);
        float bendRatio = 1f - Mathf.Clamp01(hipFootDist / avgLeg2);

        // Base knee target: midpoint of hip→foot, pushed forward
        Vector3 leftKneeTarget = (hp + left.currentPos) * 0.5f + bodyFwd * (avgThigh * 0.4f);
        Vector3 rightKneeTarget = (hp + right.currentPos) * 0.5f + bodyFwd * (avgThigh * 0.4f);

        // Only splay outward when actually crouching — zero at normal stand
        // Small amount keeps knees from touching during deep bends
        float kneeSplay = halfStance * SplayWhenCrouchedPercentage * bendRatio;
        leftKneeTarget -= rawRight * kneeSplay;
        rightKneeTarget += rawRight * kneeSplay;

        // Minimal side enforcement: just prevent crossing, don't push wide
        Vector3 hipsGround2 = new Vector3(hp.x, leftKneeTarget.y, hp.z);
        float kneeMinSide = halfStance * 0.05f;
        EnforceSide(ref leftKneeTarget, hipsGround2, rawRight, -1, kneeMinSide);
        EnforceSide(ref rightKneeTarget, new Vector3(hp.x, rightKneeTarget.y, hp.z), rawRight, +1, kneeMinSide);

        float kneeAlpha = 1f - Mathf.Exp(-10f * dt);
        left.kneeHint = Vector3.Lerp(left.kneeHint, leftKneeTarget, kneeAlpha);
        right.kneeHint = Vector3.Lerp(right.kneeHint, rightKneeTarget, kneeAlpha);

        // ── Final safety: enforce side on the actual output positions every frame ──
        // During spins, planted positions go stale and the body rotates around them.
        Vector3 hipsGround3 = new Vector3(hp.x, left.currentPos.y, hp.z);
        float footMinSide = halfStance * 0.2f;
        EnforceSide(ref left.currentPos, hipsGround3, rawRight, -1, footMinSide);
        EnforceSide(ref right.currentPos, hipsGround3, rawRight, +1, footMinSide);
    }

    // ═══════════════════════════════════════════════════════════
    //  FOOT UPDATE
    // ═══════════════════════════════════════════════════════════

    private void UpdateFoot(FootState f, FootState other, Vector3 bodyFwd, Vector3 rawFwd, float speed, float threshold, float stepDur, float dt)
    {
        if (f.phase == Phase.Planted)
        {
            float a = 1f - Mathf.Exp(-plantedLerpSpeed * dt);
            f.currentPos = Vector3.Lerp(f.currentPos, f.plantedPos, a);

            // Planted feet HOLD their rotation — only settle toward plantedRot
            float ra = 1f - Mathf.Exp(-rotationLerpSpeed * dt);
            f.currentRot = Quaternion.Slerp(f.currentRot, f.plantedRot, ra);

            float dist = HDist(f.plantedPos, f.idealPos);
            if (dist > threshold && other.phase == Phase.Planted)
                StartStep(f, rawFwd, speed, stepDur);
        }
        else
        {
            f.stepTimer += dt;
            float t = Mathf.Clamp01(f.stepTimer / f.stepDur);
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);

            Vector3 pos = Vector3.Lerp(f.stepStartPos, f.stepTargetPos, ease);

            // Step height scales with speed: humans barely lift feet at a slow shuffle
            // but clear the ground more at a brisk walk. Minimum ~40% of max at idle.
            float speedT2 = Mathf.Clamp01(speed / fastSpeedRef);
            float dynamicHeight = stepHeightCalc * Mathf.Lerp(0.4f, 1.0f, speedT2);

            // Asymmetric arc: fast lift, peak at ~35%, quick drop
            float lift = Mathf.Pow(t, 0.6f) * Mathf.Pow(1f - t, 1.4f) / 0.234f;
            pos.y += Mathf.Clamp01(lift) * dynamicHeight;
            f.currentPos = pos;

            // Use live raw forward for step rotation so it stays current during turns
            Quaternion liveRot = FootRotation(rawFwd, f.filteredNormal);
            f.currentRot = Quaternion.Slerp(f.currentRot, liveRot, ease);

            if (t >= 1f)
            {
                f.phase = Phase.Planted;
                f.plantedPos = f.stepTargetPos;
                f.plantedRot = FootRotation(rawFwd, f.filteredNormal);
                f.currentPos = f.stepTargetPos;
            }
        }
    }

    private void StartStep(FootState f, Vector3 bodyFwd, float speed, float stepDur)
    {
        f.phase = Phase.Stepping;
        f.stepStartPos = f.currentPos;
        f.stepTimer = 0f;
        f.stepDur = stepDur;

        // Predict along movement direction, clamped so the target can't exceed leg reach
        float avgLeg = (leftLegLen + rightLegLen) * 0.5f;
        Vector3 moveDir = smoothedVelocity.sqrMagnitude > 0.01f ? new Vector3(smoothedVelocity.x, 0f, smoothedVelocity.z).normalized: bodyFwd;
        float predAmount = Mathf.Min(speed * stepDur * predictionFactor, avgLeg * 0.35f);
        Vector3 prediction = moveDir * predAmount;
        Vector3 targetXZ = f.idealPos + prediction;

        Vector3 rayOrig = targetXZ + Vector3.up * rayCastRange * 0.5f;
        if (Physics.SphereCast(rayOrig, raySphereRadius, Vector3.down, out RaycastHit hit, rayCastRange, groundLayers, QueryTriggerInteraction.Ignore))
        {
            f.stepTargetPos = hit.point + hit.normal * footHeightOffset;
            f.filteredNormal = hit.normal;
        }
        else
        {
            // Airborne: place step target at hipToFoot below hips
            f.stepTargetPos = new Vector3(targetXZ.x, hips.position.y - hipToFoot, targetXZ.z);
        }

        // Enforce side constraint on step target using raw (instantaneous) body right
        Vector3 rawFwd = BodyForward();
        Vector3 rawR = Vector3.Cross(Vector3.up, rawFwd).normalized;
        if (rawR.sqrMagnitude < 0.001f)
        {
            rawR = Vector3.right;
        }

        Vector3 hGround = new Vector3(hips.position.x, f.stepTargetPos.y, hips.position.z);
        EnforceSide(ref f.stepTargetPos, hGround, rawR, f.sideSign, stanceWidth * 0.15f);

        f.stepTargetRot = FootRotation(bodyFwd, f.filteredNormal);
    }

    // ═══════════════════════════════════════════════════════════
    //  HELPERS
    // ═══════════════════════════════════════════════════════════

    private static float HDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private void ClampFootBelowHips(ref Vector3 footPos)
    {
        if (footPos.y > hips.position.y)
            footPos.y = hips.position.y;
    }

    /// <summary>
    /// Prevents a foot ideal from crossing to the wrong side of the body centerline.
    /// sideSign: -1 for left foot, +1 for right foot.
    /// minDist: minimum lateral distance from centerline (keeps feet apart).
    /// </summary>
    private static void EnforceSide(ref Vector3 idealPos, Vector3 center, Vector3 bodyRight, int sideSign, float minDist)
    {
        Vector3 toIdeal = idealPos - center;
        float lateral = Vector3.Dot(toIdeal, bodyRight); // positive = right side

        // The foot must be on its own side with at least minDist clearance
        float required = sideSign * minDist;
        if (sideSign > 0 && lateral < required)
        {
            // Right foot crossed to left side — push it back
            idealPos += bodyRight * (required - lateral);
        }
        else if (sideSign < 0 && lateral > -minDist)
        {
            // Left foot crossed to right side — push it back
            idealPos -= bodyRight * (lateral + minDist);
        }
    }

    private float HeadYaw()
    {
        var hc = BasisLocalBoneDriver.HeadControl;
        Vector3 fwd = hc.OutgoingWorldData.rotation * Vector3.forward; fwd.y = 0f;
        return fwd.sqrMagnitude < 0.001f ? prevHeadYaw : Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
    }

    private Vector3 BodyForward()
    {
        // Combine hips, chest, and head forward directions to determine where the
        // body is facing. Hips are the most stable (don't change when looking around),
        // chest adds torso twist, head adds a small bias for the look direction.
        // This prevents feet from spinning when you just look left/right.

        Vector3 accumulated = Vector3.zero;
        float totalWeight = 0f;

        // Hips: strongest influence — the pelvis is the true body facing direction
        var hipsCtrl = BasisLocalBoneDriver.HipsControl;
        Vector3 hipsFwd = hipsCtrl.OutgoingWorldData.rotation * Vector3.forward;
        hipsFwd.y = 0f;
        if (hipsFwd.sqrMagnitude > 0.001f)
        {
            accumulated += hipsFwd.normalized * 3f;
            totalWeight += 3f;
        }

        // Chest: secondary influence — captures torso twist
        var chestCtrl = BasisLocalBoneDriver.ChestControl;
        if (chestCtrl != null)
        {
            Vector3 chestFwd = chestCtrl.OutgoingWorldData.rotation * Vector3.forward;
            chestFwd.y = 0f;
            if (chestFwd.sqrMagnitude > 0.001f)
            {
                accumulated += chestFwd.normalized * 2f;
                totalWeight += 2f;
            }
        }

        // Head: lightest influence — only adds a gentle bias toward look direction.
        // Ignored when looking steeply up/down (horizontal projection too short).
        Vector3 headFwd = BasisLocalBoneDriver.HeadControl.OutgoingWorldData.rotation * Vector3.forward;
        Vector3 headFlat = new Vector3(headFwd.x, 0f, headFwd.z);
        if (headFlat.sqrMagnitude > 0.1f) // only when not looking steeply up/down
        {
            accumulated += headFlat.normalized * 1f;
            totalWeight += 1f;
        }

        if (totalWeight > 0f)
        {
            accumulated /= totalWeight;
            if (accumulated.sqrMagnitude > 0.001f)
                return accumulated.normalized;
        }

        return avatarTransform.forward;
    }

    /// <summary>
    /// Compute foot rotation from body forward + surface normal, clamped to human limits:
    /// - Tilt (roll/pitch from slope) clamped to maxFootTiltDegrees
    /// - Yaw (toe-out/toe-in from body forward) clamped to maxFootYawDegrees
    /// </summary>
    private Quaternion FootRotation(Vector3 bodyFwd, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.001f)
        {
            normal = Vector3.up;
        }

        // Project body forward onto surface plane for the foot's forward direction
        Vector3 fwd = Vector3.ProjectOnPlane(bodyFwd, normal);
        if (fwd.sqrMagnitude < 1e-6f)
        {
            fwd = Vector3.ProjectOnPlane(Vector3.forward, normal);
        }

        fwd.Normalize();

        // Clamp tilt: blend between upright and surface-aligned
        Quaternion surfaceRot = Quaternion.LookRotation(fwd, normal);
        Quaternion uprightRot = Quaternion.LookRotation(fwd, Vector3.up);
        float tiltAngle = Quaternion.Angle(uprightRot, surfaceRot);
        Quaternion result = tiltAngle > 0.01f ? Quaternion.Slerp(uprightRot, surfaceRot, Mathf.Clamp01(maxFootTiltDegrees / tiltAngle)) : uprightRot;

        // Clamp yaw: how far the foot forward deviates from body forward on the horizontal plane
        Vector3 footFwd = result * Vector3.forward;
        Vector3 footFwdFlat = new Vector3(footFwd.x, 0f, footFwd.z);
        Vector3 bodyFwdFlat = new Vector3(bodyFwd.x, 0f, bodyFwd.z);

        if (footFwdFlat.sqrMagnitude > 1e-6f && bodyFwdFlat.sqrMagnitude > 1e-6f)
        {
            footFwdFlat.Normalize();
            bodyFwdFlat.Normalize();

            float yawAngle = Vector3.SignedAngle(bodyFwdFlat, footFwdFlat, Vector3.up);
            if (Mathf.Abs(yawAngle) > maxFootYawDegrees)
            {
                float clampedYaw = Mathf.Clamp(yawAngle, -maxFootYawDegrees, maxFootYawDegrees);
                float correction = clampedYaw - yawAngle;
                result = Quaternion.AngleAxis(correction, Vector3.up) * result;
            }
        }

        return result;
    }
    private static bool TryTP(System.Collections.Generic.Dictionary<HumanBodyBones, BasisCalibratedCoords> tp, HumanBodyBones b, out Vector3 p)
    {
        p = Vector3.zero;
        if (!tp.TryGetValue(b, out var c) || c.position == Vector3.zero)
        {
            return false;
        }
        p = c.position;
        return true;
    }

    // ── Fallbacks when calibration data is missing ──
    private void FallbackStanceWidth()
    {
        if (left.bone == null || right.bone == null)
        {
            stanceWidth = 0.2f;
        }
        else
        {
            Vector3 d = right.bone.position - left.bone.position;
            d.y = 0;
            stanceWidth = Mathf.Max(0.04f, d.magnitude);
        }
    }
    private void FallbackHipToFoot()
    {
        hipToFoot = hips != null && left.bone != null && right.bone != null ? Mathf.Max(0.15f, Mathf.Abs(hips.position.y - (left.bone.position.y + right.bone.position.y) * 0.5f)): 0.85f;
    }
    private void FallbackLegLens(bool isLeft)
    {
        float total = hipToFoot;
        float th = total * 0.55f, sh = total * 0.45f;
        if (isLeft) { leftThighLen = th; leftShinLen = sh; }
        else { rightThighLen = th; rightShinLen = sh; }
    }

    /// <summary>
    /// Immediately place a foot at its ideal position with ground raycast.
    /// Used on first frame to avoid feet being stuck at stale init positions.
    /// </summary>
    private void SnapFootToGround(FootState f, Vector3 bodyFwd)
    {
        Vector3 rayOrig = f.idealPos + 0.5f * rayCastRange * Vector3.up;
        if (Physics.SphereCast(rayOrig, raySphereRadius, Vector3.down, out RaycastHit hit,
            rayCastRange, groundLayers, QueryTriggerInteraction.Ignore))
        {
            f.currentPos = f.plantedPos = hit.point + hit.normal * footHeightOffset;
            f.filteredNormal = hit.normal;
        }
        else
        {
            // Airborne: place at hipToFoot below hips
            f.currentPos = f.plantedPos = new Vector3(f.idealPos.x, hips.position.y - hipToFoot, f.idealPos.z);
        }
        // Feet must never be above hips
        ClampFootBelowHips(ref f.currentPos);
        f.plantedPos = f.currentPos;
        f.currentRot = f.plantedRot = FootRotation(bodyFwd, f.filteredNormal);
        f.phase = Phase.Planted;
    }

    private void InitPose(FootState f)
    {
        if (f.bone == null)
        {
            return;
        }

        Vector3 bp = f.bone.position;
        if (Physics.Raycast(bp + Vector3.up * 0.3f, Vector3.down, out RaycastHit hit, rayCastRange, groundLayers, QueryTriggerInteraction.Ignore))
        {
            f.currentPos = f.plantedPos = f.idealPos = hit.point + hit.normal * footHeightOffset;
            f.filteredNormal = hit.normal;
        }
        else
        {
            f.currentPos = f.plantedPos = f.idealPos = bp;
            f.filteredNormal = Vector3.up;
        }
        Vector3 fwd = avatarTransform != null ? avatarTransform.forward : Vector3.forward;
        f.currentRot = f.plantedRot = FootRotation(fwd, f.filteredNormal);
        f.phase = Phase.Planted;
        if (hips != null)
        {
            f.kneeHint = (hips.position + f.currentPos) * 0.5f + fwd * (f.thighLen > 0 ? f.thighLen * 0.4f : 0.12f);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  DEBUG
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Returns a vertical hip offset for natural walk bob. Dips when a foot is mid-step
    /// (weight transfer) and rises when both feet are planted. Amplitude scales with
    /// avatar leg length and current speed.
    /// </summary>
    public float ComputeHipBob()
    {
        if (!IsInitialized)
        {
            return 0f;
        }

        float speed = smoothedVelocity.magnitude;
        if (speed < 0.05f)
        {
            return 0f; // no bob when still
        }

        // Bob amplitude: ~2% of hip-to-foot height, scaled by speed
        float maxBob = hipToFoot * 0.02f;
        float speedScale = Mathf.Clamp01(speed / fastSpeedRef);
        float amplitude = maxBob * speedScale;

        // Dip when either foot is mid-step (weight transfer phase)
        float leftDip = 0f, rightDip = 0f;
        if (left.phase == Phase.Stepping)
        {
            float t = Mathf.Clamp01(left.stepTimer / left.stepDur);
            leftDip = Mathf.Sin(t * Mathf.PI); // peaks at mid-step
        }
        if (right.phase == Phase.Stepping)
        {
            float t = Mathf.Clamp01(right.stepTimer / right.stepDur);
            rightDip = Mathf.Sin(t * Mathf.PI);
        }

        // Take the max dip (only one foot steps at a time, but safety)
        float dip = Mathf.Max(leftDip, rightDip);
        return -dip * amplitude; // negative = downward
    }

    public bool LeftIsPlanted => left.phase == Phase.Planted;
    public bool RightIsPlanted =>  right.phase == Phase.Planted;
    public float LeftStepProgress => left.phase == Phase.Stepping ? Mathf.Clamp01(left.stepTimer / left.stepDur) : 0f;
    public float RightStepProgress => right.phase == Phase.Stepping ? Mathf.Clamp01(right.stepTimer / right.stepDur) : 0f;
    public Vector3 LeftIdealPos => left.idealPos;
    public Vector3 RightIdealPos => right.idealPos;
    public Vector3 LeftStepTarget => left.stepTargetPos;
    public Vector3 RightStepTarget => right.stepTargetPos;
    public Vector3 SmoothedVelocity => smoothedVelocity;
    public float Speed => smoothedVelocity.magnitude;
    public Vector3 HipsPosition => hips.position;
    public float CalibratedStanceWidth => stanceWidth;
    public float CalibratedHipToFoot => hipToFoot;
    public float CalibratedLeftLeg => leftLegLen;
    public float CalibratedRightLeg => rightLegLen;
    public float CalibratedFootLength => footLength;
    public float CalibratedAnkleHeight => ankleHeight;
    public float DerivedStepHeight => stepHeightCalc;
    public float DerivedStepTrigger => stepTriggerDist;
    public float DerivedFastSpeed => fastSpeedRef;

    // ═══════════════════════════════════════════════════════════
    //  GIZMOS
    // ═══════════════════════════════════════════════════════════

    public void DrawGizmos()
    {
        if (!IsInitialized || left == null || right == null) return;

        DrawFoot(left, new Color(0.2f, 0.9f, 0.4f), new Color(1f, 0.85f, 0.1f));
        DrawFoot(right, new Color(0.2f, 0.5f, 1f), new Color(1f, 0.5f, 0.1f));

        if (hips != null)
        {
            Vector3 hp = hips.position;
            Vector3 bf = BodyForward();

            Gizmos.color = new Color(1f, 1f, 1f, 0.8f);
            Gizmos.DrawLine(hp, hp + bf * 0.4f);

            if (smoothedVelocity.sqrMagnitude > 0.01f)
            {
                Gizmos.color = new Color(1f, 0.2f, 1f, 0.8f);
                Gizmos.DrawLine(hp, hp + smoothedVelocity * 0.5f);
            }
        }
    }

    private void DrawFoot(FootState f, Color plantCol, Color stepCol)
    {
        bool stepping = f.phase == Phase.Stepping;
        Color c = stepping ? stepCol : plantCol;

        Gizmos.color = c;
        Gizmos.DrawSphere(f.currentPos, 0.02f);

        Gizmos.color = new Color(0.2f, 0.4f, 1f, 0.9f);
        Gizmos.DrawLine(f.currentPos, f.currentPos + f.currentRot * Vector3.forward * 0.07f);

        Gizmos.color = c * 0.4f;
        Gizmos.DrawWireSphere(f.idealPos, 0.015f);
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
        Gizmos.DrawLine(f.plantedPos, f.idealPos);

        if (stepping)
        {
            Gizmos.color = stepCol * 0.6f;
            const int seg = 16;
            Vector3 prev = f.stepStartPos;
            for (int i = 1; i <= seg; i++)
            {
                float t = i / (float)seg;
                float e = 1f - (1f - t) * (1f - t) * (1f - t);
                Vector3 p = Vector3.Lerp(f.stepStartPos, f.stepTargetPos, e);
                float lift = Mathf.Pow(t, 0.6f) * Mathf.Pow(1f - t, 1.4f) / 0.234f;
                p.y += Mathf.Clamp01(lift) * stepHeightCalc;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
            Gizmos.color = stepCol;
            Gizmos.DrawWireSphere(f.stepTargetPos, 0.015f);
        }

        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawLine(f.currentPos, f.kneeHint);

        if (hips != null) { Gizmos.color = c * 0.3f; Gizmos.DrawLine(hips.position, f.currentPos); }

#if UNITY_EDITOR
        float dist = HDist(f.plantedPos, f.idealPos);
        string lbl = stepping
            ? $"{f.name} STEP {Mathf.Clamp01(f.stepTimer / f.stepDur):P0}"
            : $"{f.name} planted  drift:{dist * 100f:F1}cm";
        UnityEditor.Handles.color = c;
        UnityEditor.Handles.Label(f.currentPos + Vector3.up * 0.06f, lbl);
#endif
    }
}
