using System;
using System.Collections.Generic;
using UnityEngine;

namespace Basis.Scripts.Networking.Sync.Testing
{
    public enum SceneTransport { PerPacket, Batched }
    public enum SceneSample { Managed, RealJob }

    /// <summary>One object in a multi-object scene: a name and its field set (its schema).</summary>
    public sealed class BasisSyncSceneObjectSpec
    {
        public string Name;
        public List<SimFieldSpec> Fields;

        public BasisSyncSceneObjectSpec(string name, List<SimFieldSpec> fields)
        {
            Name = name;
            Fields = fields;
        }
    }

    /// <summary>
    /// A scene of several synced objects sharing one wire. Adds the interactions a single-object run can't reach:
    /// the real batch coalescing/demux (many objects' payloads framed into one datagram and split on the far side,
    /// with shared-fate loss), the real Burst interpolation over a multi-slot driver SoA, and a late-joiner that
    /// only recovers via a keyframe.
    /// </summary>
    public sealed class BasisSyncSceneScenario
    {
        public string Name;
        public List<BasisSyncSceneObjectSpec> Objects;
        public SyncMotion Motion;
        public NetworkProfile Profile;
        public int Seed;

        public SceneTransport Transport = SceneTransport.Batched;
        public SceneSample Sample = SceneSample.RealJob;

        // 0 = every receiver listens from the start. >0 = receivers attach at this frame (earlier packets never
        // reach them) and the owners force a keyframe then, mirroring OnPlayerJoined.
        public int LateJoinFrame = 0;

        public bool IdleKeyframeBackoff = false;
        public float RotationSendThresholdDegrees = 0f;
        public bool UseChecksum = true;
        public bool Extrapolate = false;
        public float MaxExtrapolation = 0.2f;

        public float SendInterval = 0.05f;
        public float KeyframeInterval = 0.5f;
        public float ContinuousEpsilon = 1e-4f;

        public float Dt = 1f / 72f;
        public float DurationSeconds = 6f;
        public float SettleSeconds = 1.8f;
    }

    /// <summary>
    /// Offline exerciser for a <see cref="BasisSyncSceneScenario"/>. Drives K real senders/receivers over a shared
    /// seeded network; in Batched mode it coalesces each frame's keyframes and deltas through the real
    /// <see cref="BasisSyncBatchWriter"/> and demuxes them via <see cref="BasisSyncBatchReader"/>. Produces one
    /// <see cref="BasisSyncSimResult"/> per object so each object's convergence is scored independently.
    /// </summary>
    public static class BasisSyncSceneSim
    {
        public static List<BasisSyncSimResult> RunScene(BasisSyncSceneScenario s)
        {
            int n = s.Objects.Count;
            var schemas = new BasisSyncSchema[n];
            var senders = new SimSender[n];
            var receivers = new BasisSyncReceiver[n];
            var truths = new BasisSyncTruthSource[n];
            var outVals = new BasisSyncValues[n];
            var results = new List<BasisSyncSimResult>(n);

            string transport = s.Transport == SceneTransport.Batched ? "batched" : "per-packet";
            string sampledVia = s.Sample == SceneSample.RealJob ? "realjob" : "managed";

            for (int i = 0; i < n; i++)
            {
                BasisSyncSceneObjectSpec spec = s.Objects[i];
                schemas[i] = BasisSyncSim.BuildSchema(spec.Fields);
                senders[i] = new SimSender(schemas[i])
                {
                    SendInterval = s.SendInterval,
                    KeyframeInterval = s.KeyframeInterval,
                    ContinuousEpsilon = s.ContinuousEpsilon,
                    UseChecksum = s.UseChecksum,
                    UseIdleKeyframeBackoff = s.IdleKeyframeBackoff,
                    RotationSendThresholdDegrees = s.RotationSendThresholdDegrees,
                };
                receivers[i] = new BasisSyncReceiver(schemas[i]);
                receivers[i].Configure(s.Extrapolate, s.MaxExtrapolation, false, 0f, 0, 0, s.UseChecksum);
                truths[i] = new BasisSyncTruthSource(schemas[i], spec.Fields, s.Motion, new System.Random(s.Seed * 397 + 13 + i * 7919), s.DurationSeconds);
                outVals[i] = new BasisSyncValues();
                outVals[i].Allocate(schemas[i]);
                results.Add(new BasisSyncSimResult
                {
                    ScenarioName = $"{s.Name}/{spec.Name}",
                    FieldConfigName = spec.Name,
                    Motion = s.Motion,
                    ProfileName = s.Profile.Name,
                    Seed = s.Seed,
                    Objects = n,
                    Transport = transport,
                    SampledVia = sampledVia,
                    WireBytesPerKeyframe = BasisSyncCodec.MaxSerializedSize(schemas[i]),
                });
            }

            // Per-packet: each object rides its own independent datagram (faithful — loss is per object).
            // Batched: one shared wire carries the batch datagrams (loss is shared-fate across the objects in a batch).
            var perNet = new SimNetwork[n];
            SimNetwork shared = null;
            var sceneNet = new BasisSyncSimResult();
            if (s.Transport == SceneTransport.PerPacket)
                for (int i = 0; i < n; i++) perNet[i] = new SimNetwork(s.Profile, new System.Random(s.Seed * 131 + i * 17 + 7));
            else
                shared = new SimNetwork(s.Profile, new System.Random(s.Seed * 131 + 7));

            int bufSize = 32;
            for (int i = 0; i < n; i++) bufSize += BasisSyncCodec.MaxSerializedSize(schemas[i]) + BasisSyncBatchWriter.EntryHeader;
            var reliableBuf = new byte[bufSize];
            var unreliableBuf = new byte[bufSize];

            var demuxErrors = new int[1];
            void Demux(byte[] bytes, int len)
            {
                var reader = new BasisSyncBatchReader(bytes, len);
                while (reader.TryRead(out ushort id, out int off, out int plen))
                {
                    int oi = id - 1;
                    if ((uint)oi >= (uint)n) { demuxErrors[0]++; continue; }
                    var sub = new byte[plen];
                    Array.Copy(bytes, off, sub, 0, plen);
                    receivers[oi].OnPacket(sub, plen);
                    results[oi].DemuxEntries++;
                    results[oi].PacketsDelivered++;
                }
            }

            int activeFrames = Mathf.CeilToInt(s.DurationSeconds / s.Dt);
            int settleFrames = Mathf.CeilToInt(s.SettleSeconds / s.Dt);
            double frozenTime = activeFrames * (double)s.Dt;
            double t = 0;
            bool joined = s.LateJoinFrame <= 0;
            byte batchSeq = 0;

            try
            {
                for (int frame = 0; frame < activeFrames + settleFrames; frame++)
                {
                    t += s.Dt;
                    bool activePhase = frame < activeFrames;
                    float motionTime = activePhase ? (float)t : (float)frozenTime;
                    for (int i = 0; i < n; i++) truths[i].Apply(senders[i].Local, motionTime);

                    if (!joined && frame >= s.LateJoinFrame)
                    {
                        joined = true;
                        for (int i = 0; i < n; i++) senders[i].ForceKeyframe();
                    }

                    var reliableW = new BasisSyncBatchWriter(reliableBuf);
                    var unreliableW = new BasisSyncBatchWriter(unreliableBuf);
                    List<SimPacket> emitted = s.Transport == SceneTransport.Batched ? new List<SimPacket>() : null;

                    for (int i = 0; i < n; i++)
                    {
                        SimPacket pkt = senders[i].Tick(t);
                        if (pkt == null || !joined) continue;

                        results[i].PacketsSent++;
                        results[i].WireBytesSent += pkt.Len;
                        if (pkt.Reliable) results[i].Keyframes++;

                        if (s.Transport == SceneTransport.PerPacket)
                        {
                            perNet[i].Send(pkt, t, results[i]);
                        }
                        else if (pkt.Reliable)
                        {
                            AppendOrEmit(ref reliableW, reliableBuf, (ushort)(i + 1), pkt, true, ref batchSeq, emitted);
                        }
                        else
                        {
                            AppendOrEmit(ref unreliableW, unreliableBuf, (ushort)(i + 1), pkt, false, ref batchSeq, emitted);
                        }
                    }

                    if (s.Transport == SceneTransport.Batched)
                    {
                        Emit(ref reliableW, reliableBuf, true, ref batchSeq, emitted);
                        Emit(ref unreliableW, unreliableBuf, false, ref batchSeq, emitted);
                        for (int e = 0; e < emitted.Count; e++) shared.Send(emitted[e], t, sceneNet);
                        shared.DeliverDue(t, Demux, sceneNet);
                    }
                    else
                    {
                        for (int i = 0; i < n; i++) perNet[i].DeliverDue(t, receivers[i].OnPacket, results[i]);
                    }

                    for (int i = 0; i < n; i++) receivers[i].Advance(s.Dt);
                }

                if (s.Transport == SceneTransport.Batched) shared.FlushAll(Demux, sceneNet);
                else for (int i = 0; i < n; i++) perNet[i].FlushAll(receivers[i].OnPacket, results[i]);

                for (int k = 0; k < 64; k++)
                    for (int i = 0; i < n; i++) receivers[i].Advance(s.Dt);

                Sample(s, schemas, receivers, outVals);
            }
            catch (Exception e)
            {
                string ex = e.GetType().Name + ": " + e.Message;
                for (int i = 0; i < n; i++) results[i].Exception = ex;
            }

            // Surface the batch's shared-fate wire stats on every object that rode it.
            if (s.Transport == SceneTransport.Batched)
            {
                for (int i = 0; i < n; i++)
                {
                    results[i].PacketsDropped = sceneNet.PacketsDropped;
                    results[i].PacketsDuplicated = sceneNet.PacketsDuplicated;
                    results[i].PacketsCorrupted = sceneNet.PacketsCorrupted;
                    results[i].PacketsReordered = sceneNet.PacketsReordered;
                    results[i].DemuxErrors = demuxErrors[0];
                }
            }

            for (int i = 0; i < n; i++)
                BasisSyncSim.Evaluate(schemas[i], s.Profile.HardConvergence, s.UseChecksum, senders[i].Local, outVals[i], receivers[i].HasData, results[i]);

            return results;
        }

        static void Sample(BasisSyncSceneScenario s, BasisSyncSchema[] schemas, BasisSyncReceiver[] receivers, BasisSyncValues[] outVals)
        {
            int n = schemas.Length;
            if (s.Sample == SceneSample.RealJob)
            {
                BasisSyncRealInterp.Layout layout = BasisSyncRealInterp.BuildLayout(schemas);
                BasisSyncRealInterp.Sample(layout, receivers, outVals);
                return;
            }

            for (int i = 0; i < n; i++)
            {
                BasisSyncReceiver r = receivers[i];
                if (!r.HasData) continue;
                BasisSyncSim.Interpolate(schemas[i], r.CurrentValues, r.NextValues, r.InterpTime, outVals[i]);
            }
        }

        // Mirrors BasisSyncBatchCollector.Append: try to coalesce; if the batch is full, emit it and retry on the
        // emptied writer. The scene sizes its batch buffer to fit every object, so the retry always succeeds.
        static void AppendOrEmit(ref BasisSyncBatchWriter w, byte[] buf, ushort id, SimPacket pkt, bool reliable, ref byte seq, List<SimPacket> emitted)
        {
            if (w.TryAppend(id, pkt.Bytes, pkt.Len)) return;
            if (w.Count > 0)
            {
                Emit(ref w, buf, reliable, ref seq, emitted);
                w.TryAppend(id, pkt.Bytes, pkt.Len);
            }
        }

        static void Emit(ref BasisSyncBatchWriter w, byte[] buf, bool reliable, ref byte seq, List<SimPacket> emitted)
        {
            int length = w.Length;
            if (length == 0) return;
            var bytes = new byte[length];
            Array.Copy(buf, 0, bytes, 0, length);
            unchecked { seq++; }
            emitted.Add(new SimPacket { Bytes = bytes, Len = length, Seq = seq, Reliable = reliable });
            w.Reset();
        }
    }
}
