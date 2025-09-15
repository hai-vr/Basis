using Basis.Scripts.Drivers;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;
namespace Basis.Scripts.UI.NamePlate
{
    public partial class BasisRemoteNamePlate
    {
        public static class BasisRemoteNamePlateBatchDriver
        {
            // --- Runtime state ---
            static bool _initialized;

            static TransformAccessArray _taa;                 // nameplates we rotate/position
            static NativeList<int> _mapNameplateToData;  // indirection: nameplate index -> data index

            // Batch-owned data arrays (hips/mouth). Kept packed; swizzle or grow as needed.
            static NativeList<float3> _hipsData;            // data index -> hips
            static NativeList<float> _mouthData;           // data index -> mouth

            // Fast lookup: Transform -> nameplate index
            static readonly Dictionary<int, int> _tToIdx = new Dictionary<int, int>(256);

            // Optional: data row reuse
            static readonly Stack<int> _freeDataRows = new Stack<int>(256);

            public static int NameplateCount => _taa.isCreated ? _taa.length : 0;
            public static int DataCount => _hipsData.IsCreated ? _hipsData.Length : 0;

            /// <summary>Call once before use. Pass a sensible capacity guess to minimize reallocs.</summary>
            public static void Initialize(int initialNameplates = 128, int initialDataRows = 128, Allocator allocator = Allocator.Persistent)
            {
                if (_initialized) return;

                _taa = new TransformAccessArray(initialNameplates);
                _mapNameplateToData = new NativeList<int>(initialNameplates, allocator);
                _hipsData = new NativeList<float3>(initialDataRows, allocator);
                _mouthData = new NativeList<float>(initialDataRows, allocator);

                _tToIdx.Clear();
                _freeDataRows.Clear();

                _initialized = true;
            }

            public static void Dispose()
            {
                if (!_initialized) return;

                if (_taa.isCreated) _taa.Dispose();
                if (_mapNameplateToData.IsCreated) _mapNameplateToData.Dispose();
                if (_hipsData.IsCreated) _hipsData.Dispose();
                if (_mouthData.IsCreated) _mouthData.Dispose();

                _tToIdx.Clear();
                _freeDataRows.Clear();
                _initialized = false;
            }
            [BurstCompile(FloatMode = FloatMode.Fast, FloatPrecision = FloatPrecision.Low)]
            public struct MappedNameplateApplyJob : IJobParallelForTransform
            {

                // Precompute 1 + 1/1.25 = 1.8f once; Burst will constant-fold this.
                // If you really want it derived: const float kY = 1f + 1f / yHeightMultiplier;
                private const float kY = 1.8f;

                public float3 CameraPosition;

                [ReadOnly] public NativeArray<int> mapNameplateToData;      // length == nameplate count
                [ReadOnly] public NativeArray<float3> outHipsPosData;          // batch-owned data arrays
                [ReadOnly] public NativeArray<float> HipToheadDifferenceScaledTpose;

                public void Execute(int jobIndex, TransformAccess tx)
                {
                    // Map nameplate -> data index
                    int dataIdx = mapNameplateToData[jobIndex];

                    // Read-only loads (Burst hoists & vectorizes nicely)
                    float3 hips = outHipsPosData[dataIdx];
                    float diff = HipToheadDifferenceScaledTpose[dataIdx];

                    // y = hips.y + diff * (1 + 1/1.25) = hips.y + diff * 1.8
                    float3 nameplatePos = new float3(hips.x, hips.y + diff * kY, hips.z);

                    // We only need yaw. atan2(x, z) gives radians; no normalization needed.
                    float3 toCam = CameraPosition - nameplatePos;
                    float yaw = math.atan2(toCam.x, toCam.z);

                    // Build rotation directly around Y in radians (avoids Quaternion.Euler + Rad2Deg).
                    quaternion rot = quaternion.RotateY(yaw);

                    tx.SetPositionAndRotation(nameplatePos, rot);
                }
            }

            // -------- Data row management (hips/mouth) --------

            /// <summary>Allocates a new data row (or reuses a free one) and writes initial values. Returns data index.</summary>
            public static int AllocateDataRow(float3 hips, float mouth)
            {

                int dataIdx = _freeDataRows.Count > 0 ? _freeDataRows.Pop() : _hipsData.Length;

                if (dataIdx == _hipsData.Length) // grow tail
                {
                    _hipsData.Add(hips);
                    _mouthData.Add(mouth);
                }
                else
                {
                    _hipsData[dataIdx] = hips;
                    _mouthData[dataIdx] = mouth;
                }

                return dataIdx;
            }

            /// <summary>Mark a data row as free for reuse. (No compaction; indirection map keeps things valid.)</summary>
            public static void FreeDataRow(int dataIdx)
            {
                // Optionally: sanity check dataIdx in range
                _freeDataRows.Push(dataIdx);
            }

            /// <summary>Update an existing data row.</summary>
            public static void UpdateDataRow(int dataIdx, in float3 hips, in float mouth)
            {
                _hipsData[dataIdx] = hips;
                _mouthData[dataIdx] = mouth;
            }

            // -------- Nameplate (transform) management --------

            /// <summary>Adds a nameplate Transform and binds it to an existing data row. Returns the nameplate index.</summary>
            public static int AddNameplate(Transform tx, int dataIdx)
            {
                var id = tx.GetInstanceID();

                if (_tToIdx.ContainsKey(id))
                    return _tToIdx[id]; // already added, idempotent

                // Append transform to TAA
                _taa.Add(tx);

                int nameplateIdx = _taa.length - 1;
                _mapNameplateToData.Add(dataIdx);
                _tToIdx[id] = nameplateIdx;

                return nameplateIdx;
            }

            /// <summary>Removes a nameplate. Swap-back to keep arrays packed. Returns true if removed.</summary>
            public static bool RemoveNameplate(Transform tx)
            {
                var id = tx.GetInstanceID();
                if (!_tToIdx.TryGetValue(id, out int idx)) return false;

                int last = _taa.length - 1;

                // If removing last, just pop
                if (idx == last)
                {
                    _taa.RemoveAtSwapBack(idx);
                    _mapNameplateToData.RemoveAtSwapBack(idx);
                    _tToIdx.Remove(id);
                    return true;
                }

                // Otherwise swap-back both TAA and map
                Transform lastTx = _taa[last];
                _taa.RemoveAtSwapBack(idx);
                _mapNameplateToData.RemoveAtSwapBack(idx);

                // Fix lookup for the swapped transform
                _tToIdx[lastTx.GetInstanceID()] = idx;
                _tToIdx.Remove(id);
                return true;
            }

            /// <summary>Removes a nameplate by index. Returns true if removed.</summary>
            public static bool RemoveNameplateAt(int nameplateIdx)
            {
                if (_taa.isCreated == false)
                {
                    return true;
                }

                if (nameplateIdx < 0 || nameplateIdx >= _taa.length) return false;

                int last = _taa.length - 1;
                Transform txToRemove = _taa[nameplateIdx];

                if (nameplateIdx == last)
                {
                    _taa.RemoveAtSwapBack(nameplateIdx);
                    _mapNameplateToData.RemoveAtSwapBack(nameplateIdx);
                    _tToIdx.Remove(txToRemove.GetInstanceID());
                    return true;
                }

                Transform lastTx = _taa[last];
                _taa.RemoveAtSwapBack(nameplateIdx);
                _mapNameplateToData.RemoveAtSwapBack(nameplateIdx);

                _tToIdx[lastTx.GetInstanceID()] = nameplateIdx;
                _tToIdx.Remove(txToRemove.GetInstanceID());
                return true;
            }

            /// <summary>Retarget a nameplate to a different data row.</summary>
            public static void RebindNameplate(Transform tx, int newDataIdx)
            {
                if (!_tToIdx.TryGetValue(tx.GetInstanceID(), out int idx)) return;
                _mapNameplateToData[idx] = newDataIdx;
            }

            /// <summary>Retarget by nameplate index.</summary>
            public static void RebindNameplate(int nameplateIdx, int newDataIdx)
            {
                _mapNameplateToData[nameplateIdx] = newDataIdx;
            }

            /// <summary>Try get the current nameplate index for a Transform.</summary>
            public static bool TryGetNameplateIndex(Transform tx, out int index)
                => _tToIdx.TryGetValue(tx.GetInstanceID(), out index);

            // -------- Scheduling --------

            /// <summary>Schedule the job that positions/rotates nameplates to face the camera.</summary>
            public static JobHandle Schedule()
            {
                var job = new MappedNameplateApplyJob
                {
                    CameraPosition = BasisLocalCameraDriver.Position,
                    mapNameplateToData = _mapNameplateToData.AsDeferredJobArray(),
                    outHipsPosData = _hipsData.AsDeferredJobArray(),
                    HipToheadDifferenceScaledTpose = _mouthData.AsDeferredJobArray()
                };

                // Defer length to TAA; Unity handles that safely for IJobParallelForTransform
                return job.Schedule(_taa);
            }

            // -------- Example helper: bulk set (useful for streaming updates) --------
            public static void BulkSetDataRows(NativeArray<float3> hips, NativeArray<float> mouths)
            {
                _hipsData.Resize(hips.Length, NativeArrayOptions.UninitializedMemory);
                _mouthData.Resize(mouths.Length, NativeArrayOptions.UninitializedMemory);
                hips.CopyTo(_hipsData.AsArray());
                mouths.CopyTo(_mouthData.AsArray());
            }
        }
    }
}
