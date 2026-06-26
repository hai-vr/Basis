using Basis.Scripts.Networking.Sync;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Coverage for the synced-transform encoding choices: per-section Ranged defaults, the Inherit/Raw/Half/Ranged
    /// mode-to-wire-width mapping, the rotation sync modes (smallest-three, 4-float quaternion Half/Raw, Euler) and
    /// the uniform-scale single-float saving. These mirror what BasisSyncedTransform registers, validated through
    /// the real codec.
    /// </summary>
    public class BasisSyncTransformTests
    {
        static readonly Vector3[] Eulers =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(30f, 45f, 60f),
            new Vector3(-120f, 200f, 15f),
            new Vector3(89f, -33f, 170f),
            new Vector3(10f, 270f, -95f),
        };

        [Test]
        public void Defaults_ArePerSectionRanges()
        {
            BasisTransformAxisCompression pos = BasisTransformAxisCompression.PositionDefault;
            Assert.AreEqual(BasisTransformAxisMode.Inherit, pos.Mode, "default stays Inherit (lossless until tuned)");
            Assert.AreEqual(-1024f, pos.Min);
            Assert.AreEqual(1024f, pos.Max);
            Assert.AreEqual(16, pos.Bits);

            BasisTransformAxisCompression rot = BasisTransformAxisCompression.RotationDefault;
            Assert.AreEqual(0f, rot.Min);
            Assert.AreEqual(360f, rot.Max, "Euler angles span a full turn");

            BasisTransformAxisCompression scale = BasisTransformAxisCompression.ScaleDefault;
            Assert.AreEqual(0f, scale.Min);
            Assert.AreEqual(64f, scale.Max);

            Assert.AreNotEqual(0f, pos.Min, "position default must allow negatives, not the old 0..1");
            Assert.Greater(scale.Max, 1f, "scale default must allow >1x, not the old 0..1");
        }

        [Test]
        public void ToSpec_MapsModeToWireWidth()
        {
            Assert.AreEqual(BasisQuantMode.Raw, Make(BasisTransformAxisMode.Inherit).ToSpec().Mode, "Inherit resolves to Raw on the wire");
            Assert.AreEqual(32, Make(BasisTransformAxisMode.Inherit).ToSpec().BitCount);
            Assert.AreEqual(32, Make(BasisTransformAxisMode.Raw).ToSpec().BitCount);
            Assert.AreEqual(16, Make(BasisTransformAxisMode.Half).ToSpec().BitCount);

            var ranged = new BasisTransformAxisCompression { Mode = BasisTransformAxisMode.Ranged, Min = -1f, Max = 1f, Bits = 10 };
            Assert.AreEqual(BasisQuantMode.Ranged, ranged.ToSpec().Mode);
            Assert.AreEqual(10, ranged.ToSpec().BitCount);
        }

        [Test]
        public void QuaternionRaw_FourFloats_RoundTripsExact()
        {
            foreach (Vector3 e in Eulers)
            {
                Quaternion q = Quaternion.Euler(e);
                Quaternion got = RoundTripQuatFloats(BasisQuantSpec.Raw, q);
                Assert.LessOrEqual(Quaternion.Angle(q, got), 0.05f, $"raw quaternion {e}");
            }
        }

        [Test]
        public void QuaternionHalf_FourFloats_RoundTripsWithinOneDegree()
        {
            foreach (Vector3 e in Eulers)
            {
                Quaternion q = Quaternion.Euler(e);
                Quaternion got = RoundTripQuatFloats(BasisQuantSpec.Half, q);
                Assert.LessOrEqual(Quaternion.Angle(q, got), 1.5f, $"half quaternion {e}");
            }
        }

        [Test]
        public void SmallestThree_RoundTripsWithinOneDegree()
        {
            (BasisSyncSchema s, BasisSyncValues v) = RotField();
            int off = s.GetField(0).Offset;
            foreach (Vector3 e in Eulers)
            {
                Unity.Mathematics.quaternion q = BasisSyncTestSupport.Quat(e.x, e.y, e.z);
                v.Rot[off] = q;
                BasisSyncValues dst = BasisSyncTestSupport.RoundTripKeyframe(s, v);
                Assert.LessOrEqual(BasisSyncTestSupport.QuatAngle(q, dst.Rot[off]), 1.5f, $"smallest-three {e}");
            }
        }

        [Test]
        public void SmallestThree_VariableBits_RoundTripAndSize([Values(6, 8, 9, 12, 14)] int magBits)
        {
            var s = new BasisSyncSchema();
            s.AddRotation(true, magBits);
            s.Lock();
            var v = new BasisSyncValues();
            v.Allocate(s);
            int off = s.GetField(0).Offset;

            float worst = 0f;
            foreach (Vector3 e in Eulers)
            {
                Unity.Mathematics.quaternion q = BasisSyncTestSupport.Quat(e.x, e.y, e.z);
                v.Rot[off] = q;
                BasisSyncValues dst = BasisSyncTestSupport.RoundTripKeyframe(s, v);
                worst = Mathf.Max(worst, BasisSyncTestSupport.QuatAngle(q, dst.Rot[off]));
            }

            float tol = 200f / (1 << magBits) + 0.1f;
            Assert.LessOrEqual(worst, tol, $"magBits={magBits} worst angle {worst}");

            BasisSyncTestSupport.SerializeKeyframe(s, v, 1, out int len);
            int payloadBytes = (2 + 3 * (1 + magBits) + 7) / 8;
            Assert.AreEqual(BasisSyncCodec.HeaderSize + payloadBytes, len, $"magBits={magBits} packet size");
        }

        [Test]
        public void RotationModes_WireSizeOrdering()
        {
            int smallest = KeyframeLen(RotField());
            int half = KeyframeLen(ContFields(4, BasisQuantSpec.Half));
            int raw = KeyframeLen(ContFields(4, BasisQuantSpec.Raw));

            Assert.Less(smallest, half, $"smallest-three ({smallest} B) should beat 4x half ({half} B)");
            Assert.Less(half, raw, $"4x half ({half} B) should beat 4x raw ({raw} B)");
        }

        [Test]
        public void UniformScale_IsSmallerThanPerAxis()
        {
            int uniform = KeyframeLen(ContFields(1, BasisQuantSpec.Raw));
            int perAxis = KeyframeLen(ContFields(3, BasisQuantSpec.Raw));
            Assert.Less(uniform, perAxis, $"uniform scale ({uniform} B) should beat per-axis xyz ({perAxis} B)");
        }

        [Test]
        public void Velocity_HalfRoundTripsWithinTolerance()
        {
            foreach (Vector3 v in new[] { new Vector3(0f, 0f, 0f), new Vector3(3.5f, -2f, 10f), new Vector3(-50f, 100f, 0.25f) })
            {
                (BasisSyncSchema s, BasisSyncValues vals) = ContFields(3, BasisQuantSpec.Half);
                vals.Cont[0] = v.x;
                vals.Cont[1] = v.y;
                vals.Cont[2] = v.z;
                BasisSyncValues dst = BasisSyncTestSupport.RoundTripKeyframe(s, vals);
                Vector3 got = new Vector3(dst.Cont[0], dst.Cont[1], dst.Cont[2]);
                Assert.LessOrEqual(Vector3.Distance(v, got), v.magnitude * 0.01f + 0.05f, $"velocity {v}");
            }
        }

        [Test]
        public void InterpolateFlag_PropagatesToSchema()
        {
            var s = new BasisSyncSchema();
            int snapFloat = s.AddField(BasisSyncFieldType.Float, false, new[] { BasisQuantSpec.Raw });
            int lerpFloat = s.AddField(BasisSyncFieldType.Float, true, new[] { BasisQuantSpec.Raw });
            int snapRot = s.AddRotation(false, 9);
            int lerpRot = s.AddRotation(true, 9);
            s.Lock();

            Assert.IsFalse(s.GetField(snapFloat).Interpolate, "snap float keeps Interpolate off");
            Assert.IsTrue(s.GetField(lerpFloat).Interpolate, "lerp float");
            Assert.IsFalse(s.GetField(snapRot).Interpolate, "snap rotation keeps Interpolate off");
            Assert.IsTrue(s.GetField(lerpRot).Interpolate, "lerp rotation");
        }

        // ── helpers ──

        static BasisTransformAxisCompression Make(BasisTransformAxisMode mode) =>
            new BasisTransformAxisCompression { Mode = mode, Min = 0f, Max = 1f, Bits = 16 };

        static (BasisSyncSchema, BasisSyncValues) ContFields(int n, BasisQuantSpec spec)
        {
            var s = new BasisSyncSchema();
            for (int i = 0; i < n; i++) s.AddField(BasisSyncFieldType.Float, true, new[] { spec });
            s.Lock();
            var v = new BasisSyncValues();
            v.Allocate(s);
            return (s, v);
        }

        static (BasisSyncSchema, BasisSyncValues) RotField()
        {
            var s = new BasisSyncSchema();
            s.AddField(BasisSyncFieldType.Rotation);
            s.Lock();
            var v = new BasisSyncValues();
            v.Allocate(s);
            return (s, v);
        }

        static Quaternion RoundTripQuatFloats(BasisQuantSpec spec, Quaternion q)
        {
            (BasisSyncSchema s, BasisSyncValues v) = ContFields(4, spec);
            v.Cont[0] = q.x;
            v.Cont[1] = q.y;
            v.Cont[2] = q.z;
            v.Cont[3] = q.w;
            BasisSyncValues dst = BasisSyncTestSupport.RoundTripKeyframe(s, v);
            return Normalize(dst.Cont[0], dst.Cont[1], dst.Cont[2], dst.Cont[3]);
        }

        static Quaternion Normalize(float x, float y, float z, float w)
        {
            float mag = Mathf.Sqrt(x * x + y * y + z * z + w * w);
            if (mag < 1e-6f) return Quaternion.identity;
            float inv = 1f / mag;
            return new Quaternion(x * inv, y * inv, z * inv, w * inv);
        }

        static int KeyframeLen((BasisSyncSchema s, BasisSyncValues v) pair)
        {
            BasisSyncTestSupport.SerializeKeyframe(pair.s, pair.v, 1, out int len);
            return len;
        }
    }
}
