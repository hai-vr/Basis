using System;
using Basis.Scripts.Networking.Sync;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Coverage for the bit-packed codec and per-component quantization: Raw is bit-exact, Half and
    /// Ranged stay within their resolution, Ranged clips out of range, bools cost one bit, mixed-mode
    /// fields survive keyframe + delta, smaller bit widths produce smaller packets, and decoding a
    /// truncated/corrupt buffer never throws.
    /// </summary>
    public class BasisSyncQuantTests
    {
        [Test]
        public void Raw_Float_IsBitExact()
        {
            (BasisSyncSchema s, BasisSyncValues v) = SingleCont(BasisSyncFieldType.Float, BasisQuantSpec.Raw);
            int off = s.GetField(0).Offset;
            foreach (float val in new[] { 0f, 1.23456f, -9876.5432f, 3.14159265f, 1e9f })
            {
                v.Cont[off] = val;
                BasisSyncValues dst = RoundTrip(s, v, out _);
                Assert.AreEqual(val, dst.Cont[off], 0f, $"raw float {val} must be bit-exact");
            }
        }

        [Test]
        public void Ranged_StaysWithinHalfStep([Values(4, 6, 8, 10, 12, 16)] int bits)
        {
            const float min = -5f, max = 5f;
            (BasisSyncSchema s, BasisSyncValues v) = SingleCont(BasisSyncFieldType.Float, BasisQuantSpec.Ranged(min, max, bits));
            int off = s.GetField(0).Offset;
            float step = (max - min) / ((1u << bits) - 1u);

            foreach (float val in new[] { -5f, -2.3f, 0f, 1.7f, 4.99f, 5f })
            {
                v.Cont[off] = val;
                BasisSyncValues dst = RoundTrip(s, v, out _);
                float err = Mathf.Abs(dst.Cont[off] - val);
                Assert.LessOrEqual(err, step * 0.5f + 1e-4f, $"bits={bits} val={val} err={err} step={step}");
            }
        }

        [Test]
        public void Ranged_ClampsOutOfRange()
        {
            (BasisSyncSchema s, BasisSyncValues v) = SingleCont(BasisSyncFieldType.Float, BasisQuantSpec.Ranged(0f, 1f, 8));
            int off = s.GetField(0).Offset;

            v.Cont[off] = 5f;
            Assert.AreEqual(1f, RoundTrip(s, v, out _).Cont[off], 0.01f, "above max clamps to max");

            v.Cont[off] = -3f;
            Assert.AreEqual(0f, RoundTrip(s, v, out _).Cont[off], 0.01f, "below min clamps to min");
        }

        [Test]
        public void Half_RoundTripsWithinHalfFloatPrecision()
        {
            (BasisSyncSchema s, BasisSyncValues v) = SingleCont(BasisSyncFieldType.Float, BasisQuantSpec.Half);
            int off = s.GetField(0).Offset;
            foreach (float val in new[] { 0f, 1f, -2.5f, 123.4f })
            {
                v.Cont[off] = val;
                float got = RoundTrip(s, v, out _).Cont[off];
                Assert.LessOrEqual(Mathf.Abs(got - val), Mathf.Abs(val) * 0.01f + 1e-3f, $"half {val} -> {got}");
            }
        }

        [Test]
        public void Bools_PackOneBitEach()
        {
            var s = new BasisSyncSchema();
            for (int i = 0; i < 10; i++) s.AddField(BasisSyncFieldType.Bool);
            s.Lock();
            var v = new BasisSyncValues(); v.Allocate(s);
            for (int i = 0; i < 10; i++) v.Disc[i] = i % 2;

            BasisSyncValues dst = RoundTrip(s, v, out int len);
            for (int i = 0; i < 10; i++) Assert.AreEqual(i % 2, dst.Disc[i], $"bool {i}");

            // Keyframe = header(4) + ceil(10 bits / 8) = 4 + 2 bytes.
            Assert.AreEqual(BasisSyncCodec.HeaderSize + 2, len, "10 bools should pack into 2 payload bytes");
        }

        [Test]
        public void RangedVector_IsSmallerThanRaw()
        {
            int lenRaw = Vec3Len(BasisQuantSpec.Raw);
            int lenComp = Vec3Len(BasisQuantSpec.Ranged(-10f, 10f, 10));
            Assert.Less(lenComp, lenRaw, $"30-bit ranged vec3 ({lenComp}) must beat 96-bit raw ({lenRaw})");
        }

        [Test]
        public void MixedModes_SurviveKeyframeAndDelta()
        {
            var s = new BasisSyncSchema();
            int v3 = s.AddField(BasisSyncFieldType.Position, true, new[]
            {
                BasisQuantSpec.Raw, BasisQuantSpec.Half, BasisQuantSpec.Ranged(-10f, 10f, 12),
            });
            int boolIdx = s.AddField(BasisSyncFieldType.Bool);
            int intIdx = s.AddField(BasisSyncFieldType.Int);
            s.Lock();

            int pos = s.GetField(v3).Offset;
            int bo = s.GetField(boolIdx).Offset;
            int io = s.GetField(intIdx).Offset;

            var src = new BasisSyncValues(); src.Allocate(s);
            src.Cont[pos] = 123.5f; src.Cont[pos + 1] = 0.25f; src.Cont[pos + 2] = -7.5f;
            src.Disc[bo] = 1; src.Disc[io] = -4242;

            var baseline = new BasisSyncValues(); baseline.Allocate(s);
            Keyframe(s, src, baseline);

            Assert.AreEqual(123.5f, baseline.Cont[pos], 0f, "raw axis exact");
            Assert.AreEqual(0.25f, baseline.Cont[pos + 1], 0.002f, "half axis");
            Assert.AreEqual(-7.5f, baseline.Cont[pos + 2], 0.01f, "ranged axis");
            Assert.AreEqual(1, baseline.Disc[bo]);
            Assert.AreEqual(-4242, baseline.Disc[io]);

            // Delta that only changes the int — everything else must survive from the baseline.
            var changed = new BasisSyncValues(); changed.Allocate(s); changed.CopyFrom(src);
            changed.Disc[io] = 999;
            var mask = new byte[Math.Max(1, s.DirtyMaskBytes)];
            mask[intIdx >> 3] |= (byte)(1 << (intIdx & 7));
            var buf = new byte[BasisSyncCodec.MaxSerializedSize(s)];
            int len = BasisSyncCodec.Serialize(s, changed, false, mask, 2, 50, buf);
            Assert.IsTrue(BasisSyncCodec.Deserialize(s, buf, len, baseline, out _, out bool kf, out _));

            Assert.IsFalse(kf);
            Assert.AreEqual(999, baseline.Disc[io], "int updated by delta");
            Assert.AreEqual(123.5f, baseline.Cont[pos], 0f, "raw axis preserved across delta");
            Assert.AreEqual(-7.5f, baseline.Cont[pos + 2], 0.01f, "ranged axis preserved across delta");
        }

        [Test]
        public void Decode_Truncated_NeverThrows()
        {
            var s = new BasisSyncSchema();
            s.AddField(BasisSyncFieldType.Position, true, new[] { BasisQuantSpec.Ranged(-1f, 1f, 9), BasisQuantSpec.Half, BasisQuantSpec.Raw });
            s.AddField(BasisSyncFieldType.Rotation);
            s.AddField(BasisSyncFieldType.Int);
            s.AddField(BasisSyncFieldType.Bool);
            s.Lock();

            var v = new BasisSyncValues(); v.Allocate(s);
            var mask = new byte[Math.Max(1, s.DirtyMaskBytes)];
            var buf = new byte[BasisSyncCodec.MaxSerializedSize(s)];
            int len = BasisSyncCodec.Serialize(s, v, true, mask, 1, 50, buf);

            var dst = new BasisSyncValues(); dst.Allocate(s);
            for (int cut = 0; cut <= len; cut++)
            {
                int c = cut;
                Assert.DoesNotThrow(() => BasisSyncCodec.Deserialize(s, buf, c, dst, out _, out _, out _), $"truncated to {c} bytes threw");
            }
        }

        // ── helpers ──

        static (BasisSyncSchema, BasisSyncValues) SingleCont(BasisSyncFieldType type, BasisQuantSpec spec)
        {
            var s = new BasisSyncSchema();
            s.AddField(type, true, new[] { spec });
            s.Lock();
            var v = new BasisSyncValues(); v.Allocate(s);
            return (s, v);
        }

        static int Vec3Len(BasisQuantSpec spec)
        {
            var s = new BasisSyncSchema();
            s.AddField(BasisSyncFieldType.Position, true, new[] { spec, spec, spec });
            s.Lock();
            var v = new BasisSyncValues(); v.Allocate(s);
            int off = s.GetField(0).Offset;
            v.Cont[off] = 1f; v.Cont[off + 1] = 2f; v.Cont[off + 2] = 3f;
            RoundTrip(s, v, out int len);
            return len;
        }

        static BasisSyncValues RoundTrip(BasisSyncSchema schema, BasisSyncValues src, out int len)
        {
            var mask = new byte[Math.Max(1, schema.DirtyMaskBytes)];
            var buf = new byte[BasisSyncCodec.MaxSerializedSize(schema)];
            len = BasisSyncCodec.Serialize(schema, src, true, mask, 1, 50, buf);
            Assert.LessOrEqual(len, buf.Length, "serialized length exceeded MaxSerializedSize");
            var dst = new BasisSyncValues(); dst.Allocate(schema);
            Assert.IsTrue(BasisSyncCodec.Deserialize(schema, buf, len, dst, out _, out bool kf, out _), "deserialize failed");
            Assert.IsTrue(kf, "keyframe flag missing");
            return dst;
        }

        static void Keyframe(BasisSyncSchema schema, BasisSyncValues src, BasisSyncValues baseline)
        {
            var mask = new byte[Math.Max(1, schema.DirtyMaskBytes)];
            var buf = new byte[BasisSyncCodec.MaxSerializedSize(schema)];
            int len = BasisSyncCodec.Serialize(schema, src, true, mask, 1, 50, buf);
            Assert.IsTrue(BasisSyncCodec.Deserialize(schema, buf, len, baseline, out _, out _, out _));
        }
    }
}
