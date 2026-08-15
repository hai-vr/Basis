using UnityEngine;

namespace Basis.IK
{
    public static class BasisChestSpringCore
    {
        public static void Step(Vector3 pos, Vector3 vel, Vector3 target, float dt, float hz, float damping,
            out Vector3 newPos, out Vector3 newVel)
        {
            float omega = 2f * Mathf.PI * hz;
            float omegaSq = omega * omega;
            float twoOmegaDamping = 2f * omega * Mathf.Max(0f, damping);

            float denom = 1f + dt * twoOmegaDamping + dt * dt * omegaSq;
            newVel = (vel + dt * omegaSq * (target - pos)) / denom;
            newPos = pos + dt * newVel;
        }
    }

    public static class BasisHipFrameSpringCore
    {
        public static void Step(Quaternion rot, Vector3 angVel, Quaternion target, float dt, float hz, float damping,
            out Quaternion newRot, out Vector3 newAngVel)
        {
            Quaternion errQ = target * Quaternion.Inverse(rot);
            if (errQ.w < 0f) errQ = new Quaternion(-errQ.x, -errQ.y, -errQ.z, -errQ.w);
            Vector3 ev = new Vector3(errQ.x, errQ.y, errQ.z);
            float evLen = Mathf.Sqrt(ev.x * ev.x + ev.y * ev.y + ev.z * ev.z);
            Vector3 e;
            if (evLen > 1e-6f)
            {
                float angle = 2f * Mathf.Atan2(evLen, errQ.w);
                e = ev * (angle / evLen);
            }
            else
            {
                e = 2f * ev;
            }

            float omega = 2f * Mathf.PI * hz;
            float omegaSq = omega * omega;
            float twoOmegaDamping = 2f * omega * Mathf.Max(0f, damping);

            float denom = 1f + dt * twoOmegaDamping + dt * dt * omegaSq;
            newAngVel = (angVel + dt * omegaSq * e) / denom;

            Vector3 dTheta = dt * newAngVel;
            float ang = Mathf.Sqrt(dTheta.x * dTheta.x + dTheta.y * dTheta.y + dTheta.z * dTheta.z);
            Quaternion step;
            if (ang > 1e-8f)
            {
                float half = 0.5f * ang;
                float s = Mathf.Sin(half) / ang;
                step = new Quaternion(dTheta.x * s, dTheta.y * s, dTheta.z * s, Mathf.Cos(half));
            }
            else
            {
                step = Quaternion.identity;
            }

            Quaternion outRot = step * rot;
            float n = Mathf.Sqrt(outRot.x * outRot.x + outRot.y * outRot.y + outRot.z * outRot.z + outRot.w * outRot.w);
            newRot = n > 1e-8f
                ? new Quaternion(outRot.x / n, outRot.y / n, outRot.z / n, outRot.w / n)
                : Quaternion.identity;
        }
    }

    public struct BasisHeadPitchSwingInput
    {
        public float PitchDeg;
        public float YawDeg;
        public Vector3 EyeFromNeck;
        public float Strength;
        public float BackwardScale;
    }

    public struct BasisHeadPitchSwingResult
    {
        public Vector3 Offset;
        public float ForwardMeters;
    }

    public static class BasisHeadPitchSwingCore
    {
        const float k_Epsilon = 1e-5f;

        public static void Solve(in BasisHeadPitchSwingInput i, out BasisHeadPitchSwingResult r)
        {
            r = default;

            float lever = i.EyeFromNeck.y;
            if (!(lever > k_Epsilon)) return;
            if (!(i.PitchDeg > -180f && i.PitchDeg < 180f)) return;
            if (!(i.YawDeg > -720f && i.YawDeg < 720f)) return;
            if (!(i.Strength > 0f)) return;

            float p = i.PitchDeg * Mathf.Deg2Rad;
            float forwardRest = i.EyeFromNeck.z;

            float swung = lever * Mathf.Sin(p) + forwardRest * Mathf.Cos(p);
            float forward = (swung - forwardRest) * i.Strength;

            if (forward < 0f) forward *= i.BackwardScale;

            if (!(forward > -4f && forward < 4f)) return;
            forward = Mathf.Clamp(forward, -1f, 1f);

            float yawRad = i.YawDeg * Mathf.Deg2Rad;
            Vector3 heading = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));

            r.ForwardMeters = forward;
            r.Offset = new Vector3(heading.x * forward, 0f, heading.z * forward);
        }
    }

    public struct BasisHipHingeInput
    {
        public Vector3 HeadPos;
        public Vector3 HipsPos;
        public Quaternion HipsRot;
        public Vector3 PlayerUp;
        public float StartDeg;
        public float MaxAddDeg;
    }

    public struct BasisHipHingeResult
    {
        public Quaternion HipsRot;
        public bool Applied;
        public float LeanDeg;
        public float AddDeg;
    }

    public static class BasisHipHingeCore
    {
        const float k_Epsilon = 1e-5f;
        const float k_SqrEpsilon = 1e-8f;

        public const float PelvisFollowSlope = 1.0f;

        public static void Solve(in BasisHipHingeInput i, out BasisHipHingeResult r)
        {
            r = default;
            r.HipsRot = i.HipsRot;
            r.LeanDeg = float.NaN;

            if (i.MaxAddDeg <= 0f)
            {
                return;
            }

            Vector3 hipsToHead = i.HeadPos - i.HipsPos;
            float upDot = Vector3.Dot(hipsToHead, i.PlayerUp);
            Vector3 horizontal = hipsToHead - i.PlayerUp * upDot;
            float horizMag = horizontal.magnitude;

            if (horizMag < k_Epsilon)
            {
                return;
            }

            float leanDeg = Mathf.Atan2(horizMag, upDot) * Mathf.Rad2Deg;
            r.LeanDeg = leanDeg;
            if (leanDeg <= i.StartDeg)
            {
                return;
            }

            float excess = (leanDeg - i.StartDeg) * PelvisFollowSlope;
            float addDeg = Saturate(excess, i.MaxAddDeg);

            Vector3 hingeAxis = Vector3.Cross(i.PlayerUp, horizontal / horizMag);
            if (hingeAxis.sqrMagnitude < k_SqrEpsilon)
            {
                return;
            }

            hingeAxis.Normalize();
            r.AddDeg = addDeg;
            r.Applied = true;
            r.HipsRot = Quaternion.AngleAxis(addDeg, hingeAxis) * i.HipsRot;
        }

        public static float Saturate(float x, float cap) => BasisTrunkCounterbalanceCore.Saturate(x, cap);
    }
}
