using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Drivers;
using System;
using UnityEngine;

[Serializable]
public class BasisLocalFootDriver
{
    [Header("Raycast")]
    [SerializeField, Range(0.03f, 0.15f)] private float raySphereRadius = 0.06f;
    [SerializeField, Range(0.1f, 3f)] private float rayCastRange = 2f;   // recalculated at init (max of legs)
    [SerializeField, Range(0.0f, 0.5f)] private float rayOriginHeight = 0.10f; // height above foot for ray origin
    [SerializeField] private LayerMask groundLayers;

    [Header("Foot Placement")]
    [SerializeField, Range(0f, 0.2f)] private float footHeightOffset = 0.02f;
    [SerializeField, Range(0f, 60f)] private float maxFootTiltDegrees = 35f;
    [SerializeField, Range(0f, 0.5f)] private float maxStepDownSlack = 0.25f;  // extra reach downward

    [Header("Smoothing")]
    [SerializeField, Range(0f, 30f)] private float positionLerpSpeed = 15f;
    [SerializeField, Range(0f, 30f)] private float rotationLerpSpeed = 15f;

    [Header("Leg Curve (Gizmos Only)")]
    [SerializeField, Range(6, 64)] private int curveSegments = 20;
    [SerializeField, Tooltip("Default knee plane hint; usually Avatar forward.")]
    private Vector3 kneeForwardHint = Vector3.forward;
    [SerializeField, Range(0f, 1f), Tooltip("How strongly the knee aligns to the forward hint vs. auto plane.")]
    private float kneeHintWeight = 0.8f;
    [SerializeField, Tooltip("If your avatar faces Z+, leave as is. Flip if the knee bends backward.")]
    private bool flipKneeBend = false;

    private Transform AvatarTransform;
    private Transform Hips;
    private FootState left;
    private FootState right;

    [Serializable]
    private class FootState
    {
        public string name;
        public Transform bone;              // ankle/foot transform you want to place
        public Transform thigh;             // upper leg (hip→knee)
        public Transform shin;              // lower leg (knee→ankle)
        public Vector3 targetPos;
        public Quaternion targetRot;
        public Vector3 currentPos;
        public Quaternion currentRot;
        public float legLength;
        public float thighLen;
        public float shinLen;
        public bool hasHit;
        public int sideSign;                // -1 for Left, +1 for Right

        public FootState(string name, Transform bone, int sideSign)
        {
            this.name = name;
            this.bone = bone;
            this.sideSign = sideSign;
        }
    }

    // ----------- Lifecycle -------------

    public void InitializeVariables()
    {
        AvatarTransform = BasisLocalPlayer.Instance.AvatarTransform;
        Hips = BasisLocalAvatarDriver.References.Hips;

        var leftFoot = BasisLocalAvatarDriver.References.leftFoot;
        var rightFoot = BasisLocalAvatarDriver.References.rightFoot;

        left = new FootState("Left", leftFoot, -1);
        right = new FootState("Right", rightFoot, +1);

        // Try to grab thigh/shin from references if available; otherwise walk parents.
        left.thigh = SafeGet(BasisLocalAvatarDriver.References.LeftUpperLeg, leftFoot != null ? leftFoot.parent?.parent : null);
        left.shin = SafeGet(BasisLocalAvatarDriver.References.LeftLowerLeg, leftFoot != null ? leftFoot.parent : null);
        right.thigh = SafeGet(BasisLocalAvatarDriver.References.RightUpperLeg, rightFoot != null ? rightFoot.parent?.parent : null);
        right.shin = SafeGet(BasisLocalAvatarDriver.References.RightLowerLeg, rightFoot != null ? rightFoot.parent : null);

        groundLayers = (groundLayers.value == 0) ? LayerMask.GetMask("Default") : groundLayers;

        // Estimate lengths (and cache separate thigh/shin).
        CacheLegMetrics(left);
        CacheLegMetrics(right);

        // Use the longer leg as a conservative visible range.
        rayCastRange = Mathf.Max(left.legLength, right.legLength);

        // Initialize to current pose to avoid popping.
        InitFootPose(left);
        InitFootPose(right);
    }

    private Transform SafeGet(Transform prefer, Transform fallback)
    {
        return prefer != null ? prefer : fallback;
    }

    private void CacheLegMetrics(FootState f)
    {
        if (Hips == null || f.bone == null) return;

        // Measure segments if we have them; otherwise just hip→foot for total length
        if (f.thigh != null && f.shin != null)
        {
            // Thigh length = distance hip→knee (thigh→shin joint)
            var kneeGuess = f.shin.position;
            f.thighLen = Vector3.Distance(Hips.position, kneeGuess);
            // Shin length = distance knee→ankle
            f.shinLen = Vector3.Distance(kneeGuess, f.bone.position);
            f.legLength = f.thighLen + f.shinLen + maxStepDownSlack;
        }
        else
        {
            f.thighLen = Mathf.Max(0.15f, Vector3.Distance(Hips.position, f.bone.position) * 0.45f);
            f.shinLen = Mathf.Max(0.15f, Vector3.Distance(Hips.position, f.bone.position) * 0.55f);
            f.legLength = f.thighLen + f.shinLen + maxStepDownSlack;
        }
    }

    private void InitFootPose(FootState f)
    {
        if (f.bone == null) return;
        f.currentPos = f.targetPos = f.bone.position;
        f.currentRot = f.targetRot = f.bone.rotation;
    }

    // Call from LateUpdate in your host to let animation pose first, then we correct feet.
    public void Update()
    {
        if (AvatarTransform == null || Hips == null) return;

        CalculateFootPlacement(left);
        CalculateFootPlacement(right);

        // Smooth toward targets (keeps feet from jittering)
        ApplySmoothing(left);
        ApplySmoothing(right);

        // Write back to the bones
        WriteToBone(left);
        WriteToBone(right);

        var Rig = BasisLocalPlayer.Instance.LocalRigDriver;
        // LEFT
        PushToRigTargets(
            left,
            pos => Rig.LeftFootTwoBoneIK.data.TargetPosition = pos,
            rot => Rig.LeftFootTwoBoneIK.data.TargetRotation = rot,
            // comment the next line if your constraint doesn't use a hint:
            hint => Rig.LeftFootTwoBoneIK.data.HintPosition = hint
        );

        // RIGHT
        PushToRigTargets(
            right,
            pos => Rig.RightFootTwoBoneIK.data.TargetPosition = pos,
            rot => Rig.RightFootTwoBoneIK.data.TargetRotation = rot,
            // comment if not using hints:
            hint => Rig.RightFootTwoBoneIK.data.HintPosition = hint
        );
    }

    // ----------- Core logic -------------

    private Vector3 GetPerFootRayOrigin(FootState f)
    {
        Vector3 horizontalOffset = Vector3.ProjectOnPlane(f.bone.position - Hips.position, AvatarTransform.up);
        return Hips.position + horizontalOffset + AvatarTransform.up * rayOriginHeight;
    }

    private void CalculateFootPlacement(FootState f)
    {
        if (f.bone == null) return;

        Vector3 rayOrigin = GetPerFootRayOrigin(f);
        Vector3 rayDir = Vector3.down; // world down (you could use -AvatarTransform.up if you want avatar-relative)
        float rayLen = f.legLength;

        RaycastHit hit;
        f.hasHit = Physics.SphereCast(
            rayOrigin,
            raySphereRadius,
            rayDir,
            out hit,
            rayLen,
            groundLayers,
            QueryTriggerInteraction.Ignore
        );

        if (f.hasHit)
        {
            Vector3 desiredPos = hit.point + hit.normal * footHeightOffset;

            Vector3 forwardProjected = Vector3.ProjectOnPlane(AvatarTransform.forward, hit.normal);
            if (forwardProjected.sqrMagnitude < 1e-6f)
                forwardProjected = Vector3.ProjectOnPlane(AvatarTransform.right, hit.normal);
            forwardProjected.Normalize();

            Quaternion surfaceRot = Quaternion.LookRotation(forwardProjected, hit.normal);
            Quaternion uprightRot = Quaternion.LookRotation(forwardProjected, AvatarTransform.up);
            float tiltAngle = Quaternion.Angle(uprightRot, surfaceRot);
            float t = (tiltAngle <= 0.0001f) ? 0f : Mathf.Clamp01(maxFootTiltDegrees / tiltAngle);
            Quaternion clampedRot = Quaternion.Slerp(uprightRot, surfaceRot, t);

            f.targetPos = desiredPos;
            f.targetRot = clampedRot;
        }
        else
        {
            f.targetPos = f.bone.position;
            f.targetRot = f.bone.rotation;
        }
    }

    private void ApplySmoothing(FootState f)
    {
        float dt = Time.deltaTime;
        float posAlpha = 1f - Mathf.Exp(-positionLerpSpeed * dt);
        float rotAlpha = 1f - Mathf.Exp(-rotationLerpSpeed * dt);

        f.currentPos = Vector3.Lerp(f.currentPos, f.targetPos, posAlpha);
        f.currentRot = Quaternion.Slerp(f.currentRot, f.targetRot, rotAlpha);
    }

    private void WriteToBone(FootState f)
    {
        if (f.bone == null) return;
        f.bone.position = f.currentPos;
        f.bone.rotation = f.currentRot;
    }

    // ----------- Knee solve (Gizmos) -------------

    private Vector3 SolveKneePosition(FootState f, Vector3 hip, Vector3 ankle, out bool valid)
    {
        valid = false;

        float a = Mathf.Max(0.0001f, f.thighLen);
        float b = Mathf.Max(0.0001f, f.shinLen);
        Vector3 v = ankle - hip;
        float d = v.magnitude;

        // Clamp ankle distance inside reach
        float maxReach = Mathf.Max(0.0001f, a + b - 1e-4f);
        float minReach = Mathf.Abs(a - b) + 1e-4f;
        d = Mathf.Clamp(d, minReach, maxReach);

        Vector3 dir = v.normalized;

        // Law of cosines along the hip→ankle axis
        float x = (a * a - b * b + d * d) / (2f * d);
        float h2 = a * a - x * x;
        if (h2 < 0f) h2 = 0f;
        float h = Mathf.Sqrt(h2);

        // Forward hint in world space
        Vector3 hintWorld =
            (AvatarTransform != null)
            ? AvatarTransform.TransformDirection(kneeForwardHint).normalized
            : kneeForwardHint.normalized;

        // Build knee-plane normal from dir and hint; guarantee it points toward forward
        Vector3 planeN = Vector3.Cross(dir, Vector3.Cross(hintWorld, dir));
        if (planeN.sqrMagnitude < 1e-8f)
        {
            // Degenerate fallback
            planeN = Vector3.Cross(dir, Vector3.up);
            if (planeN.sqrMagnitude < 1e-8f) planeN = Vector3.Cross(dir, Vector3.right);
        }
        planeN.Normalize();

        // Nudge toward the hint if desired
        if (kneeHintWeight < 1f && kneeHintWeight > 0f)
        {
            Vector3 altN = Vector3.Cross(dir, Vector3.up);
            if (altN.sqrMagnitude > 1e-8f) altN.Normalize();
            planeN = Vector3.Slerp(altN, planeN, kneeHintWeight).normalized;
        }

        // FORCE forward bend: flip planeN so its dot with forward is positive
        Vector3 fwd = (AvatarTransform != null) ? AvatarTransform.forward : Vector3.forward;
        if (Vector3.Dot(planeN, fwd) < 0f) planeN = -planeN;

        // Optional manual flip if rig faces the other way
        float bendSign = (flipKneeBend ? -1f : 1f);

        Vector3 knee = hip + dir * x + planeN * (h * bendSign);

        valid = true;
        return knee;
    }

    // ----------- Optional gizmos -------------

    // Call this from your host MonoBehaviour's OnDrawGizmosSelected()
    public void DrawGizmos()
    {
        DrawFootGizmos(left, Color.cyan);
        DrawFootGizmos(right, Color.magenta);
    }

    private void DrawFootGizmos(FootState f, Color c)
    {
        if (f == null || f.bone == null || AvatarTransform == null || Hips == null) return;

        // Spherecast visualization
        Gizmos.color = c * 0.8f;
        Vector3 rayOrigin = GetPerFootRayOrigin(f);
        Vector3 rayDir = Vector3.down;
        float rayLen = f.legLength;
        Gizmos.DrawWireSphere(rayOrigin, raySphereRadius);
        Gizmos.DrawLine(rayOrigin, rayOrigin + rayDir * rayLen);
        Gizmos.DrawWireSphere(rayOrigin + rayDir * rayLen, raySphereRadius);

        // Target pose
        Gizmos.color = c;
        Gizmos.DrawSphere(f.targetPos, 0.015f);

        // Tiny orientation triad at foot target
        const float ax = 0.08f;
        Vector3 p = f.targetPos;
        Gizmos.DrawLine(p, p + f.targetRot * Vector3.forward * ax);
        Gizmos.DrawLine(p, p + f.targetRot * Vector3.right * ax);
        Gizmos.DrawLine(p, p + f.targetRot * Vector3.up * ax);

        // --- LEG CURVE (hip → knee → foot) ---
        bool ok;
        Vector3 hip = Hips.position;
        Vector3 ankle = f.targetPos; // use target so curve reflects placement
        Vector3 knee = SolveKneePosition(f, hip, ankle, out ok);

        if (ok)
        {
            // Visualize joints
            Gizmos.color = new Color(c.r, c.g, c.b, 0.85f);
            Gizmos.DrawSphere(hip, 0.012f);
            Gizmos.DrawSphere(knee, 0.012f);
            Gizmos.DrawSphere(ankle, 0.012f);

            // Draw quadratic Bézier from hip→knee→ankle
            DrawQuadraticBezier(hip, knee, ankle, curveSegments, c);

            // Optional: draw bone lines
            Gizmos.color = c * 0.9f;
            Gizmos.DrawLine(hip, knee);
            Gizmos.DrawLine(knee, ankle);
        }
    }

    private void DrawQuadraticBezier(Vector3 a, Vector3 b, Vector3 c, int segments, Color col)
    {
        segments = Mathf.Max(2, segments);
        Gizmos.color = col;
        Vector3 prev = a;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            // B(t) = (1-t)^2 a + 2(1-t)t b + t^2 c
            float u = 1f - t;
            Vector3 pt = (u * u) * a + (2f * u * t) * b + (t * t) * c;
            Gizmos.DrawLine(prev, pt);
            prev = pt;
        }
    }
    private void PushToRigTargets(FootState f,
                              Action<Vector3> setTargetLocalPos,
                              Action<Quaternion> setTargetLocalRot,
                              Action<Vector3> setHintLocalPos = null)
    {
        if (AvatarTransform == null) return;

        // convert foot target from world -> rig local
       // Vector3 localPos = AvatarTransform.InverseTransformPoint(f.currentPos);

        // for rotation, remove rigRoot's rotation (local space)
       // Quaternion localRot = Quaternion.Inverse(AvatarTransform.rotation) * f.currentRot;

        setTargetLocalPos?.Invoke(f.currentPos);
        setTargetLocalRot?.Invoke(f.currentRot);

        // optional: knee hint (world -> local) using your analytic knee solve
        if (setHintLocalPos != null && Hips != null)
        {
            bool valid;
            Vector3 hip = Hips.position;
            Vector3 ankle = f.currentPos;
            Vector3 knee = SolveKneePosition(f, hip, ankle, out valid);
            if (valid)
            {
                Vector3 kneeLocal = AvatarTransform.InverseTransformPoint(knee);
                setHintLocalPos(kneeLocal);
            }
        }
    }
}
