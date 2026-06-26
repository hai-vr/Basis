using System;
using Basis.Scripts.Networking.Sync;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using static Basis.Tests.Sync.BasisSyncTestSupport;

namespace Basis.Tests.Sync
{
    /// <summary>
    /// Contract tests for the schema layout and the wire codec: field-pool offset assignment, the
    /// lock / field-count limits, lossless round-trips per field type, quantization precision and
    /// clamping, varint boundaries, and the guarantee that no packet ever exceeds MaxSerializedSize
    /// (the buffer the sender allocates).
    /// </summary>
    public class BasisSyncCodecTests
    {
        // ── Schema ──

        [Test]
        public void Schema_RejectsAddField_AfterLock()
        {
            var s = new BasisSyncSchema();
            s.AddField(BasisSyncFieldType.Position);
            s.Lock();
            Assert.Throws<InvalidOperationException>(() => s.AddField(BasisSyncFieldType.Float));
        }

        [Test]
        public void Schema_AssignsPerPoolOffsetsAndCounts()
        {
            var s = new BasisSyncSchema();
            s.AddField(BasisSyncFieldType.Position); // cont 0..2
            s.AddField(BasisSyncFieldType.Float);    // cont 3
            s.AddField(BasisSyncFieldType.Rotation); // rot 0
            s.AddField(BasisSyncFieldType.Int);      // disc 0
            s.AddField(BasisSyncFieldType.Scale);    // cont 4..6
            s.AddField(BasisSyncFieldType.UShort);   // disc 1
            s.Lock();

            Assert.AreEqual(7, s.ContCount);
            Assert.AreEqual(1, s.RotCount);
            Assert.AreEqual(2, s.DiscCount);
            Assert.AreEqual(6, s.FieldCount);
            Assert.AreEqual(1, s.DirtyMaskBytes);

            Assert.AreEqual(0, s.GetField(0).Offset);
            Assert.AreEqual(BasisSyncPool.Continuous, s.GetField(0).Pool);
            Assert.AreEqual(3, s.GetField(0).ContComponents);
            Assert.AreEqual(3, s.GetField(1).Offset);
            Assert.AreEqual(0, s.GetField(2).Offset);             // rotation pool
            Assert.AreEqual(BasisSyncPool.Rotation, s.GetField(2).Pool);
            Assert.AreEqual(0, s.GetField(3).Offset);             // discrete pool
            Assert.AreEqual(4, s.GetField(4).Offset);             // scale after position+float
            Assert.AreEqual(1, s.GetField(5).Offset);             // second discrete
        }

        [Test]
        public void Schema_DirtyMaskBytes_ScaleWithFieldCount()
        {
            var s = new BasisSyncSchema();
            for (int i = 0; i < 8; i++) s.AddField(BasisSyncFieldType.Byte);
            Assert.AreEqual(1, s.DirtyMaskBytes);
            s.AddField(BasisSyncFieldType.Byte);
            Assert.AreEqual(2, s.DirtyMaskBytes);
        }

        [Test]
        public void Schema_RejectsBeyond255Fields()
        {
            var s = new BasisSyncSchema();
            for (int i = 0; i < 255; i++) s.AddField(BasisSyncFieldType.Byte);
            Assert.Throws<InvalidOperationException>(() => s.AddField(BasisSyncFieldType.Byte));
        }

        // ── Round-trips ──

        [Test]
        public void Codec_RoundTrips_Continuous([Values(false, true)] bool quant)
        {
            CheckCont(BasisSyncFieldType.Position, quant, new[] { 12.5f, -3.25f, 7f }, quant ? 0.06f : 1e-4f);
            CheckCont(BasisSyncFieldType.Scale, quant, new[] { 1.5f, 2f, 0.5f }, quant ? 0.01f : 1e-4f);
            CheckCont(BasisSyncFieldType.Float, quant, new[] { 3.14159f }, quant ? 0.02f : 1e-4f);
            CheckCont(BasisSyncFieldType.Angle, quant, new[] { 47.5f }, quant ? 0.06f : 1e-4f);
            CheckCont(BasisSyncFieldType.Vector2, quant, new[] { 1.5f, -2.5f }, quant ? 0.02f : 1e-4f);
            CheckCont(BasisSyncFieldType.Vector4, quant, new[] { 1f, -2f, 3f, -4f }, quant ? 0.03f : 1e-4f);
            CheckCont(BasisSyncFieldType.Color, quant, new[] { 0.2f, 0.4f, 0.6f, 0.8f }, quant ? 0.012f : 1e-4f);
        }

        [Test]
        public void Codec_RoundTrips_Discrete()
        {
            CheckDisc(BasisSyncFieldType.Int, -98765);
            CheckDisc(BasisSyncFieldType.UShort, 54321);
            CheckDisc(BasisSyncFieldType.Bool, 1);
            CheckDisc(BasisSyncFieldType.Bool, 0);
            CheckDisc(BasisSyncFieldType.Byte, 200);
            CheckDisc(BasisSyncFieldType.UInt, 12345678);
        }

        [Test]
        public void Codec_RoundTrips_Rotation_AcrossOrientations()
        {
            foreach (Vector3 e in new[] { Vector3.zero, new Vector3(90, 0, 0), new Vector3(0, 90, 0), new Vector3(0, 0, 90), new Vector3(33, 77, 124), new Vector3(-45, 200, -170) })
            {
                var (schema, src) = Single(BasisSyncFieldType.Rotation);
                Quaternion q = Quaternion.Euler(e);
                src.Rot[0] = new quaternion(q.x, q.y, q.z, q.w);
                BasisSyncValues dst = RoundTripKeyframe(schema, src);
                float deg = QuatAngle(src.Rot[0], dst.Rot[0]);
                Assert.LessOrEqual(deg, 1.0f, $"rotation {e} round-trip off by {deg} deg");
            }
        }

        // ── Quantization ──

        [Test]
        public void Codec_Quantize_ClampsOutOfRange()
        {
            // Position quantizes over +/-1024.
            CheckQuantClamp(BasisSyncFieldType.Position, 5000f, 1024f, 0.1f);
            CheckQuantClamp(BasisSyncFieldType.Position, -5000f, -1024f, 0.1f);
            // Scale quantizes over [0,64].
            CheckQuantClamp(BasisSyncFieldType.Scale, 100f, 64f, 0.1f);
            CheckQuantClamp(BasisSyncFieldType.Scale, -5f, 0f, 0.1f);
            // Color channels quantize over [0,1].
            CheckQuantClamp(BasisSyncFieldType.Color, 2f, 1f, 0.01f);
            CheckQuantClamp(BasisSyncFieldType.Color, -1f, 0f, 0.01f);
        }

        // ── Varint ──

        [Test]
        public void Codec_VarInt_RoundTripsBoundaryValues()
        {
            foreach (int v in new[] { 0, 1, -1, 63, 64, -64, 8191, 8192, 1000000, -1000000, int.MaxValue, int.MinValue })
                CheckDisc(BasisSyncFieldType.Int, v);
        }

        // ── Delta ──

        [Test]
        public void Codec_Delta_PreservesUntouchedFields()
        {
            var schema = new BasisSyncSchema();
            int posIdx = schema.AddField(BasisSyncFieldType.Position);
            int floatIdx = schema.AddField(BasisSyncFieldType.Float);
            int intIdx = schema.AddField(BasisSyncFieldType.Int);
            schema.Lock();

            var keyframe = Values(schema);
            BasisSyncField pos = schema.GetField(posIdx), fl = schema.GetField(floatIdx), it = schema.GetField(intIdx);
            keyframe.Cont[pos.Offset] = 1f; keyframe.Cont[pos.Offset + 1] = 2f; keyframe.Cont[pos.Offset + 2] = 3f;
            keyframe.Cont[fl.Offset] = 9f;
            keyframe.Disc[it.Offset] = 42;

            var baseline = Values(schema);
            byte[] kfBuf = SerializeKeyframe(schema, keyframe, 1, out int kfLen);
            Assert.IsTrue(BasisSyncCodec.Deserialize(schema, kfBuf, kfLen, baseline, out _, out _, out _));

            var changed = Values(schema);
            changed.CopyFrom(keyframe);
            changed.Cont[fl.Offset] = -5.5f;
            byte[] dBuf = SerializeDelta(schema, changed, 2, new[] { floatIdx }, out int dLen);

            Assert.IsTrue(BasisSyncCodec.Deserialize(schema, dBuf, dLen, baseline, out _, out bool kf, out _));
            Assert.IsFalse(kf, "delta should not be flagged keyframe");
            Assert.AreEqual(-5.5f, baseline.Cont[fl.Offset], 1e-4f, "float should update");
            Assert.AreEqual(1f, baseline.Cont[pos.Offset], 1e-4f, "position preserved from baseline");
            Assert.AreEqual(3f, baseline.Cont[pos.Offset + 2], 1e-4f);
            Assert.AreEqual(42, baseline.Disc[it.Offset], "int preserved from baseline");
        }

        // ── Buffer safety ──

        [Test]
        public void Codec_NeverExceedsMaxSerializedSize_AtExtremeValues()
        {
            var schema = AllTypesSchema();
            int max = BasisSyncCodec.MaxSerializedSize(schema);

            var v = Values(schema);
            for (int i = 0; i < schema.ContCount; i++) v.Cont[i] = (i % 2 == 0) ? 1e9f : -1e9f;
            for (int i = 0; i < schema.RotCount; i++) v.Rot[i] = new quaternion(0.5f, 0.5f, 0.5f, 0.5f);
            for (int i = 0; i < schema.DiscCount; i++) v.Disc[i] = (i % 2 == 0) ? int.MaxValue : int.MinValue;

            byte[] kf = SerializeKeyframe(schema, v, 1, out int kfLen);
            Assert.LessOrEqual(kfLen, max, "keyframe exceeded MaxSerializedSize");
            Assert.LessOrEqual(kfLen, kf.Length, "keyframe wrote past the scratch buffer");

            int[] allDirty = new int[schema.FieldCount];
            for (int i = 0; i < allDirty.Length; i++) allDirty[i] = i;
            SerializeDelta(schema, v, 2, allDirty, out int dLen);
            Assert.LessOrEqual(dLen, max, "all-fields delta exceeded MaxSerializedSize");
        }

        [Test]
        public void Codec_EmptySchema_IsHeaderOnly()
        {
            var s = new BasisSyncSchema();
            s.Lock();
            byte[] buf = SerializeKeyframe(s, Values(s), 1, out int len); // no checksum
            Assert.AreEqual(BasisSyncCodec.HeaderSize, len, "empty keyframe is header-only");
            Assert.GreaterOrEqual(BasisSyncCodec.MaxSerializedSize(s), len, "MaxSerializedSize bounds the real packet");
            Assert.IsTrue(BasisSyncCodec.Deserialize(s, buf, len, Values(s), out _, out bool kf, out _));
            Assert.IsTrue(kf);
        }

        [Test]
        public void Codec_Checksum_DetectsCorruption()
        {
            var (s, v) = Single(BasisSyncFieldType.Position);
            v.Cont[0] = 1.5f; v.Cont[1] = -2f; v.Cont[2] = 3f;
            var mask = new byte[Math.Max(1, s.DirtyMaskBytes)];
            var buf = new byte[BasisSyncCodec.MaxSerializedSize(s)];
            int len = BasisSyncCodec.Serialize(s, v, true, mask, 1, 50, buf, appendChecksum: true);

            Assert.IsTrue(BasisSyncCodec.VerifyChecksum(buf, len), "a clean packet must verify");

            buf[BasisSyncCodec.HeaderSize] ^= 0x01;         // corrupt a value bit
            Assert.IsFalse(BasisSyncCodec.VerifyChecksum(buf, len), "a corrupted body must fail verification");

            buf[BasisSyncCodec.HeaderSize] ^= 0x01;         // restore body
            buf[len - 1] ^= 0x80;                           // corrupt the trailer itself
            Assert.IsFalse(BasisSyncCodec.VerifyChecksum(buf, len), "a corrupted trailer must fail verification");
        }

        [Test]
        public void Codec_Deserialize_RejectsTruncatedPacket()
        {
            var (s, v) = Single(BasisSyncFieldType.Position);
            byte[] buf = SerializeKeyframe(s, v, 1, out _);
            Assert.IsFalse(BasisSyncCodec.Deserialize(s, buf, BasisSyncCodec.HeaderSize - 1, Values(s), out _, out _, out _));
            Assert.IsFalse(BasisSyncCodec.Deserialize(s, null, 0, Values(s), out _, out _, out _));
        }

        // ── helpers ──

        static void CheckCont(BasisSyncFieldType type, bool quant, float[] components, float tol)
        {
            var (schema, src) = Single(type, quant);
            BasisSyncField f = schema.GetField(0);
            for (int c = 0; c < components.Length; c++) src.Cont[f.Offset + c] = components[c];

            BasisSyncValues dst = RoundTripKeyframe(schema, src);
            for (int c = 0; c < components.Length; c++)
            {
                float err = type == BasisSyncFieldType.Angle
                    ? Mathf.Abs(Mathf.DeltaAngle(dst.Cont[f.Offset + c], components[c]))
                    : Mathf.Abs(dst.Cont[f.Offset + c] - components[c]);
                Assert.LessOrEqual(err, tol, $"{type} quant={quant} comp {c}: got {dst.Cont[f.Offset + c]} expected {components[c]}");
            }
        }

        static void CheckDisc(BasisSyncFieldType type, int value)
        {
            var (schema, src) = Single(type);
            src.Disc[0] = value;
            BasisSyncValues dst = RoundTripKeyframe(schema, src);
            Assert.AreEqual(value, dst.Disc[0], $"{type} discrete round-trip of {value}");
        }

        static void CheckQuantClamp(BasisSyncFieldType type, float input, float expected, float tol)
        {
            var (schema, src) = Single(type, quant: true);
            BasisSyncField f = schema.GetField(0);
            for (int c = 0; c < f.ContComponents; c++) src.Cont[f.Offset + c] = input;
            BasisSyncValues dst = RoundTripKeyframe(schema, src);
            Assert.AreEqual(expected, dst.Cont[f.Offset], tol, $"{type} did not clamp {input} to {expected}");
        }

        static BasisSyncSchema AllTypesSchema()
        {
            var s = new BasisSyncSchema();
            foreach (BasisSyncFieldType t in Enum.GetValues(typeof(BasisSyncFieldType)))
                s.AddField(t, true, false);
            s.Lock();
            return s;
        }
    }
}
