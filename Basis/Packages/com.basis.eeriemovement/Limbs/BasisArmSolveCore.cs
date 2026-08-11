using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine;

namespace Basis.IK
{
    public struct BasisArmSolveInput
    {
        public Vector3 Shoulder;
        public Vector3 Elbow;
        public Vector3 Hand;
        public Quaternion RootRotation;
        public Quaternion MidRotation;
        public Vector3 TargetPosition;
        public Quaternion TargetRotation;
        public Vector3 HintPosition;
        public bool HintWeight;
        public Quaternion TargetOffset;
        public Vector3 PlayerUp;
        public float HintMaxStepDeg;
        public bool HintIsTracker;
        public Quaternion TipRotation;

        public Quaternion HintRotation;
    }

    public struct BasisArmSolveResult
    {
        public Quaternion MidDelta;
        public Quaternion RootDelta;
        public Quaternion HintDelta;
        public Quaternion MidPostRoll;

        public Quaternion TipRotation;
        public bool HintApplied;

        public Vector3 ElbowSolved;
        public Vector3 HandSolved;
        public Quaternion RootRotationSolved;
        public Quaternion MidRotationSolved;

        public float UpperLength;
        public float LowerLength;
        public float TargetDistance;
        public float ReachRatio;
        public float ElbowAngleDeg;
        public float HintFade;
        public float HintProjMag;
        public float ArmProjMag;
        public byte AxisSource;
        public float HandError;
        public float WristTwistDeg;
        public float WristReliefDeg;
        public float ForearmRollDeg;
    }

    public static class BasisArmSolveCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        public const float MinElbowAngleDeg = 23f;

        public const float MaxElbowAngleDeg = 170f;

        public const float WristRollComfortDeg = 80f;

        public const float WristRollRampStartDeg = 55f;

        public const float WristRollMaxReliefDeg = 70f;

        public const float TrackerRollHandBlend = 0.5f;

        public const float TrackerForearmRollMaxDeg = 120f;

        const float k_WristWrapFadeStartDeg = 155f;
        const float k_WristWrapFadeEndDeg = 178f;

        public static void Solve(in BasisArmSolveInput i, out BasisArmSolveResult r)
        {
            r = default;

            r.MidPostRoll = Quaternion.identity;

            Vector3 aPosition = i.Shoulder;
            Vector3 bPosition = i.Elbow;
            Vector3 cPosition = i.Hand;
            Quaternion rootRot = i.RootRotation;
            Quaternion midRot = i.MidRotation;

            Vector3 tPosition = i.TargetPosition;
            Quaternion tRotation = i.TargetRotation * i.TargetOffset;

            Vector3 ab = bPosition - aPosition;
            Vector3 bc = cPosition - bPosition;
            Vector3 ac = cPosition - aPosition;

            float abLen = ab.magnitude;
            float bcLen = bc.magnitude;
            float totalLen = abLen + bcLen;

            Vector3 atCorrected = tPosition - aPosition;
            float acLen = ac.magnitude;

            float oldAbcAngle = TriangleAngle(acLen, abLen, bcLen);
            float atCorrectedLen = atCorrected.magnitude;
            float newAbcAngle = TriangleAngle(atCorrectedLen, abLen, bcLen);

            newAbcAngle = Mathf.Clamp(newAbcAngle, MinElbowAngleDeg * Mathf.Deg2Rad, MaxElbowAngleDeg * Mathf.Deg2Rad);

            byte axisSource = 0;
            Vector3 axis = Vector3.Cross(ab, bc);
            if (axis.sqrMagnitude < k_SqrEpsilon)
            {
                Vector3 straightArm = ac.sqrMagnitude > k_SqrEpsilon ? ac : bc;
                if (straightArm.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 saN = straightArm.normalized;
                    Vector3 downPole = -i.PlayerUp - saN * Vector3.Dot(-i.PlayerUp, saN);
                    axis = Vector3.Cross(downPole, bc);
                    axisSource = 4;
                }

                if (axis.sqrMagnitude < k_SqrEpsilon)
                {
                    axis = i.HintWeight ? Vector3.Cross(i.HintPosition - aPosition, bc) : Vector3.zero;
                    axisSource = 1;
                }
                if (axis.sqrMagnitude < k_SqrEpsilon)
                {
                    axis = Vector3.Cross(atCorrected, bc);
                    axisSource = 2;
                }

                if (axis.sqrMagnitude < k_SqrEpsilon)
                {
                    axis = i.PlayerUp;
                    axisSource = 3;
                }
            }
            axis = axis.normalized;

            float a = 0.5f * (oldAbcAngle - newAbcAngle);
            float sin = Mathf.Sin(a);
            float cos = Mathf.Cos(a);
            Quaternion deltaR = new Quaternion(axis.x * sin, axis.y * sin, axis.z * sin, cos);

            midRot = deltaR * midRot;
            cPosition = bPosition + deltaR * (cPosition - bPosition);
            ac = cPosition - aPosition;

            Quaternion rootDelta = Quaternion.identity;
            if (atCorrectedLen > k_Epsilon)
            {
                rootDelta = BasisQuaternionExt.FromToRotation(ac, atCorrected);
                rootRot = rootDelta * rootRot;

                bPosition = aPosition + rootDelta * (bPosition - aPosition);
                cPosition = aPosition + rootDelta * (cPosition - aPosition);
                midRot = rootDelta * midRot;
            }

            Quaternion hintR = Quaternion.identity;
            bool hintApplied = false;
            float hintFade = 0f;
            float swivelUsedRad = 0f;
            float hintProjMag = 0f;
            float armProjMag = 0f;
            if (i.HintWeight)
            {
                float acSqrMag = ac.sqrMagnitude;
                if (acSqrMag > 0f)
                {
                    ab = bPosition - aPosition;
                    ac = cPosition - aPosition;

                    Vector3 acNorm = ac / Mathf.Sqrt(acSqrMag);
                    Vector3 ah = i.HintPosition - aPosition;
                    Vector3 abProj = ab - acNorm * Vector3.Dot(ab, acNorm);
                    Vector3 ahProj = ah - acNorm * Vector3.Dot(ah, acNorm);
                    Vector3 elbowDir = Vector3.Cross(acNorm, rootDelta * axis);
                    elbowDir -= acNorm * Vector3.Dot(elbowDir, acNorm);
                    hintProjMag = ahProj.magnitude;
                    armProjMag = abProj.magnitude;

                    hintFade = 1f;

                    if (ahProj.sqrMagnitude > k_SqrEpsilon && elbowDir.sqrMagnitude > k_SqrEpsilon)
                    {
                        float effFade = hintFade;
                        if (effFade < 1f)
                        {
                            float denom = Mathf.Sqrt(elbowDir.sqrMagnitude * ahProj.sqrMagnitude);
                            float cosBA = denom > k_Epsilon ? Mathf.Clamp(Vector3.Dot(elbowDir, ahProj) / denom, -1f, 1f) : 1f;
                            float flipDeg = Mathf.Acos(cosBA) * Mathf.Rad2Deg;
                            float commit = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((flipDeg - 90f) / 80f));

                            commit *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((hintFade - 0.3f) / 0.25f));
                            effFade = hintFade + (1f - hintFade) * commit;
                        }

                        swivelUsedRad = SignedAngleRad(elbowDir, ahProj, acNorm) * effFade;
                        float swivel = swivelUsedRad;

                        float maxStep = i.HintMaxStepDeg * Mathf.Deg2Rad;
                        if (swivel > maxStep) swivel = maxStep;
                        else if (swivel < -maxStep) swivel = -maxStep;
                        swivelUsedRad = swivel;

                        hintR = AngleAxisRad(swivel, acNorm);

                        rootRot = hintR * rootRot;
                        bPosition = aPosition + hintR * (bPosition - aPosition);
                        cPosition = aPosition + hintR * (cPosition - aPosition);
                        midRot = hintR * midRot;
                        hintApplied = true;
                    }
                }
            }

            if (i.HintWeight && !i.HintIsTracker)
            {
                float poleCond = totalLen > k_Epsilon ? hintProjMag / totalLen : 1f;

                if (i.HintIsTracker)
                {
                    float floorBlend = Mathf.Clamp01((poleCond - 0.05f) / 0.05f);
                    poleCond = Mathf.Lerp(poleCond, Mathf.Max(poleCond, 0.30f), floorBlend);
                }
                float collapse = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((poleCond - 0.15f) / 0.15f));
                Vector3 acStab = cPosition - aPosition;
                if (collapse > 0f && acStab.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 acStabN = acStab.normalized;
                    Vector3 downPole = -i.PlayerUp - acStabN * Vector3.Dot(-i.PlayerUp, acStabN);
                    Vector3 elbowPole = (bPosition - aPosition) - acStabN * Vector3.Dot(bPosition - aPosition, acStabN);
                    if (downPole.sqrMagnitude > k_SqrEpsilon && elbowPole.sqrMagnitude > k_SqrEpsilon)
                    {
                        float stabSwivel = SignedAngleRad(elbowPole, downPole, acStabN) * collapse;

                        float budget = i.HintMaxStepDeg * Mathf.Deg2Rad - Mathf.Abs(swivelUsedRad);
                        if (!(budget > 0f)) budget = 0f;
                        if (stabSwivel > budget) stabSwivel = budget;
                        else if (stabSwivel < -budget) stabSwivel = -budget;

                        Quaternion stab = AngleAxisRad(stabSwivel, acStabN);
                        rootRot = stab * rootRot;
                        bPosition = aPosition + stab * (bPosition - aPosition);
                        cPosition = aPosition + stab * (cPosition - aPosition);
                        midRot = stab * midRot;
                        hintR = stab * hintR;
                    }
                }
            }

            float tipRotSqr = i.TipRotation.x * i.TipRotation.x + i.TipRotation.y * i.TipRotation.y
                            + i.TipRotation.z * i.TipRotation.z + i.TipRotation.w * i.TipRotation.w;
            if (tipRotSqr > 0.5f && !i.HintIsTracker)
            {
                Vector3 fore = cPosition - bPosition;
                Vector3 acRelief = cPosition - aPosition;
                if (fore.sqrMagnitude > k_SqrEpsilon && acRelief.sqrMagnitude > k_SqrEpsilon)
                {
                    Quaternion neutral = midRot * Quaternion.Inverse(i.MidRotation) * i.TipRotation;
                    float twistRad = TwistAngleRad(tRotation * Quaternion.Inverse(neutral), fore.normalized);
                    r.WristTwistDeg = twistRad * Mathf.Rad2Deg;

                    float rollAbs = Mathf.Abs(twistRad);
                    float rampStart = WristRollRampStartDeg * Mathf.Deg2Rad;
                    float band = WristRollComfortDeg * Mathf.Deg2Rad;

                    float relief;
                    if (rollAbs <= rampStart)
                    {
                        relief = 0f;
                    }
                    else if (rollAbs <= band)
                    {
                        float t = rollAbs - rampStart;
                        relief = t * t / (2f * (band - rampStart));
                    }
                    else
                    {
                        relief = 0.5f * (band - rampStart) + (rollAbs - band);
                    }

                    float reliefCap = WristRollMaxReliefDeg * Mathf.Deg2Rad;
                    if (relief > reliefCap) relief = reliefCap;
                    relief *= 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                        (rollAbs * Mathf.Rad2Deg - k_WristWrapFadeStartDeg) / (k_WristWrapFadeEndDeg - k_WristWrapFadeStartDeg)));

                    if (relief > 0f)
                    {
                        float reliefSigned = twistRad < 0f ? -relief : relief;
                        Quaternion reliefR = AngleAxisRad(reliefSigned, acRelief.normalized);

                        rootRot = reliefR * rootRot;
                        bPosition = aPosition + reliefR * (bPosition - aPosition);
                        cPosition = aPosition + reliefR * (cPosition - aPosition);
                        midRot = reliefR * midRot;
                        hintR = reliefR * hintR;
                        r.WristReliefDeg = reliefSigned * Mathf.Rad2Deg;
                    }
                }
            }

            float guardSwivel = BasisElbowAnatomyCore.GuardSwivelRad(aPosition, bPosition, cPosition, i.PlayerUp, totalLen);
            if (guardSwivel != 0f)
            {
                Vector3 acGuard = cPosition - aPosition;
                if (acGuard.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 acGuardN = acGuard.normalized;
                    Quaternion guard = AngleAxisRad(guardSwivel, acGuardN);

                    rootRot = guard * rootRot;
                    bPosition = aPosition + guard * (bPosition - aPosition);
                    cPosition = aPosition + guard * (cPosition - aPosition);
                    midRot = guard * midRot;
                    hintR = guard * hintR;
                }
            }

            float hintRotSqr = i.HintRotation.x * i.HintRotation.x + i.HintRotation.y * i.HintRotation.y
                             + i.HintRotation.z * i.HintRotation.z + i.HintRotation.w * i.HintRotation.w;
            if (i.HintIsTracker && hintRotSqr > 0.5f)
            {
                Vector3 foreRoll = cPosition - bPosition;
                if (foreRoll.sqrMagnitude > k_SqrEpsilon)
                {
                    Vector3 foreRollN = foreRoll.normalized;
                    float trackerRoll = TwistAngleRad(i.HintRotation * Quaternion.Inverse(midRot), foreRollN);

                    float roll = trackerRoll;
                    if (tipRotSqr > 0.5f)
                    {
                        Quaternion neutral = midRot * Quaternion.Inverse(i.MidRotation) * i.TipRotation;
                        float handRoll = TwistAngleRad(tRotation * Quaternion.Inverse(neutral), foreRollN);
                        r.WristTwistDeg = handRoll * Mathf.Rad2Deg;

                        float d = handRoll - trackerRoll;
                        if (d > Mathf.PI) d -= 2f * Mathf.PI;
                        else if (d < -Mathf.PI) d += 2f * Mathf.PI;
                        float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(
                            (Mathf.Abs(d) * Mathf.Rad2Deg - k_WristWrapFadeStartDeg) / (k_WristWrapFadeEndDeg - k_WristWrapFadeStartDeg)));
                        roll = trackerRoll + TrackerRollHandBlend * d * fade;
                    }

                    float rollAbs = Mathf.Abs(roll);
                    float rollCap = TrackerForearmRollMaxDeg * Mathf.Deg2Rad;
                    if (rollAbs > rollCap) rollAbs = rollCap;
                    if (rollAbs > 1e-6f)
                    {
                        float rollSigned = roll < 0f ? -rollAbs : rollAbs;
                        r.MidPostRoll = AngleAxisRad(rollSigned, foreRollN);
                        midRot = r.MidPostRoll * midRot;
                        r.ForearmRollDeg = rollSigned * Mathf.Rad2Deg;
                    }
                }
            }

            r.MidDelta = deltaR;
            r.RootDelta = rootDelta;
            r.HintDelta = hintR;
            r.TipRotation = tRotation;
            r.HintApplied = hintApplied;

            r.ElbowSolved = bPosition;
            r.HandSolved = cPosition;
            r.RootRotationSolved = rootRot;
            r.MidRotationSolved = midRot;

            r.UpperLength = abLen;
            r.LowerLength = bcLen;
            r.TargetDistance = atCorrectedLen;
            r.ReachRatio = (totalLen > k_Epsilon) ? atCorrectedLen / totalLen : 0f;
            r.ElbowAngleDeg = AngleDeg(aPosition - bPosition, cPosition - bPosition);
            r.HintFade = hintFade;
            r.HintProjMag = hintProjMag;
            r.ArmProjMag = armProjMag;
            r.AxisSource = axisSource;
            r.HandError = (cPosition - tPosition).magnitude;
        }

        static float SignedAngleRad(Vector3 from, Vector3 to, Vector3 axis)
        {
            float denom = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (!(denom > k_Epsilon))
            {
                return 0f;
            }

            float c = Vector3.Dot(from, to) / denom;
            c = c > 1f ? 1f : (c > -1f ? c : -1f);
            float angle = Mathf.Acos(c);
            return Vector3.Dot(axis, Vector3.Cross(from, to)) < 0f ? -angle : angle;
        }

        static Quaternion AngleAxisRad(float radians, Vector3 axis)
        {
            float h = 0.5f * radians;
            float s = Mathf.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(h));
        }

        static float TwistAngleRad(Quaternion q, Vector3 axis)
        {
            float s = q.x * axis.x + q.y * axis.y + q.z * axis.z;
            float c = q.w;
            if (c < 0f) { s = -s; c = -c; }
            if (!(s * s + c * c > k_SqrEpsilon))
            {
                return 0f;
            }
            return 2f * Mathf.Atan2(s, c);
        }

        static float AngleDeg(Vector3 from, Vector3 to)
        {
            float denom = Mathf.Sqrt(from.sqrMagnitude * to.sqrMagnitude);
            if (denom < k_Epsilon)
            {
                return 0f;
            }

            float c = Mathf.Clamp(Vector3.Dot(from, to) / denom, -1f, 1f);
            return Mathf.Acos(c) * Mathf.Rad2Deg;
        }

        static float TriangleAngle(float aLen, float aLen1, float aLen2)
        {
            if (aLen1 <= k_Epsilon || aLen2 <= k_Epsilon)
            {
                return 0f;
            }

            float c = Mathf.Clamp((aLen1 * aLen1 + aLen2 * aLen2 - aLen * aLen) / (2.0f * aLen1 * aLen2), -1.0f, 1.0f);
            return Mathf.Acos(c);
        }
    }

    public struct BasisArmBendLookup
    {
        public const int GridSize = 11;
        public const int GridSizeSq = GridSize * GridSize;
        public const int TotalEntries = GridSize * GridSize * GridSize;

        public static Vector3[] GenerateDefaultTable()
        {
            var table = new Vector3[TotalEntries];
            float step = 2f / (GridSize - 1);

            for (int iz = 0; iz < GridSize; iz++)
            for (int iy = 0; iy < GridSize; iy++)
            for (int ix = 0; ix < GridSize; ix++)
            {
                float x = -1f + ix * step;
                float y = -1f + iy * step;
                float z = -1f + iz * step;

                Vector3 bendDir;

                float forwardness = Mathf.Clamp01(z);

                float upness = Mathf.Clamp01(y);

                bendDir = new Vector3(0f, -0.3f, -1f);

                bendDir = Vector3.Lerp(bendDir, new Vector3(0f, -1f, -0.3f), forwardness * 0.6f);

                bendDir = Vector3.Lerp(bendDir, new Vector3(0.7f, -0.8f, -0.2f), upness * 0.5f);

                float inwardness = Mathf.Clamp01(-x);
                bendDir = Vector3.Lerp(bendDir, new Vector3(1f, -0.5f, 0f), inwardness * 0.4f);

                float behindness = Mathf.Clamp01(-z);
                bendDir = Vector3.Lerp(bendDir, new Vector3(0.4f, -0.75f, -0.55f), behindness * 0.7f);

                float downness = Mathf.Clamp01(-y);
                bendDir = Vector3.Lerp(bendDir, new Vector3(0f, 0f, -1f), downness * 0.3f);

                int idx = ix + iy * GridSize + iz * GridSizeSq;
                table[idx] = bendDir.normalized;
            }

            return table;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float ClampToGrid(float v) =>
            v > 0f ? (v < GridSize - 1.001f ? v : GridSize - 1.001f) : 0f;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 SampleTrilinear(NativeArray<Vector3> table, Vector3 normalizedPos)
        {
            float fx = ClampToGrid((normalizedPos.x * 0.5f + 0.5f) * (GridSize - 1));
            float fy = ClampToGrid((normalizedPos.y * 0.5f + 0.5f) * (GridSize - 1));
            float fz = ClampToGrid((normalizedPos.z * 0.5f + 0.5f) * (GridSize - 1));

            int x0 = (int)fx; int x1 = Mathf.Min(x0 + 1, GridSize - 1);
            int y0 = (int)fy; int y1 = Mathf.Min(y0 + 1, GridSize - 1);
            int z0 = (int)fz; int z1 = Mathf.Min(z0 + 1, GridSize - 1);

            float tx = fx - x0;
            float ty = fy - y0;
            float tz = fz - z0;

            Vector3 c000 = table[x0 + y0 * GridSize + z0 * GridSizeSq];
            Vector3 c100 = table[x1 + y0 * GridSize + z0 * GridSizeSq];
            Vector3 c010 = table[x0 + y1 * GridSize + z0 * GridSizeSq];
            Vector3 c110 = table[x1 + y1 * GridSize + z0 * GridSizeSq];
            Vector3 c001 = table[x0 + y0 * GridSize + z1 * GridSizeSq];
            Vector3 c101 = table[x1 + y0 * GridSize + z1 * GridSizeSq];
            Vector3 c011 = table[x0 + y1 * GridSize + z1 * GridSizeSq];
            Vector3 c111 = table[x1 + y1 * GridSize + z1 * GridSizeSq];

            Vector3 c00 = Vector3.Lerp(c000, c100, tx);
            Vector3 c10 = Vector3.Lerp(c010, c110, tx);
            Vector3 c01 = Vector3.Lerp(c001, c101, tx);
            Vector3 c11 = Vector3.Lerp(c011, c111, tx);

            Vector3 c0 = Vector3.Lerp(c00, c10, ty);
            Vector3 c1 = Vector3.Lerp(c01, c11, ty);

            return Vector3.Lerp(c0, c1, tz).normalized;
        }
    }
}
