using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Scripts.Networking.Sync.Testing
{
    /// <summary>
    /// Runs the REAL Burst <see cref="InterpolateSyncObjectsJob"/> over a driver-style multi-slot SoA layout,
    /// mirroring how <see cref="BasisSyncDriver"/> packs every remote object into shared pools and interpolates
    /// them in one pass. This lets the offline harness produce its "other side" output with the real job instead
    /// of a managed mirror, so the multi-slot base arithmetic and the inactive-slot skip get exercised — the
    /// parts a single-slot fidelity pin can never reach.
    /// </summary>
    public static class BasisSyncRealInterp
    {
        /// <summary>
        /// Per-slot pool layout for a set of objects: contiguous continuous / rotation / discrete windows plus the
        /// per-component interpolation mode, assigned exactly as <see cref="BasisSyncDriver.RebuildLayout"/> does.
        /// </summary>
        public sealed class Layout
        {
            public int N;
            public int[] ContBase, ContCount, RotBase, RotCount, DiscBase, DiscCount;
            public byte[] ContMode;
            public byte[] RotMode;
            public int ContTotal, RotTotal, DiscTotal;
        }

        public static Layout BuildLayout(IReadOnlyList<BasisSyncSchema> schemas)
        {
            int n = schemas.Count;
            var L = new Layout
            {
                N = n,
                ContBase = new int[n], ContCount = new int[n],
                RotBase = new int[n], RotCount = new int[n],
                DiscBase = new int[n], DiscCount = new int[n],
            };

            int cb = 0, rb = 0, db = 0;
            for (int i = 0; i < n; i++)
            {
                BasisSyncSchema s = schemas[i];
                L.ContBase[i] = cb; L.ContCount[i] = s.ContCount;
                L.RotBase[i] = rb; L.RotCount[i] = s.RotCount;
                L.DiscBase[i] = db; L.DiscCount[i] = s.DiscCount;
                cb += s.ContCount; rb += s.RotCount; db += s.DiscCount;
            }
            L.ContTotal = cb; L.RotTotal = rb; L.DiscTotal = db;

            L.ContMode = new byte[Mathf.Max(1, cb)];
            L.RotMode = new byte[Mathf.Max(1, rb)];
            for (int i = 0; i < n; i++)
            {
                BasisSyncSchema s = schemas[i];
                int sCb = L.ContBase[i], sRb = L.RotBase[i];
                for (int f = 0; f < s.FieldCount; f++)
                {
                    BasisSyncField fld = s.GetField(f);
                    if (fld.Pool == BasisSyncPool.Continuous)
                    {
                        byte mode = fld.Type == BasisSyncFieldType.Angle
                            ? (fld.Interpolate ? (byte)2 : (byte)0)
                            : (fld.Interpolate ? (byte)1 : (byte)0);
                        for (int c = 0; c < fld.ContComponents; c++)
                            L.ContMode[sCb + fld.Offset + c] = mode;
                    }
                    else if (fld.Pool == BasisSyncPool.Rotation)
                    {
                        L.RotMode[sRb + fld.Offset] = (byte)(fld.Interpolate ? 1 : 0);
                    }
                }
            }
            return L;
        }

        /// <summary>Pulls each slot's Current/Next/InterpTime from its receiver, then runs the real job; inactive (no-data) receivers leave their out values untouched.</summary>
        public static void Sample(Layout L, IReadOnlyList<BasisSyncReceiver> receivers, BasisSyncValues[] outValues)
        {
            int n = L.N;
            var cur = new BasisSyncValues[n];
            var next = new BasisSyncValues[n];
            var t = new float[n];
            var active = new bool[n];
            for (int i = 0; i < n; i++)
            {
                BasisSyncReceiver r = receivers[i];
                if (r != null && r.HasData)
                {
                    cur[i] = r.CurrentValues;
                    next[i] = r.NextValues;
                    t[i] = r.InterpTime;
                    active[i] = true;
                }
            }
            Run(L, cur, next, t, active, outValues);
        }

        /// <summary>
        /// Runs the real job for every slot given explicit Current/Next values, per-slot interp fraction, and active
        /// mask. Each active slot's interpolated output is written into <paramref name="outValues"/>[i]; inactive
        /// slots are skipped by the job and left untouched.
        /// </summary>
        public static void Run(Layout L, BasisSyncValues[] cur, BasisSyncValues[] next, float[] t, bool[] active, BasisSyncValues[] outValues)
        {
            int n = L.N;
            int cc = Mathf.Max(1, L.ContTotal), rc = Mathf.Max(1, L.RotTotal), dc = Mathf.Max(1, L.DiscTotal);

            var activeN = new NativeArray<byte>(Mathf.Max(1, n), Allocator.Temp);
            var tN = new NativeArray<float>(Mathf.Max(1, n), Allocator.Temp);
            var contBase = new NativeArray<int>(Mathf.Max(1, n), Allocator.Temp);
            var contCount = new NativeArray<int>(Mathf.Max(1, n), Allocator.Temp);
            var rotBase = new NativeArray<int>(Mathf.Max(1, n), Allocator.Temp);
            var rotCount = new NativeArray<int>(Mathf.Max(1, n), Allocator.Temp);
            var discBase = new NativeArray<int>(Mathf.Max(1, n), Allocator.Temp);
            var discCount = new NativeArray<int>(Mathf.Max(1, n), Allocator.Temp);
            var contCur = new NativeArray<float>(cc, Allocator.Temp);
            var contNext = new NativeArray<float>(cc, Allocator.Temp);
            var contMode = new NativeArray<byte>(cc, Allocator.Temp);
            var rotCur = new NativeArray<quaternion>(rc, Allocator.Temp);
            var rotNext = new NativeArray<quaternion>(rc, Allocator.Temp);
            var rotMode = new NativeArray<byte>(rc, Allocator.Temp);
            var discNext = new NativeArray<int>(dc, Allocator.Temp);
            var contOut = new NativeArray<float>(cc, Allocator.Temp);
            var rotOut = new NativeArray<quaternion>(rc, Allocator.Temp);
            var discOut = new NativeArray<int>(dc, Allocator.Temp);

            try
            {
                for (int i = 0; i < rc; i++) { rotCur[i] = quaternion.identity; rotNext[i] = quaternion.identity; rotOut[i] = quaternion.identity; }
                for (int i = 0; i < L.ContTotal; i++) contMode[i] = L.ContMode[i];
                for (int i = 0; i < L.RotTotal; i++) rotMode[i] = L.RotMode[i];

                for (int i = 0; i < n; i++)
                {
                    contBase[i] = L.ContBase[i]; contCount[i] = L.ContCount[i];
                    rotBase[i] = L.RotBase[i]; rotCount[i] = L.RotCount[i];
                    discBase[i] = L.DiscBase[i]; discCount[i] = L.DiscCount[i];

                    if (!active[i] || cur[i] == null || next[i] == null) { activeN[i] = 0; continue; }

                    int cb = L.ContBase[i], rb = L.RotBase[i], dbb = L.DiscBase[i];
                    for (int c = 0; c < L.ContCount[i]; c++) { contCur[cb + c] = cur[i].Cont[c]; contNext[cb + c] = next[i].Cont[c]; }
                    for (int r = 0; r < L.RotCount[i]; r++) { rotCur[rb + r] = cur[i].Rot[r]; rotNext[rb + r] = next[i].Rot[r]; }
                    for (int d = 0; d < L.DiscCount[i]; d++) discNext[dbb + d] = next[i].Disc[d];

                    tN[i] = t[i];
                    activeN[i] = 1;
                }

                var job = new InterpolateSyncObjectsJob
                {
                    Active = activeN, T = tN,
                    ContBase = contBase, ContCount = contCount,
                    RotBase = rotBase, RotCount = rotCount,
                    DiscBase = discBase, DiscCount = discCount,
                    ContCur = contCur, ContNext = contNext, ContMode = contMode,
                    RotCur = rotCur, RotNext = rotNext, RotMode = rotMode,
                    DiscNext = discNext,
                    ContOut = contOut, RotOut = rotOut, DiscOut = discOut,
                };
                for (int i = 0; i < n; i++) job.Execute(i);

                for (int i = 0; i < n; i++)
                {
                    if (activeN[i] == 0 || outValues[i] == null) continue;
                    int cb = L.ContBase[i], rb = L.RotBase[i], dbb = L.DiscBase[i];
                    for (int c = 0; c < L.ContCount[i]; c++) outValues[i].Cont[c] = contOut[cb + c];
                    for (int r = 0; r < L.RotCount[i]; r++) outValues[i].Rot[r] = rotOut[rb + r];
                    for (int d = 0; d < L.DiscCount[i]; d++) outValues[i].Disc[d] = discOut[dbb + d];
                }
            }
            finally
            {
                activeN.Dispose(); tN.Dispose();
                contBase.Dispose(); contCount.Dispose();
                rotBase.Dispose(); rotCount.Dispose();
                discBase.Dispose(); discCount.Dispose();
                contCur.Dispose(); contNext.Dispose(); contMode.Dispose();
                rotCur.Dispose(); rotNext.Dispose(); rotMode.Dispose();
                discNext.Dispose();
                contOut.Dispose(); rotOut.Dispose(); discOut.Dispose();
            }
        }
    }
}
