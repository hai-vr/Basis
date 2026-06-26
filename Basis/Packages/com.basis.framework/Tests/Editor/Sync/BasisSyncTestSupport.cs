using System;
using Basis.Scripts.Networking.Sync;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Tests.Sync
{
    /// <summary>Shared builders for the sync codec / receiver / sim tests.</summary>
    static class BasisSyncTestSupport
    {
        public const float Dt = 1f / 72f;

        public static BasisSyncSchema Schema(params BasisSyncFieldType[] types)
        {
            var s = new BasisSyncSchema();
            foreach (BasisSyncFieldType t in types) s.AddField(t, true, false);
            s.Lock();
            return s;
        }

        public static (BasisSyncSchema schema, BasisSyncValues values) Single(BasisSyncFieldType t, bool quant = false, bool interp = true)
        {
            var s = new BasisSyncSchema();
            s.AddField(t, interp, quant);
            s.Lock();
            return (s, Values(s));
        }

        public static BasisSyncValues Values(BasisSyncSchema s)
        {
            var v = new BasisSyncValues();
            v.Allocate(s);
            return v;
        }

        public static byte[] SerializeKeyframe(BasisSyncSchema s, BasisSyncValues v, byte seq, out int len, ushort ms = 50, bool checksum = false)
        {
            var mask = new byte[Math.Max(1, s.DirtyMaskBytes)];
            var buf = new byte[BasisSyncCodec.MaxSerializedSize(s)];
            len = BasisSyncCodec.Serialize(s, v, true, mask, seq, ms, buf, checksum);
            return buf;
        }

        public static byte[] SerializeDelta(BasisSyncSchema s, BasisSyncValues v, byte seq, int[] dirty, out int len, ushort ms = 50, bool checksum = false)
        {
            var mask = new byte[Math.Max(1, s.DirtyMaskBytes)];
            foreach (int fi in dirty) mask[fi >> 3] |= (byte)(1 << (fi & 7));
            var buf = new byte[BasisSyncCodec.MaxSerializedSize(s)];
            len = BasisSyncCodec.Serialize(s, v, false, mask, seq, ms, buf, checksum);
            return buf;
        }

        public static BasisSyncValues RoundTripKeyframe(BasisSyncSchema s, BasisSyncValues src)
        {
            byte[] buf = SerializeKeyframe(s, src, 1, out int len);
            BasisSyncValues dst = Values(s);
            Assert.IsTrue(BasisSyncCodec.Deserialize(s, buf, len, dst, out _, out bool kf, out _), "deserialize failed");
            Assert.IsTrue(kf, "keyframe flag missing");
            return dst;
        }

        public static void FeedKeyframe(BasisSyncReceiver r, BasisSyncSchema s, BasisSyncValues v, byte seq, ushort ms = 50, bool checksum = false)
        {
            byte[] buf = SerializeKeyframe(s, v, seq, out int len, ms, checksum);
            r.OnPacket(buf, len);
        }

        public static void FeedDelta(BasisSyncReceiver r, BasisSyncSchema s, BasisSyncValues v, byte seq, int[] dirty, ushort ms = 50, bool checksum = false)
        {
            byte[] buf = SerializeDelta(s, v, seq, dirty, out int len, ms, checksum);
            r.OnPacket(buf, len);
        }

        public static void Advance(BasisSyncReceiver r, int steps, float dt = Dt)
        {
            for (int i = 0; i < steps; i++) r.Advance(dt);
        }

        public static quaternion Quat(float xDeg, float yDeg, float zDeg)
        {
            Quaternion q = Quaternion.Euler(xDeg, yDeg, zDeg);
            return new quaternion(q.x, q.y, q.z, q.w);
        }

        public static Quaternion ToUnity(quaternion q) => new Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);
        public static float QuatAngle(quaternion a, quaternion b) => Quaternion.Angle(ToUnity(a), ToUnity(b));
    }
}
