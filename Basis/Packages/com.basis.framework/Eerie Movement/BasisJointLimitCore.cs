using UnityEngine;

namespace Basis.IK
{
    public struct BasisJointRom
    {
        public float FlexDeg;
        public float ExtDeg;
        public float AbductDeg;
        public float AdductDeg;
        public float TwistDeg;
        public float TwistEdgeDeg;

        public BasisJointRom(float flexDeg, float extDeg, float abductDeg, float adductDeg, float twistDeg)
        {
            FlexDeg = flexDeg;
            ExtDeg = extDeg;
            AbductDeg = abductDeg;
            AdductDeg = adductDeg;
            TwistDeg = twistDeg;
            TwistEdgeDeg = twistDeg;
        }

        public BasisJointRom(float flexDeg, float extDeg, float abductDeg, float adductDeg, float twistDeg, float twistEdgeDeg)
        {
            FlexDeg = flexDeg;
            ExtDeg = extDeg;
            AbductDeg = abductDeg;
            AdductDeg = adductDeg;
            TwistDeg = twistDeg;
            TwistEdgeDeg = twistEdgeDeg;
        }
    }

    public struct BasisJointRestFrame
    {
        public Quaternion RestLocalRot;
        public Vector3 Right;
        public Vector3 Up;
        public Vector3 Forward;
        public bool Valid;
    }

    public struct BasisJointClampInfo
    {
        public bool SwingClamped;
        public bool TwistClamped;
        public float FlexDeg;
        public float LatDeg;
        public float TwistDeg;
        public float FlexGuardedDeg;
        public float LatGuardedDeg;
        public float TwistGuardedDeg;
        public float ConeRadius;
        public float TwistLimitDeg;

        public bool Touched => SwingClamped || TwistClamped;
    }

    public static class BasisJointLimitCore
    {
        const float k_Epsilon = 1e-5f;

        public const float OvershootAsymptote = 1.25f;

        public static Quaternion Conj(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, q.w);

        public static Quaternion AxisAngle(float deg, Vector3 axis)
        {
            float h = deg * (0.5f * Mathf.Deg2Rad);
            float s = Mathf.Sin(h);
            return new Quaternion(axis.x * s, axis.y * s, axis.z * s, Mathf.Cos(h));
        }

        public static float Saturate(float x, float soft, float hard)
        {
            if (!(x > soft))
            {
                return x;
            }

            float m = hard - soft;
            if (!(m > k_Epsilon))
            {
                return soft;
            }

            float e = x - soft;
            return soft + m * e / (m + e);
        }

        public static float SaturateSigned(float x, float softAbs, float hardAbs)
        {
            float a = x < 0f ? -x : x;
            float g = Saturate(a, softAbs, hardAbs);
            return x < 0f ? -g : g;
        }

        public static float TwistLimitAt(float coneRadius, in BasisJointRom rom)
        {
            float t = coneRadius > 0f ? coneRadius : 0f;
            if (t > 1f)
            {
                t = 1f;
            }

            float lim = rom.TwistDeg + (rom.TwistEdgeDeg - rom.TwistDeg) * t;
            return lim > 0f ? lim : 0f;
        }

        public static Quaternion Clamp(Quaternion localRot, in BasisJointRestFrame frame, in BasisJointRom rom)
        {
            return Clamp(localRot, frame, rom, out _);
        }

        public static Quaternion Clamp(Quaternion localRot, in BasisJointRestFrame frame, in BasisJointRom rom, out BasisJointClampInfo info)
        {
            info = default;
            if (!frame.Valid)
            {
                return localRot;
            }

            Quaternion delta = localRot * Conj(frame.RestLocalRot);
            Decompose(delta, frame, out float flexDeg, out float latDeg, out float twistDeg);

            if (!(flexDeg * flexDeg + latDeg * latDeg + twistDeg * twistDeg >= 0f))
            {
                return localRot;
            }

            info.FlexDeg = flexDeg;
            info.LatDeg = latDeg;
            info.TwistDeg = twistDeg;

            float flexLim = flexDeg >= 0f ? rom.FlexDeg : rom.ExtDeg;
            float latLim = latDeg >= 0f ? rom.AbductDeg : rom.AdductDeg;
            if (flexLim < 0f)
            {
                flexLim = 0f;
            }
            if (latLim < 0f)
            {
                latLim = 0f;
            }

            float fN = flexDeg / (flexLim > k_Epsilon ? flexLim : k_Epsilon);
            float lN = latDeg / (latLim > k_Epsilon ? latLim : k_Epsilon);
            float q = fN * fN + lN * lN;

            float coneR = q > 0f ? Mathf.Sqrt(q) : 0f;
            info.ConeRadius = coneR;

            float flexGuarded = flexDeg;
            float latGuarded = latDeg;

            if (q > 1f && coneR > k_Epsilon)
            {
                float rGuard = Saturate(coneR, 1f, OvershootAsymptote);
                float swingScale = rGuard / coneR;
                flexGuarded = flexDeg * swingScale;
                latGuarded = latDeg * swingScale;
                info.SwingClamped = true;
            }

            float twistLim = TwistLimitAt(coneR, rom);
            info.TwistLimitDeg = twistLim;

            float twistGuarded = twistDeg;
            float twistAbs = twistDeg < 0f ? -twistDeg : twistDeg;
            if (twistAbs > twistLim)
            {
                twistGuarded = SaturateSigned(twistDeg, twistLim, twistLim * OvershootAsymptote);
                info.TwistClamped = true;
            }

            info.FlexGuardedDeg = flexGuarded;
            info.LatGuardedDeg = latGuarded;
            info.TwistGuardedDeg = twistGuarded;

            if (!info.SwingClamped && !info.TwistClamped)
            {
                return localRot;
            }

            return Recompose(flexGuarded, latGuarded, twistGuarded, frame);
        }

        public static void Decompose(Quaternion delta, in BasisJointRestFrame frame, out float flexDeg, out float latDeg, out float twistDeg)
        {
            if (delta.w < 0f)
            {
                delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
            }

            Vector3 v = new Vector3(delta.x, delta.y, delta.z);
            float proj = Vector3.Dot(v, frame.Up);

            Quaternion twist = new Quaternion(frame.Up.x * proj, frame.Up.y * proj, frame.Up.z * proj, delta.w);
            float n2 = twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w;

            if (!(n2 > k_Epsilon))
            {
                twist = Quaternion.identity;
                twistDeg = 0f;
            }
            else
            {
                float inv = 1f / Mathf.Sqrt(n2);
                twist = new Quaternion(twist.x * inv, twist.y * inv, twist.z * inv, twist.w * inv);
                float s = twist.x * frame.Up.x + twist.y * frame.Up.y + twist.z * frame.Up.z;
                twistDeg = 2f * Mathf.Atan2(s, twist.w) * Mathf.Rad2Deg;
            }

            Quaternion swing = delta * Conj(twist);
            if (swing.w < 0f)
            {
                swing = new Quaternion(-swing.x, -swing.y, -swing.z, -swing.w);
            }

            Vector3 sv = new Vector3(swing.x, swing.y, swing.z);
            float svLen = sv.magnitude;

            if (svLen > k_Epsilon)
            {
                float angleDeg = 2f * Mathf.Atan2(svLen, swing.w) * Mathf.Rad2Deg;
                Vector3 swingVec = sv * (angleDeg / svLen);
                flexDeg = Vector3.Dot(swingVec, frame.Right);
                latDeg = Vector3.Dot(swingVec, frame.Forward);
            }
            else
            {
                flexDeg = 0f;
                latDeg = 0f;
            }
        }

        public static Quaternion Recompose(float flexDeg, float latDeg, float twistDeg, in BasisJointRestFrame frame)
        {
            Vector3 swingVec = frame.Right * flexDeg + frame.Forward * latDeg;
            float mag = swingVec.magnitude;

            Quaternion swing = mag > k_Epsilon
                ? AxisAngle(mag, swingVec * (1f / mag))
                : Quaternion.identity;

            Quaternion twist = AxisAngle(twistDeg, frame.Up);
            return swing * twist * frame.RestLocalRot;
        }

        public static BasisJointRestFrame BuildRestFrame(Vector3 boneWorldPos, Vector3 childWorldPos, Quaternion boneWorldRot, Quaternion parentWorldRot, Vector3 rightHintWorld)
        {
            BasisJointRestFrame frame = default;

            Vector3 up = childWorldPos - boneWorldPos;
            float upLen = up.magnitude;
            if (!(upLen > k_Epsilon))
            {
                return frame;
            }
            Vector3 upW = up * (1f / upLen);

            float rightLen = rightHintWorld.magnitude;
            if (!(rightLen > k_Epsilon))
            {
                return frame;
            }
            Vector3 rightW = rightHintWorld * (1f / rightLen);

            rightW = rightW - upW * Vector3.Dot(rightW, upW);
            float rLen = rightW.magnitude;
            if (!(rLen > k_Epsilon))
            {
                return frame;
            }
            rightW = rightW * (1f / rLen);

            Vector3 forwardW = Vector3.Cross(rightW, upW);

            Quaternion invParent = Conj(parentWorldRot);
            frame.RestLocalRot = invParent * boneWorldRot;
            frame.Right = invParent * rightW;
            frame.Up = invParent * upW;
            frame.Forward = invParent * forwardW;
            frame.Valid = true;
            return frame;
        }
    }
}
