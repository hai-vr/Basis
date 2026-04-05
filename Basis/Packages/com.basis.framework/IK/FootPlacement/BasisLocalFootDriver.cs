using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using Basis.Scripts.Drivers;
using System;
using UnityEngine;

/// <summary>
/// Procedural foot placement with velocity-predicted stepping.
/// Uses a Planted/Stepping state machine but with predictive step targets so feet
/// land where the body is GOING, not where it WAS. Step timing and stride length
/// scale with movement speed.
/// </summary>
[Serializable]
public class BasisLocalFootDriver
{
    // ───────────────────── Tuning ─────────────────────

    [Header("Ground Detection")]
    [SerializeField, Range(0.03f, 0.15f)] private float raySphereRadius = 0.05f;
    [SerializeField, Range(0.0f, 0.10f)] private float footHeightOffset = 0.015f;
    [SerializeField] private LayerMask groundLayers;

    [Header("Body Proportions (computed from calibration)")]
    [Tooltip("Read-only: computed from T-pose calibration data.")]
    [SerializeField] private float calibratedStanceWidth = 0.1f;
    [SerializeField] private float calibratedHipToFoot = 0.85f;
    [SerializeField] private float calibratedLeftLegLen = 0.8f;
    [SerializeField] private float calibratedRightLegLen = 0.8f;

    [Header("Stepping")]
    [Tooltip("Base trigger distance when standing still.")]
    [SerializeField, Range(0.04f, 0.20f)] private float idleStepThreshold = 0.10f;
    [Tooltip("Trigger distance grows with speed: threshold + speed * this.")]
    [SerializeField, Range(0.02f, 0.15f)] private float strideScaleWithSpeed = 0.08f;
    [Tooltip("Step duration at idle (slow steps).")]
    [SerializeField, Range(0.10f, 0.5f)] private float stepDurationSlow = 0.22f;
    [Tooltip("Step duration at fast walk (quick steps).")]
    [SerializeField, Range(0.05f, 0.3f)] private float stepDurationFast = 0.12f;
    [Tooltip("Speed at which step duration reaches its minimum.")]
    [SerializeField, Range(0.5f, 4f)] private float fastSpeedRef = 1.8f;
    [Tooltip("Max step lift height.")]
    [SerializeField, Range(0.02f, 0.25f)] private float stepHeight = 0.14f;

    [Header("Prediction")]
    [Tooltip("How far ahead (in step-durations) to predict the step target.")]
    [SerializeField, Range(0.3f, 1.5f)] private float predictionFactor = 0.7f;

    [Header("Smoothing")]
    [SerializeField, Range(5f, 40f)] private float plantedLerpSpeed = 25f;
    [SerializeField, Range(5f, 40f)] private float rotationLerpSpeed = 16f;

    [Header("Foot Tilt")]
    [SerializeField, Range(0f, 60f)] private float maxFootTiltDegrees = 35f;

    // ───────────────────── Runtime ─────────────────────

    private Transform avatarTransform;
    private Transform hips;
    private FootState left, right;
    private float rayCastRange;

    private Vector3 prevHeadPos;
    private Vector3 smoothedVelocity;
    private float prevHeadYaw;

    // Public results
    public bool IsInitialized { get; private set; }
    public Vector3 LeftFootPosition => left != null ? left.currentPos : Vector3.zero;
    public Quaternion LeftFootRotation => left != null ? left.currentRot : Quaternion.identity;
    public Vector3 RightFootPosition => right != null ? right.currentPos : Vector3.zero;
    public Quaternion RightFootRotation => right != null ? right.currentRot : Quaternion.identity;
    public Vector3 LeftKneeHint => left != null ? left.kneeHint : Vector3.zero;
    public Vector3 RightKneeHint => right != null ? right.kneeHint : Vector3.zero;

    // ───────────────────── Per-foot ─────────────────────

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

        public Vector3 stepStartPos;
        public Vector3 stepTargetPos;
        public Quaternion stepTargetRot;
        public float stepTimer, stepDur;

        public Vector3 idealPos;       // debug: where the foot wants to be
        public Vector3 filteredNormal;
        public Vector3 currentPos;
        public Quaternion currentRot;
        public Vector3 kneeHint;

        public FootState(string n, Transform b, int s)
        {
            name = n; bone = b; sideSign = s;
            filteredNormal = Vector3.up;
            phase = Phase.Planted;
        }
    }

    // ───────────────────── Init ─────────────────────

    public void InitializeVariables()
    {
        avatarTransform = BasisLocalPlayer.Instance.AvatarTransform;
        var mapping = BasisLocalAvatarDriver.Mapping;
        hips = mapping.Hips;

        var lf = mapping.leftFoot;
        var rf = mapping.rightFoot;

        left = new FootState("Left", lf, -1);
        right = new FootState("Right", rf, +1);

        left.thigh = SafeGet(mapping.LeftUpperLeg, lf != null ? lf.parent?.parent : null);
        left.shin = SafeGet(mapping.LeftLowerLeg, lf != null ? lf.parent : null);
        right.thigh = SafeGet(mapping.RightUpperLeg, rf != null ? rf.parent?.parent : null);
        right.shin = SafeGet(mapping.RightLowerLeg, rf != null ? rf.parent : null);

        if (groundLayers.value == 0) groundLayers = LayerMask.GetMask("Default");

        // ── Compute proportions from T-pose calibration ──
        ComputeProportionsFromCalibration(mapping);

        left.thighLen = calibratedLeftLegLen * 0.5f;
        left.shinLen = calibratedLeftLegLen * 0.5f;
        left.legLength = calibratedLeftLegLen;
        right.thighLen = calibratedRightLegLen * 0.5f;
        right.shinLen = calibratedRightLegLen * 0.5f;
        right.legLength = calibratedRightLegLen;

        // Refine segment lengths from actual bone transforms if available
        RefineSegmentLengths(left);
        RefineSegmentLengths(right);

        rayCastRange = Mathf.Max(left.legLength, right.legLength) + 0.3f;

        InitPose(left); InitPose(right);

        var hc = BasisLocalBoneDriver.HeadControl;
        if (hc != null) { prevHeadPos = hc.OutgoingWorldData.position; prevHeadYaw = HeadYaw(); }
        smoothedVelocity = Vector3.zero;
        IsInitialized = true;
    }

    /// <summary>
    /// Pull exact body proportions from the T-pose data recorded during calibration.
    /// This replaces hardcoded/guessed values with measurements from the actual avatar.
    /// </summary>
    private void ComputeProportionsFromCalibration(BasisTransformMapping mapping)
    {
        var tpose = mapping.TposeFromRoot;
        if (tpose == null || tpose.Count == 0)
        {
            // Fallback: measure from live transforms
            FallbackMeasure();
            return;
        }

        bool hasHips = TryGetTpose(tpose, HumanBodyBones.Hips, out Vector3 tHips);
        bool hasLUL = TryGetTpose(tpose, HumanBodyBones.LeftUpperLeg, out Vector3 tLUL);
        bool hasRUL = TryGetTpose(tpose, HumanBodyBones.RightUpperLeg, out Vector3 tRUL);
        bool hasLLL = TryGetTpose(tpose, HumanBodyBones.LeftLowerLeg, out Vector3 tLLL);
        bool hasRLL = TryGetTpose(tpose, HumanBodyBones.RightLowerLeg, out Vector3 tRLL);
        bool hasLF = TryGetTpose(tpose, HumanBodyBones.LeftFoot, out Vector3 tLF);
        bool hasRF = TryGetTpose(tpose, HumanBodyBones.RightFoot, out Vector3 tRF);

        // ── Stance width: horizontal distance between feet in T-pose ──
        if (hasLF && hasRF)
        {
            Vector3 diff = tRF - tLF;
            diff.y = 0f; // horizontal only
            calibratedStanceWidth = diff.magnitude;
        }

        // ── Hip to foot height: vertical distance from hips to average foot ──
        if (hasHips && hasLF && hasRF)
        {
            Vector3 avgFoot = (tLF + tRF) * 0.5f;
            calibratedHipToFoot = Mathf.Abs(tHips.y - avgFoot.y);
        }

        // ── Leg lengths from segments ──
        if (hasLUL && hasLLL && hasLF)
        {
            float thigh = Vector3.Distance(tLUL, tLLL);
            float shin = Vector3.Distance(tLLL, tLF);
            calibratedLeftLegLen = thigh + shin;
        }
        else if (hasHips && hasLF)
        {
            calibratedLeftLegLen = Vector3.Distance(tHips, tLF);
        }

        if (hasRUL && hasRLL && hasRF)
        {
            float thigh = Vector3.Distance(tRUL, tRLL);
            float shin = Vector3.Distance(tRLL, tRF);
            calibratedRightLegLen = thigh + shin;
        }
        else if (hasHips && hasRF)
        {
            calibratedRightLegLen = Vector3.Distance(tHips, tRF);
        }

        // Sanity clamps
        calibratedStanceWidth = Mathf.Max(0.05f, calibratedStanceWidth);
        calibratedHipToFoot = Mathf.Max(0.2f, calibratedHipToFoot);
        calibratedLeftLegLen = Mathf.Max(0.2f, calibratedLeftLegLen);
        calibratedRightLegLen = Mathf.Max(0.2f, calibratedRightLegLen);
    }

    private static bool TryGetTpose(System.Collections.Generic.Dictionary<HumanBodyBones, BasisCalibratedCoords> tpose, HumanBodyBones bone, out Vector3 pos)
    {
        pos = Vector3.zero;
        if (tpose.TryGetValue(bone, out var coords))
        {
            if (coords.position != Vector3.zero)
            {
                pos = coords.position;
                return true;
            }
        }
        return false;
    }

    private void FallbackMeasure()
    {
        // Measure from live transforms as fallback when no calibration exists
        if (hips != null && left.bone != null && right.bone != null)
        {
            Vector3 lf = left.bone.position, rf = right.bone.position;
            Vector3 diff = rf - lf; diff.y = 0f;
            calibratedStanceWidth = Mathf.Max(0.05f, diff.magnitude);
            calibratedHipToFoot = Mathf.Max(0.2f, Mathf.Abs(hips.position.y - (lf.y + rf.y) * 0.5f));
            calibratedLeftLegLen = Mathf.Max(0.2f, Vector3.Distance(hips.position, lf));
            calibratedRightLegLen = Mathf.Max(0.2f, Vector3.Distance(hips.position, rf));
        }
    }

    private void RefineSegmentLengths(FootState f)
    {
        // If we have thigh/shin transforms, measure actual segment lengths
        if (f.thigh != null && f.shin != null && f.bone != null)
        {
            float thigh = Vector3.Distance(f.thigh.position, f.shin.position);
            float shin = Vector3.Distance(f.shin.position, f.bone.position);
            if (thigh > 0.05f && shin > 0.05f)
            {
                f.thighLen = thigh;
                f.shinLen = shin;
                f.legLength = thigh + shin;
            }
        }
    }

    // ───────────────────── Main tick ─────────────────────

    public void Simulate(float dt)
    {
        if (!IsInitialized || avatarTransform == null || hips == null) return;
        if (left == null || right == null || dt <= 0f) return;

        // ── Locomotion from head ──
        Vector3 headPos = BasisLocalBoneDriver.HeadControl != null
            ? BasisLocalBoneDriver.HeadControl.OutgoingWorldData.position
            : avatarTransform.position + Vector3.up * 1.6f;

        Vector3 rawVel = (headPos - prevHeadPos) / dt;
        rawVel.y = 0f;
        prevHeadPos = headPos;

        float vAlpha = 1f - Mathf.Exp(-10f * dt);
        smoothedVelocity = Vector3.Lerp(smoothedVelocity, rawVel, vAlpha);
        prevHeadYaw = HeadYaw();

        float speed = smoothedVelocity.magnitude;
        Vector3 bodyFwd = BodyForward(headPos);
        Vector3 bodyRight = Vector3.Cross(Vector3.up, bodyFwd).normalized;
        if (bodyRight.sqrMagnitude < 0.001f) bodyRight = avatarTransform.right;

        // ── Ideal foot positions: under hips, biased forward by velocity ──
        // Feet lead slightly in the movement direction so they don't trail the body.
        Vector3 hipsXZ = new Vector3(hips.position.x, 0f, hips.position.z);
        Vector3 velBias = new Vector3(smoothedVelocity.x, 0f, smoothedVelocity.z) * 0.06f;

        float groundY = hips.position.y - HipToFoot();
        if (Physics.Raycast(hips.position, Vector3.down, out RaycastHit ch, rayCastRange, groundLayers, QueryTriggerInteraction.Ignore))
            groundY = ch.point.y;

        Vector3 center = new Vector3(hipsXZ.x, groundY, hipsXZ.z) + velBias;
        float halfStance = calibratedStanceWidth * 0.5f;
        left.idealPos = center - bodyRight * halfStance;
        right.idealPos = center + bodyRight * halfStance;

        // ── Speed-adaptive step parameters ──
        float speedT = Mathf.Clamp01(speed / fastSpeedRef);
        float stepThreshold = idleStepThreshold + speed * strideScaleWithSpeed;
        float stepDur = Mathf.Lerp(stepDurationSlow, stepDurationFast, speedT);

        // ── Update feet ──
        UpdateFoot(left, right, bodyFwd, speed, stepThreshold, stepDur, dt);
        UpdateFoot(right, left, bodyFwd, speed, stepThreshold, stepDur, dt);

        // ── Knee hints ──
        Vector3 hp = hips.position;
        left.kneeHint = (hp + left.currentPos) * 0.5f + bodyFwd * 0.2f;
        right.kneeHint = (hp + right.currentPos) * 0.5f + bodyFwd * 0.2f;
    }

    // ───────────────────── Foot update ─────────────────────

    private void UpdateFoot(FootState f, FootState other, Vector3 bodyFwd, float speed, float threshold, float stepDur, float dt)
    {
        if (f.phase == Phase.Planted)
        {
            // Hold position, lerp for surface tracking
            float a = 1f - Mathf.Exp(-plantedLerpSpeed * dt);
            f.currentPos = Vector3.Lerp(f.currentPos, f.plantedPos, a);
            float ra = 1f - Mathf.Exp(-rotationLerpSpeed * dt);
            f.currentRot = Quaternion.Slerp(f.currentRot, f.plantedRot, ra);

            // Check trigger
            float dist = HDist(f.plantedPos, f.idealPos);
            bool otherPlanted = other.phase == Phase.Planted;

            if (dist > threshold && otherPlanted)
            {
                StartStep(f, bodyFwd, speed, stepDur);
            }
        }
        else // Stepping
        {
            f.stepTimer += dt;
            float t = Mathf.Clamp01(f.stepTimer / f.stepDur);

            // Ease-out for snappy landing
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);

            Vector3 pos = Vector3.Lerp(f.stepStartPos, f.stepTargetPos, ease);

            // Foot-like arc: quick lift (toe-off), peak at ~35%, fast drop (heel-strike).
            // Skewed parabola: h(t) = t^0.6 * (1-t)^1.4 normalized so peak = 1
            float lift = Mathf.Pow(t, 0.6f) * Mathf.Pow(1f - t, 1.4f);
            lift /= 0.234f; // normalize (peak of t^0.6*(1-t)^1.4 is ~0.234)
            lift = Mathf.Clamp01(lift);
            pos.y += lift * stepHeight;
            f.currentPos = pos;

            f.currentRot = Quaternion.Slerp(f.currentRot, f.stepTargetRot, ease);

            if (t >= 1f)
            {
                f.phase = Phase.Planted;
                f.plantedPos = f.stepTargetPos;
                f.plantedRot = f.stepTargetRot;
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

        // ── Predicted target: where ideal will be when the step finishes ──
        // This is the key fix — step to where the body is GOING.
        Vector3 prediction = smoothedVelocity * (stepDur * predictionFactor);
        Vector3 targetXZ = f.idealPos + new Vector3(prediction.x, 0f, prediction.z);

        // Raycast to ground at predicted target
        Vector3 rayOrig = targetXZ + Vector3.up * rayCastRange * 0.5f;
        if (Physics.SphereCast(rayOrig, raySphereRadius, Vector3.down, out RaycastHit hit,
            rayCastRange, groundLayers, QueryTriggerInteraction.Ignore))
        {
            f.stepTargetPos = hit.point + hit.normal * footHeightOffset;
            f.filteredNormal = hit.normal;
        }
        else
        {
            f.stepTargetPos = targetXZ;
        }

        f.stepTargetRot = FootRotation(bodyFwd, f.filteredNormal);
    }

    // ───────────────────── Helpers ─────────────────────

    private static float HDist(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private float HeadYaw()
    {
        var hc = BasisLocalBoneDriver.HeadControl;
        if (hc == null) return 0f;
        Vector3 fwd = hc.OutgoingWorldData.rotation * Vector3.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.001f) return prevHeadYaw;
        return Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
    }

    private Vector3 BodyForward(Vector3 headPos)
    {
        var hc = BasisLocalBoneDriver.HeadControl;
        if (hc != null)
        {
            Vector3 fwd = hc.OutgoingWorldData.rotation * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude > 0.001f) return fwd.normalized;
        }
        return avatarTransform != null ? avatarTransform.forward : Vector3.forward;
    }

    private float HipToFoot()
    {
        return calibratedHipToFoot;
    }

    private Quaternion FootRotation(Vector3 bodyFwd, Vector3 normal)
    {
        if (normal.sqrMagnitude < 0.001f) normal = Vector3.up;
        Vector3 fwd = Vector3.ProjectOnPlane(bodyFwd, normal);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.ProjectOnPlane(Vector3.forward, normal);
        fwd.Normalize();

        Quaternion sRot = Quaternion.LookRotation(fwd, normal);
        Quaternion uRot = Quaternion.LookRotation(fwd, Vector3.up);
        float angle = Quaternion.Angle(uRot, sRot);
        if (angle > 0.01f) return Quaternion.Slerp(uRot, sRot, Mathf.Clamp01(maxFootTiltDegrees / angle));
        return uRot;
    }

    private Transform SafeGet(Transform a, Transform b) => a != null ? a : b;

    // CacheLeg removed — proportions now come from ComputeProportionsFromCalibration + RefineSegmentLengths

    private void InitPose(FootState f)
    {
        if (f.bone == null) return;
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
    }

    // ───────────────────── Debug ─────────────────────

    public bool LeftIsPlanted => left != null && left.phase == Phase.Planted;
    public bool RightIsPlanted => right != null && right.phase == Phase.Planted;
    public float LeftStepProgress => left != null && left.phase == Phase.Stepping ? Mathf.Clamp01(left.stepTimer / left.stepDur) : 0f;
    public float RightStepProgress => right != null && right.phase == Phase.Stepping ? Mathf.Clamp01(right.stepTimer / right.stepDur) : 0f;
    public Vector3 LeftIdealPos => left != null ? left.idealPos : Vector3.zero;
    public Vector3 RightIdealPos => right != null ? right.idealPos : Vector3.zero;
    public Vector3 LeftStepTarget => left != null ? left.stepTargetPos : Vector3.zero;
    public Vector3 RightStepTarget => right != null ? right.stepTargetPos : Vector3.zero;
    public Vector3 SmoothedVelocity => smoothedVelocity;
    public float Speed => smoothedVelocity.magnitude;
    public Vector3 HipsPosition => hips != null ? hips.position : Vector3.zero;
    public float CalibratedStanceWidth => calibratedStanceWidth;
    public float CalibratedHipToFoot => calibratedHipToFoot;
    public float CalibratedLeftLeg => calibratedLeftLegLen;
    public float CalibratedRightLeg => calibratedRightLegLen;

    // ───────────────────── Gizmos ─────────────────────

    public void DrawGizmos()
    {
        if (!IsInitialized || left == null || right == null) return;

        DrawFoot(left, new Color(0.2f, 0.9f, 0.4f), new Color(1f, 0.85f, 0.1f));
        DrawFoot(right, new Color(0.2f, 0.5f, 1f), new Color(1f, 0.5f, 0.1f));

        if (hips != null)
        {
            Vector3 hp = hips.position;
            Vector3 bf = BodyForward(BasisLocalBoneDriver.HeadControl != null
                ? BasisLocalBoneDriver.HeadControl.OutgoingWorldData.position : hp + Vector3.up);

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

        // Orientation
        Gizmos.color = new Color(0.2f, 0.4f, 1f, 0.9f);
        Gizmos.DrawLine(f.currentPos, f.currentPos + f.currentRot * Vector3.forward * 0.07f);

        // Ideal position
        Gizmos.color = c * 0.4f;
        Gizmos.DrawWireSphere(f.idealPos, 0.015f);
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.5f);
        Gizmos.DrawLine(f.plantedPos, f.idealPos);

        if (stepping)
        {
            // Step arc preview
            Gizmos.color = stepCol * 0.6f;
            const int seg = 16;
            Vector3 prev = f.stepStartPos;
            for (int i = 1; i <= seg; i++)
            {
                float t = i / (float)seg;
                float e = 1f - (1f - t) * (1f - t) * (1f - t);
                Vector3 p = Vector3.Lerp(f.stepStartPos, f.stepTargetPos, e);
                float lift = Mathf.Pow(t, 0.6f) * Mathf.Pow(1f - t, 1.4f) / 0.234f;
                p.y += Mathf.Clamp01(lift) * stepHeight;
                Gizmos.DrawLine(prev, p);
                prev = p;
            }
            Gizmos.color = stepCol;
            Gizmos.DrawWireSphere(f.stepTargetPos, 0.015f);
        }

        // Knee hint
        Gizmos.color = new Color(0f, 1f, 1f, 0.4f);
        Gizmos.DrawLine(f.currentPos, f.kneeHint);

        // Leg
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
