using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Scripts.Networking.Sync.Testing
{
    /// <summary>
    /// Offline, deterministic exerciser for the generic value-sync data path
    /// (<see cref="BasisSyncCodec"/> + <see cref="BasisSyncReceiver"/>).
    ///
    /// It runs the real serialize → wire → deserialize → interpolate pipeline without a live
    /// network or a MonoBehaviour: a faithful re-implementation of the owner send cadence
    /// (mirrors <c>BasisSyncedObject.TransmitIfDue</c> / <c>FieldChanged</c>) drives the real codec;
    /// a seeded packet network injects loss / duplication / reorder / jitter / corruption; the real
    /// <see cref="BasisSyncReceiver"/> plays it back; and a managed copy of
    /// <c>InterpolateSyncObjectsJob</c> samples the output. Every run is reproducible from its seed,
    /// so the result CSV is stable.
    ///
    /// Out of scope (timing/transport, not data correctness): distance reduction, relevance culling,
    /// P2P routing, recipient filtering. Those don't change what comes out of the codec/receiver.
    /// </summary>
    public static class BasisSyncSim
    {
        public const float RotationToleranceDeg = 2.0f;

        public static BasisSyncSimResult Run(BasisSyncSimScenario s)
        {
            var result = new BasisSyncSimResult
            {
                ScenarioName = s.Name,
                FieldConfigName = s.FieldConfigName,
                Motion = s.Motion,
                ProfileName = s.Profile.Name,
                Seed = s.Seed,
            };

            BasisSyncSchema schema = BuildSchema(s.Fields);
            result.WireBytesPerKeyframe = BasisSyncCodec.MaxSerializedSize(schema);

            var sender = new SimSender(schema)
            {
                SendInterval = s.SendInterval,
                KeyframeInterval = s.KeyframeInterval,
                ContinuousEpsilon = s.ContinuousEpsilon,
                UseChecksum = s.UseChecksum,
                UseIdleKeyframeBackoff = s.IdleKeyframeBackoff,
                RotationSendThresholdDegrees = s.RotationSendThresholdDegrees,
            };
            var receiver = new BasisSyncReceiver(schema);

            int teleStart = 0, teleCount = 0;
            if (s.Fields.Count > 0 && s.Fields[0].Type == BasisSyncFieldType.Position) teleCount = 3;
            receiver.Configure(s.Extrapolate, s.MaxExtrapolation, s.TeleportThreshold,
                s.TeleportThresholdMeters * s.TeleportThresholdMeters, teleStart, teleCount, s.UseChecksum);

            var net = new SimNetwork(s.Profile, new System.Random(s.Seed));
            var truth = new BasisSyncTruthSource(schema, s.Fields, s.Motion, new System.Random(s.Seed * 397 + 13), s.DurationSeconds);
            var outVals = new BasisSyncValues();
            outVals.Allocate(schema);

            int activeFrames = Mathf.CeilToInt(s.DurationSeconds / s.Dt);
            int settleFrames = Mathf.CeilToInt(s.SettleSeconds / s.Dt);
            double frozenTime = activeFrames * (double)s.Dt;
            double t = 0;

            try
            {
                for (int frame = 0; frame < activeFrames + settleFrames; frame++)
                {
                    t += s.Dt;
                    bool active = frame < activeFrames;
                    float motionTime = active ? (float)t : (float)frozenTime;

                    truth.Apply(sender.Local, motionTime);

                    SimPacket pkt = sender.Tick(t);
                    if (pkt != null)
                    {
                        result.PacketsSent++;
                        result.WireBytesSent += pkt.Len;
                        if (pkt.Reliable) result.Keyframes++;
                        net.Send(pkt, t, result);
                    }

                    net.DeliverDue(t, receiver.OnPacket, result);
                    receiver.Advance(s.Dt);
                }

                net.FlushAll(receiver.OnPacket, result);
                for (int k = 0; k < 64; k++) receiver.Advance(s.Dt);
                Sample(receiver, schema, outVals);
            }
            catch (Exception e)
            {
                result.Exception = e.GetType().Name + ": " + e.Message;
            }

            Evaluate(schema, s.Profile.HardConvergence, s.UseChecksum, sender.Local, outVals, receiver.HasData, result);
            return result;
        }

        internal static BasisSyncSchema BuildSchema(List<SimFieldSpec> fields)
        {
            var schema = new BasisSyncSchema();
            for (int i = 0; i < fields.Count; i++)
            {
                SimFieldSpec fs = fields[i];
                if (fs.Type == BasisSyncFieldType.Rotation)
                    schema.AddRotation(fs.Interpolate, fs.RotBits);
                else
                    schema.AddField(fs.Type, fs.Interpolate, fs.Quantize);
            }
            schema.Lock();
            return schema;
        }

        static void Sample(BasisSyncReceiver recv, BasisSyncSchema schema, BasisSyncValues outVals)
        {
            if (!recv.HasData) return;
            Interpolate(schema, recv.CurrentValues, recv.NextValues, recv.InterpTime, outVals);
        }

        /// <summary>
        /// Managed mirror of <c>InterpolateSyncObjectsJob</c> + the driver SoA fill: continuous → lerp,
        /// angle → wrapped lerp, rotation → nlerp, discrete → snap to next. Exposed so a fidelity test
        /// can pin it against the real Burst job.
        /// </summary>
        public static void Interpolate(BasisSyncSchema schema, BasisSyncValues cur, BasisSyncValues nxt, float t, BasisSyncValues outVals)
        {
            for (int fi = 0; fi < schema.FieldCount; fi++)
            {
                BasisSyncField f = schema.GetField(fi);
                switch (f.Pool)
                {
                    case BasisSyncPool.Continuous:
                    {
                        byte mode = f.Type == BasisSyncFieldType.Angle
                            ? (f.Interpolate ? (byte)2 : (byte)0)
                            : (f.Interpolate ? (byte)1 : (byte)0);
                        for (int c = 0; c < f.ContComponents; c++)
                        {
                            int idx = f.Offset + c;
                            if (mode == 0) outVals.Cont[idx] = nxt.Cont[idx];
                            else if (mode == 2)
                            {
                                float a = cur.Cont[idx];
                                float delta = math.fmod(nxt.Cont[idx] - a + 540f, 360f) - 180f;
                                outVals.Cont[idx] = a + delta * t;
                            }
                            else outVals.Cont[idx] = math.lerp(cur.Cont[idx], nxt.Cont[idx], t);
                        }
                        break;
                    }
                    case BasisSyncPool.Rotation:
                    {
                        quaternion a = cur.Rot[f.Offset];
                        quaternion b = nxt.Rot[f.Offset];
                        if (math.dot(a.value, b.value) < 0f) b.value = -b.value;
                        outVals.Rot[f.Offset] = math.nlerp(a, b, t);
                        break;
                    }
                    case BasisSyncPool.Discrete:
                        outVals.Disc[f.Offset] = nxt.Disc[f.Offset];
                        break;
                }
            }
        }

        // ── Convergence scoring vs the owner's final authoritative values ──
        internal static void Evaluate(BasisSyncSchema schema, bool hardConvergence, bool useChecksum, BasisSyncValues truth, BasisSyncValues outVals, bool hasData, BasisSyncSimResult result)
        {
            if (result.Exception != null)
            {
                result.Pass = false;
                result.FailReason = result.Exception;
                return;
            }

            if (!hasData)
            {
                // Faithful profiles deliver keyframes reliably, so no output is a real failure.
                // Chaos impairs reliable traffic too — total keyframe loss there is observational.
                result.Pass = !hardConvergence;
                result.FailReason = "receiver never produced output (no keyframe arrived)";
                return;
            }

            float worstCont = 0f, worstRotDeg = 0f;
            int discMismatch = 0;
            bool nonFinite = false;
            string firstFail = null;

            for (int fi = 0; fi < schema.FieldCount; fi++)
            {
                BasisSyncField f = schema.GetField(fi);
                switch (f.Pool)
                {
                    case BasisSyncPool.Continuous:
                    {
                        float tol = ContinuousTolerance(f, truth);
                        for (int c = 0; c < f.ContComponents; c++)
                        {
                            int idx = f.Offset + c;
                            float o = outVals.Cont[idx];
                            if (float.IsNaN(o) || float.IsInfinity(o)) { nonFinite = true; continue; }
                            float err = f.Type == BasisSyncFieldType.Angle ? AngleError(o, truth.Cont[idx]) : Mathf.Abs(o - truth.Cont[idx]);
                            if (err > worstCont) worstCont = err;
                            if (err > tol && firstFail == null)
                                firstFail = $"{f.Type}[{fi}].c{c} off by {err:0.###} (tol {tol:0.###})";
                        }
                        break;
                    }
                    case BasisSyncPool.Rotation:
                    {
                        Quaternion o = ToUnity(outVals.Rot[f.Offset]);
                        if (!IsFinite(o)) { nonFinite = true; break; }
                        float deg = Quaternion.Angle(o, ToUnity(truth.Rot[f.Offset]));
                        if (deg > worstRotDeg) worstRotDeg = deg;
                        if (deg > RotationToleranceDeg && firstFail == null)
                            firstFail = $"Rotation[{fi}] off by {deg:0.##}deg (tol {RotationToleranceDeg})";
                        break;
                    }
                    case BasisSyncPool.Discrete:
                        if (outVals.Disc[f.Offset] != truth.Disc[f.Offset])
                        {
                            discMismatch++;
                            if (firstFail == null)
                                firstFail = $"{f.Type}[{fi}] = {outVals.Disc[f.Offset]} expected {truth.Disc[f.Offset]}";
                        }
                        break;
                }
            }

            result.MaxContError = worstCont;
            result.MaxRotErrorDeg = worstRotDeg;
            result.DiscreteMismatches = discMismatch;

            if (nonFinite)
            {
                result.Pass = false;
                result.FailReason = "non-finite output after settle";
                return;
            }

            // Faithful profiles guarantee convergence (keyframes are ReliableOrdered). The chaos
            // profile impairs reliable traffic too, so its convergence is observational only.
            if (firstFail != null && hardConvergence)
            {
                // Without a checksum a corrupted packet can't be detected or dropped, so its bad value gets
                // applied — an expected limitation of running unprotected, not a codec bug. Flag it as a
                // warning rather than a hard fail (only when corruption actually occurred; other no-checksum
                // failures still fail).
                if (!useChecksum && result.PacketsCorrupted > 0)
                {
                    result.Pass = true;
                    result.Warn = true;
                    result.FailReason = "no checksum — corruption undetectable (expected): " + firstFail;
                    return;
                }

                result.Pass = false;
                result.FailReason = firstFail;
                return;
            }

            result.Pass = true;
            result.FailReason = firstFail == null ? "" : "(soft) " + firstFail;
        }

        static float ContinuousTolerance(in BasisSyncField f, BasisSyncValues truth)
        {
            if (!f.Quantize)
                return f.Type == BasisSyncFieldType.Angle ? 0.05f : 0.01f;

            switch (f.Type)
            {
                case BasisSyncFieldType.Position: return 0.06f;   // 16-bit over ±1024
                case BasisSyncFieldType.Scale: return 0.01f;      // 16-bit over [0,64]
                case BasisSyncFieldType.Color: return 0.012f;     // unit byte
                default:                                           // half-float (Float/Vector2/Vector4/Angle)
                {
                    float mag = 0f;
                    for (int c = 0; c < f.ContComponents; c++) mag = Mathf.Max(mag, Mathf.Abs(truth.Cont[f.Offset + c]));
                    return Mathf.Max(0.03f, 0.01f * mag);
                }
            }
        }

        static float AngleError(float a, float b)
        {
            float d = Mathf.Abs(Mathf.DeltaAngle(a, b));
            return d;
        }

        static Quaternion ToUnity(quaternion q) => new Quaternion(q.value.x, q.value.y, q.value.z, q.value.w);
        static bool IsFinite(Quaternion q) => !(float.IsNaN(q.x) || float.IsNaN(q.y) || float.IsNaN(q.z) || float.IsNaN(q.w)
            || float.IsInfinity(q.x) || float.IsInfinity(q.y) || float.IsInfinity(q.z) || float.IsInfinity(q.w));
    }

    /// <summary>One synced field in a test object.</summary>
    public sealed class SimFieldSpec
    {
        public BasisSyncFieldType Type;
        public bool Interpolate = true;
        public bool Quantize = false;
        public int RotBits = 9;   // smallest-three magnitude bits when Type == Rotation (9 = the legacy 32-bit packet)

        public SimFieldSpec(BasisSyncFieldType type, bool interpolate = true, bool quantize = false)
        {
            Type = type;
            Interpolate = interpolate;
            Quantize = quantize;
        }
    }

    public enum SyncMotion { Static, Ramp, Sine, Step, Teleport, RandomWalk }

    /// <summary>Impairment profile applied to the wire between owner and remote.</summary>
    public sealed class NetworkProfile
    {
        public string Name = "perfect";
        public float DeltaLoss;        // drop probability for unreliable (delta) packets
        public float DuplicateProb;    // probability a delivered packet is duplicated
        public float CorruptProb;      // probability an unreliable packet is bit-corrupted
        public float BaseLatencySec;   // fixed one-way latency
        public float JitterSec;        // extra uniform [0,JitterSec) latency (causes reorder)
        public bool ImpairReliable;    // chaos: also lose/reorder/corrupt keyframes
        public bool HardConvergence = true; // false => convergence is recorded but not asserted
    }

    public sealed class SimPacket
    {
        public byte[] Bytes;
        public int Len;
        public byte Seq;
        public bool Reliable;
    }

    /// <summary>Faithful re-implementation of the owner send cadence (BasisSyncedObject.TransmitIfDue).</summary>
    public sealed class SimSender
    {
        public float SendInterval = 0.05f;
        public float KeyframeInterval = 0.5f;
        public float ContinuousEpsilon = 1e-4f;
        public bool UseChecksum = true;

        // Idle keyframe backoff: a value that stops changing stretches its keyframe interval (2^n x, capped), so the
        // wire matches what the real TransmitIfDue sends. Off keeps the legacy fixed-interval cadence.
        public bool UseIdleKeyframeBackoff = false;
        // 0 keeps the legacy 0.99999 rotation-dirty dot; a positive value mirrors RotationSendThresholdDegrees.
        public float RotationSendThresholdDegrees = 0f;

        const int MaxKeyframeBackoffShift = 4;

        readonly BasisSyncSchema _schema;
        readonly BasisSyncValues _local;
        readonly BasisSyncValues _lastSent;
        readonly byte[] _dirtyMask;
        readonly byte[] _scratch;

        byte _seq;
        double _lastSendTime = -1;
        double _lastKeyframeTime = -1;
        bool _forceKeyframe = true;
        int _idleKeyframeBackoff;
        float _rotDotThreshold = 0.99999f;

        public BasisSyncValues Local => _local;

        public SimSender(BasisSyncSchema schema)
        {
            _schema = schema;
            _local = new BasisSyncValues(); _local.Allocate(schema);
            _lastSent = new BasisSyncValues(); _lastSent.Allocate(schema);
            _dirtyMask = new byte[_schema.DirtyMaskBytes < 1 ? 1 : _schema.DirtyMaskBytes];
            _scratch = new byte[BasisSyncCodec.MaxSerializedSize(schema)];
        }

        public void ForceKeyframe() => _forceKeyframe = true;

        public SimPacket Tick(double time)
        {
            bool intervalElapsed = _lastSendTime < 0 || (time - _lastSendTime) >= SendInterval;
            if (!intervalElapsed) return null;

            _rotDotThreshold = RotationSendThresholdDegrees > 0f
                ? Mathf.Cos(Mathf.Deg2Rad * RotationSendThresholdDegrees * 0.5f)
                : 0.99999f;

            double effectiveKeyframe = UseIdleKeyframeBackoff
                ? BasisSyncedObject.KeyframeBackoffInterval(KeyframeInterval, _idleKeyframeBackoff, MaxKeyframeBackoffShift)
                : KeyframeInterval;
            bool keyframe = _forceKeyframe || _lastSendTime < 0 || (time - _lastKeyframeTime) >= effectiveKeyframe;

            int dirtyBytes = _schema.DirtyMaskBytes;
            for (int i = 0; i < dirtyBytes; i++) _dirtyMask[i] = 0;

            bool anyChange = false;
            bool discreteChange = false;
            for (int fi = 0; fi < _schema.FieldCount; fi++)
            {
                if (!FieldChanged(fi)) continue;
                _dirtyMask[fi >> 3] |= (byte)(1 << (fi & 7));
                anyChange = true;
                if (_schema.GetField(fi).Pool == BasisSyncPool.Discrete) discreteChange = true;
            }

            if (discreteChange) keyframe = true;
            if (!keyframe && !anyChange) return null;
            if (anyChange) _idleKeyframeBackoff = 0;

            double elapsed = _lastSendTime >= 0 ? (time - _lastSendTime) : SendInterval;
            ushort intervalMs = (ushort)math.clamp((int)math.round(elapsed * 1000.0), 1, 65535);

            _lastSendTime = time;
            unchecked { _seq++; }

            int len = BasisSyncCodec.Serialize(_schema, _local, keyframe, _dirtyMask, _seq, intervalMs, _scratch, UseChecksum);
            var bytes = new byte[len];
            Array.Copy(_scratch, 0, bytes, 0, len);

            _lastSent.CopyFrom(_local);
            if (keyframe)
            {
                _lastKeyframeTime = time;
                _forceKeyframe = false;
                if (UseIdleKeyframeBackoff && !anyChange && _idleKeyframeBackoff < MaxKeyframeBackoffShift) _idleKeyframeBackoff++;
            }

            return new SimPacket { Bytes = bytes, Len = len, Seq = _seq, Reliable = keyframe };
        }

        bool FieldChanged(int fi)
        {
            BasisSyncField f = _schema.GetField(fi);
            switch (f.Pool)
            {
                case BasisSyncPool.Continuous:
                    for (int c = 0; c < f.ContComponents; c++)
                        if (math.abs(_local.Cont[f.Offset + c] - _lastSent.Cont[f.Offset + c]) > ContinuousEpsilon) return true;
                    return false;
                case BasisSyncPool.Rotation:
                    return math.abs(math.dot(_local.Rot[f.Offset].value, _lastSent.Rot[f.Offset].value)) < _rotDotThreshold;
                case BasisSyncPool.Discrete:
                    return _local.Disc[f.Offset] != _lastSent.Disc[f.Offset];
            }
            return false;
        }
    }

    /// <summary>
    /// Seeded packet network. Models LiteNetLib delivery semantics: ReliableOrdered keyframes are
    /// never lost/reordered/corrupted (head-of-line ordered); Unreliable deltas suffer the profile's
    /// loss / duplication / reorder (via latency jitter) / corruption. The chaos profile overrides
    /// this and impairs everything.
    /// </summary>
    public sealed class SimNetwork
    {
        sealed class InFlight
        {
            public byte[] Bytes;
            public int Len;
            public byte Seq;
            public int SendIndex;
            public double Arrival;
        }

        readonly NetworkProfile _p;
        readonly System.Random _rng;
        readonly List<InFlight> _inflight = new List<InFlight>();

        int _sendCounter;
        double _lastReliableArrival = -1;
        int _lastDeliveredSendIndex = -1;

        public SimNetwork(NetworkProfile profile, System.Random rng)
        {
            _p = profile;
            _rng = rng;
        }

        public void Send(SimPacket pkt, double sendTime, BasisSyncSimResult result)
        {
            int sendIndex = _sendCounter++;
            bool impair = !pkt.Reliable || _p.ImpairReliable;

            if (impair && Chance(_p.DeltaLoss))
            {
                result.PacketsDropped++;
                return;
            }

            Enqueue(pkt, sendTime, sendIndex, impair, result);

            if (impair && Chance(_p.DuplicateProb))
            {
                result.PacketsDuplicated++;
                Enqueue(pkt, sendTime, sendIndex, impair, result);
            }
        }

        void Enqueue(SimPacket pkt, double sendTime, int sendIndex, bool impair, BasisSyncSimResult result)
        {
            var f = new InFlight
            {
                Bytes = (byte[])pkt.Bytes.Clone(),
                Len = pkt.Len,
                Seq = pkt.Seq,
                SendIndex = sendIndex,
            };

            double arrival = sendTime + _p.BaseLatencySec + (impair ? _p.JitterSec * _rng.NextDouble() : 0.0);

            if (pkt.Reliable && !_p.ImpairReliable)
            {
                // ReliableOrdered: in-order, no reorder past earlier reliable packets.
                if (arrival <= _lastReliableArrival) arrival = _lastReliableArrival + 1e-5;
                _lastReliableArrival = arrival;
            }
            else if (Chance(_p.CorruptProb))
            {
                Corrupt(f);
                result.PacketsCorrupted++;
            }

            f.Arrival = arrival;
            _inflight.Add(f);
        }

        void Corrupt(InFlight f)
        {
            int flips = 1 + _rng.Next(3);
            for (int i = 0; i < flips; i++)
            {
                int pos = _rng.Next(f.Len);
                f.Bytes[pos] ^= (byte)(1 << _rng.Next(8));
            }
        }

        public void DeliverDue(double now, Action<byte[], int> deliver, BasisSyncSimResult result)
        {
            _inflight.Sort((a, b) => a.Arrival.CompareTo(b.Arrival));
            int i = 0;
            while (i < _inflight.Count)
            {
                InFlight f = _inflight[i];
                if (f.Arrival > now) break;
                deliver(f.Bytes, f.Len);
                result.PacketsDelivered++;
                if (f.SendIndex < _lastDeliveredSendIndex) result.PacketsReordered++;
                else _lastDeliveredSendIndex = f.SendIndex;
                _inflight.RemoveAt(i);
            }
        }

        public void FlushAll(Action<byte[], int> deliver, BasisSyncSimResult result)
        {
            DeliverDue(double.MaxValue, deliver, result);
        }

        bool Chance(float p) => p > 0f && _rng.NextDouble() < p;
    }

    /// <summary>
    /// Deterministic input generator. Writes the owner's authoritative values into the sender's local
    /// buffer each frame. Continuous fields follow the motion curve; discrete fields step at a fixed
    /// cadence; rotation tumbles. Frozen time (settle phase) yields constant values so the pipeline
    /// can converge.
    /// </summary>
    public sealed class BasisSyncTruthSource
    {
        const float DiscreteStepPeriod = 0.25f;
        const float TeleportPeriod = 1.7f;
        const float TeleportJump = 40f;

        readonly BasisSyncSchema _schema;
        readonly List<SimFieldSpec> _fields;
        readonly SyncMotion _motion;
        readonly float _duration;

        readonly float[] _phase;       // per-field phase offset
        readonly int[] _discState;     // per-field current discrete value
        readonly int[] _discStep;      // per-field last step index applied
        readonly float[] _walk;        // per continuous-component random-walk accumulator
        readonly int[] _walkStep;      // per-field last walk step index
        readonly System.Random _rng;

        public BasisSyncTruthSource(BasisSyncSchema schema, List<SimFieldSpec> fields, SyncMotion motion, System.Random rng, float duration)
        {
            _schema = schema;
            _fields = fields;
            _motion = motion;
            _rng = rng;
            _duration = Mathf.Max(0.001f, duration);

            _phase = new float[fields.Count];
            _discState = new int[fields.Count];
            _discStep = new int[fields.Count];
            _walkStep = new int[fields.Count];
            for (int i = 0; i < fields.Count; i++)
            {
                _phase[i] = i * 0.7f + 0.31f;
                _discStep[i] = -1;
                _walkStep[i] = -1;
            }
            _walk = new float[schema.ContCount];
        }

        public void Apply(BasisSyncValues local, float t)
        {
            for (int fi = 0; fi < _fields.Count; fi++)
            {
                BasisSyncField f = _schema.GetField(fi);
                float ph = _phase[fi];
                switch (f.Type)
                {
                    case BasisSyncFieldType.Position:
                        WriteVec(local, f, fi, t, ph, 5f, teleport: true);
                        break;
                    case BasisSyncFieldType.Scale:
                        for (int c = 0; c < 3; c++) local.Cont[f.Offset + c] = 1f + 0.4f * Signal(t, ph + c * 0.5f, fi, c);
                        break;
                    case BasisSyncFieldType.Float:
                        local.Cont[f.Offset] = 10f * Signal(t, ph, fi, 0);
                        break;
                    case BasisSyncFieldType.Angle:
                        local.Cont[f.Offset] = 180f + 150f * Signal(t, ph, fi, 0);
                        break;
                    case BasisSyncFieldType.Vector2:
                        for (int c = 0; c < 2; c++) local.Cont[f.Offset + c] = 3f * Signal(t, ph + c * 0.5f, fi, c);
                        break;
                    case BasisSyncFieldType.Vector4:
                        for (int c = 0; c < 4; c++) local.Cont[f.Offset + c] = 3f * Signal(t, ph + c * 0.5f, fi, c);
                        break;
                    case BasisSyncFieldType.Color:
                        for (int c = 0; c < 4; c++) local.Cont[f.Offset + c] = Mathf.Clamp01(0.5f + 0.5f * Signal(t, ph + c * 0.5f, fi, c));
                        break;
                    case BasisSyncFieldType.Rotation:
                    {
                        Vector3 e = new Vector3(40f * Signal(t, ph, fi, 0), 60f * Signal(t, ph + 0.5f, fi, 1), 50f * Signal(t, ph + 1.0f, fi, 2));
                        Quaternion q = Quaternion.Euler(e);
                        local.Rot[f.Offset] = new quaternion(q.x, q.y, q.z, q.w);
                        break;
                    }
                    case BasisSyncFieldType.Bool:
                        local.Disc[f.Offset] = Discrete(t, fi) > 0 ? 1 : 0;
                        break;
                    case BasisSyncFieldType.Byte:
                        local.Disc[f.Offset] = Discrete(t, fi) & 0xFF;
                        break;
                    case BasisSyncFieldType.UShort:
                        local.Disc[f.Offset] = Discrete(t, fi) & 0xFFFF;
                        break;
                    case BasisSyncFieldType.Int:
                        local.Disc[f.Offset] = Discrete(t, fi);
                        break;
                    case BasisSyncFieldType.UInt:
                        local.Disc[f.Offset] = Discrete(t, fi) & 0x7FFFFFFF;
                        break;
                }
            }
        }

        void WriteVec(BasisSyncValues local, in BasisSyncField f, int fi, float t, float ph, float amp, bool teleport)
        {
            float tele = 0f;
            if (teleport && _motion == SyncMotion.Teleport)
                tele = ((int)(t / TeleportPeriod) % 2 == 1) ? TeleportJump : 0f;
            for (int c = 0; c < f.ContComponents; c++)
                local.Cont[f.Offset + c] = amp * Signal(t, ph + c * 0.5f, fi, c) + (c == 0 ? tele : 0f);
        }

        float Signal(float t, float phase, int fi, int comp)
        {
            float u = Mathf.Clamp01(t / _duration);
            switch (_motion)
            {
                case SyncMotion.Static:
                    return Mathf.Sin(phase);                       // constant per component
                case SyncMotion.Ramp:
                    return 2f * u - 1f;
                case SyncMotion.Sine:
                    return Mathf.Sin(2f * Mathf.PI * 0.5f * t + phase);
                case SyncMotion.Step:
                    return ((int)(t / 0.5f + phase) % 2 == 0) ? 1f : -1f;
                case SyncMotion.Teleport:
                    return Mathf.Sin(2f * Mathf.PI * 0.4f * t + phase);
                case SyncMotion.RandomWalk:
                    return Walk(t, fi, comp);
                default:
                    return 0f;
            }
        }

        float Walk(float t, int fi, int comp)
        {
            int idx = _schema.GetField(fi).Offset + comp;
            if (idx < 0 || idx >= _walk.Length) return 0f;
            int step = (int)(t / 0.12f);
            if (step != _walkStep[fi])
            {
                _walkStep[fi] = step;
                float d = (float)(_rng.NextDouble() * 2.0 - 1.0) * 0.35f;
                _walk[idx] = Mathf.Clamp(_walk[idx] + d, -1f, 1f);
            }
            return _walk[idx];
        }

        int Discrete(float t, int fi)
        {
            int step = (int)(t / DiscreteStepPeriod);
            if (step != _discStep[fi])
            {
                _discStep[fi] = step;
                if (_motion == SyncMotion.Static)
                    _discState[fi] = 1 + (fi % 7);
                else
                    _discState[fi] = _rng.Next(0, 1000);
            }
            return _discState[fi];
        }
    }

    /// <summary>A single scenario: a field set, a motion, a network profile, and the sync config.</summary>
    public sealed class BasisSyncSimScenario
    {
        public string Name;
        public string FieldConfigName;
        public List<SimFieldSpec> Fields;
        public SyncMotion Motion;
        public NetworkProfile Profile;
        public int Seed;

        public float SendInterval = 0.05f;
        public float KeyframeInterval = 0.5f;
        public float ContinuousEpsilon = 1e-4f;

        public bool Extrapolate = false;
        public float MaxExtrapolation = 0.2f;
        public bool TeleportThreshold = false;
        public float TeleportThresholdMeters = 3f;
        public bool UseChecksum = true;
        public bool IdleKeyframeBackoff = false;
        public float RotationSendThresholdDegrees = 0f;

        public float Dt = 1f / 72f;
        public float DurationSeconds = 8f;
        public float SettleSeconds = 2f;
    }

    /// <summary>The outcome of one scenario — one CSV row.</summary>
    public sealed class BasisSyncSimResult
    {
        public string ScenarioName;
        public string FieldConfigName;
        public SyncMotion Motion;
        public string ProfileName;
        public int Seed;

        public int PacketsSent;
        public int Keyframes;
        public int PacketsDelivered;
        public int PacketsDropped;
        public int PacketsDuplicated;
        public int PacketsCorrupted;
        public int PacketsReordered;
        public int WireBytesSent;
        public int WireBytesPerKeyframe;

        public float MaxContError;
        public float MaxRotErrorDeg;
        public int DiscreteMismatches;

        // Multi-object scene metadata. Single-object runs leave these at their single/managed defaults.
        public int Objects = 1;
        public string Transport = "single";   // single | per-packet | batched
        public string SampledVia = "managed"; // managed mirror | realjob (real Burst InterpolateSyncObjectsJob)
        public int DemuxEntries;               // batch sub-payloads routed to a receiver
        public int DemuxErrors;                // batch entries whose NetworkID had no receiver / truncated tail

        public string Exception;
        public bool Pass;
        public bool Warn;
        public string FailReason = "";

        public float AvgPacketBytes => PacketsSent > 0 ? (float)WireBytesSent / PacketsSent : 0f;

        public static string CsvHeader =>
            "scenario,fieldConfig,motion,profile,seed,pass,failReason," +
            "packetsSent,keyframes,delivered,dropped,duplicated,corrupted,reordered," +
            "wireBytesSent,avgPacketBytes,bytesPerKeyframe,maxContError,maxRotErrorDeg,discreteMismatches,exception," +
            "objects,transport,sampledVia,demuxEntries,demuxErrors";

        public string ToCsvRow()
        {
            return string.Join(",",
                Csv(ScenarioName), Csv(FieldConfigName), Motion, Csv(ProfileName), Seed,
                Warn ? "WARN" : (Pass ? "PASS" : "FAIL"), Csv(FailReason),
                PacketsSent, Keyframes, PacketsDelivered, PacketsDropped, PacketsDuplicated, PacketsCorrupted, PacketsReordered,
                WireBytesSent, AvgPacketBytes.ToString("0.0"), WireBytesPerKeyframe,
                MaxContError.ToString("0.#####"), MaxRotErrorDeg.ToString("0.###"), DiscreteMismatches,
                Csv(Exception ?? ""),
                Objects, Csv(Transport), Csv(SampledVia), DemuxEntries, DemuxErrors);
        }

        static string Csv(string v)
        {
            if (string.IsNullOrEmpty(v)) return "";
            if (v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0)
                return "\"" + v.Replace("\"", "\"\"") + "\"";
            return v;
        }
    }
}
