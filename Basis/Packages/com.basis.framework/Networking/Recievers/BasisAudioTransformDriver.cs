using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

/// <summary>
/// Static transform batch applier with additive capacity control.
/// Usage:
/// - Initialize() once (optional capacity).
/// - Per frame: BeginFrame() then EndFrame(), or SimulateOneFrame().
/// - EnqueueSet / RequestRemove anytime (main thread).
/// - Shutdown() on domain unload.
/// </summary>
public static class BasisAudioTransformDriver
{
    // ------- State -------
    private static bool _initialized;
    private static int _initialCapacity = 128;

    // Job wiring
    private static JobHandle _handle;
    private static bool _jobScheduled;

    // Transform storage for the job system
    private static TransformAccessArray _taa;

    // Parallel per-entity arrays (aligned by index with _taa)
    private static NativeList<float3> _positions;     // target world positions
    private static NativeList<quaternion> _rotations; // target world rotations
    private static NativeList<int> _entryGen;         // "dirty" generation per entry

    // Managed mirrors & index map
    private static readonly List<Transform> _mirror = new List<Transform>(4096);
    private static readonly Dictionary<Transform, int> _indexOf = new Dictionary<Transform, int>(4096);

    // Deferred queues (safe to call EnqueueSet/RequestRemove anytime; we flush once per frame)
    private static readonly List<(Transform t, float3 p, quaternion r)> _queuedSetsThisFrame = new List<(Transform, float3, quaternion)>(4096);
    private static readonly HashSet<Transform> _queuedRemovals = new HashSet<Transform>();

    // "Dirty" tracking: only entries tagged with currentGen are touched
    private static int _currentGen = 1;
    private static int _dirtyCountThisGen = 0;

    // -------- Additive Capacity Controls --------
    // How many slots to add/remove at a time.
    private static int _growthStep = 128;

    // If utilization (length/capacity) stays below this for N frames, shrink.
    private static float _shrinkUtilizationThreshold = 0.55f;

    // Number of consecutive under-utilized frames before shrinking.
    private static int _shrinkConsecutiveFrames = 20;

    // Cooldown between shrinks to prevent thrash.
    private static int _shrinkCooldownFrames = 60;

    private static int _underUtilFrameCounter = 0;
    private static int _sinceLastShrinkFrames = 9999;

    public static bool IsInitialized => _initialized;
    public static int Count => _taa.isCreated ? _taa.length : 0;

    // ---------------- Job ----------------
    [BurstCompile]
    private struct ApplyTRJob : IJobParallelForTransform
    {
        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<quaternion> rotations;
        [ReadOnly] public NativeArray<int> entryGen;
        [ReadOnly] public int currentGen;

        public void Execute(int index, TransformAccess transform)
        {
            if (entryGen[index] == currentGen)
            {
                transform.SetPositionAndRotation(positions[index], rotations[index]);
            }
        }
    }

    // --------------- Lifecycle ---------------
    /// <summary>Allocate native containers and prepare capacity. Safe to call multiple times; no-ops after the first.</summary>
    public static void Initialize(int initialCapacity = 1024)
    {
        if (_initialized) return;

        _initialCapacity = Mathf.Max(1, initialCapacity);

        // Round initial capacity up to step boundary to make future shrink clean.
        int stepAligned = RoundUpToStep(_initialCapacity, _growthStep);

        _taa = new TransformAccessArray(stepAligned);
        _positions = new NativeList<float3>(stepAligned, Allocator.Persistent);
        _rotations = new NativeList<quaternion>(stepAligned, Allocator.Persistent);
        _entryGen = new NativeList<int>(stepAligned, Allocator.Persistent);

        _mirror.Capacity = Mathf.Max(_mirror.Capacity, stepAligned);

        _currentGen = 1;
        _dirtyCountThisGen = 0;
        _jobScheduled = false;
        _initialized = true;

        _underUtilFrameCounter = 0;
        _sinceLastShrinkFrames = 9999;
    }

    /// <summary>Dispose containers and clear all state.</summary>
    public static void Shutdown()
    {
        if (!_initialized) return;

        CompleteIfScheduled();

        if (_taa.isCreated) _taa.Dispose();
        if (_positions.IsCreated) _positions.Dispose();
        if (_rotations.IsCreated) _rotations.Dispose();
        if (_entryGen.IsCreated) _entryGen.Dispose();

        _queuedSetsThisFrame.Clear();
        _queuedRemovals.Clear();
        _indexOf.Clear();
        _mirror.Clear();

        _currentGen = 1;
        _dirtyCountThisGen = 0;
        _jobScheduled = false;
        _initialized = false;

        _underUtilFrameCounter = 0;
        _sinceLastShrinkFrames = 9999;
    }

    // --------------- Frame Simulation API ---------------
    public static void BeginFrame()
    {
        // Finish prior frame
        CompleteIfScheduled();

        // Apply queued removals/additions (no Transform writes here)
        FlushRemovals();
        FlushQueuedSets();

        // If anything was marked dirty this frame, schedule the single job
        if (_dirtyCountThisGen > 0 && _taa.length > 0)
        {
            var job = new ApplyTRJob
            {
                positions = _positions.AsDeferredJobArray(),
                rotations = _rotations.AsDeferredJobArray(),
                entryGen = _entryGen.AsDeferredJobArray(),
                currentGen = _currentGen
            };

            _handle = job.Schedule(_taa);
            _jobScheduled = true;
            JobHandle.ScheduleBatchedJobs();
        }
    }

    public static void EndFrame()
    {
        if (!_initialized) return;

        CompleteIfScheduled();

        // Advance generation and reset dirty count
        if (_dirtyCountThisGen > 0)
        {
            _dirtyCountThisGen = 0;
            _currentGen++;
            if (_currentGen == int.MaxValue) _currentGen = 1; // wrap safely
        }

        // Additive shrink evaluation once per frame (after mutations are settled)
        TryShrinkCapacity();

        _sinceLastShrinkFrames++;
    }

    public static void SimulateOneFrame()
    {
        BeginFrame();
        EndFrame();
    }

    public static void ForceComplete() => CompleteIfScheduled();

    private static void CompleteIfScheduled()
    {
        if (_jobScheduled)
        {
            _handle.Complete();
            _jobScheduled = false;
        }
    }

    // --------------- Public Work API ---------------
    public static void EnqueueSet(Transform t, Vector3 position, Quaternion rotation)
    {
        if (t == null) return;
        _queuedSetsThisFrame.Add((t, (float3)position, (quaternion)rotation));
    }

    public static void RequestRemove(Transform t)
    {
        if (t == null) return;
        _queuedRemovals.Add(t);
    }

    public static void ClearAll()
    {
        if (!_initialized) return;
        CompleteIfScheduled();

        _queuedSetsThisFrame.Clear();
        _queuedRemovals.Clear();
        _indexOf.Clear();
        _mirror.Clear();

        while (_taa.length > 0)
            _taa.RemoveAtSwapBack(_taa.length - 1);

        _positions.Clear();
        _rotations.Clear();
        _entryGen.Clear();

        _dirtyCountThisGen = 0;
        _currentGen = 1;

        // Bring capacity down to the nearest step to free memory.
        TrimExcessCapacity();
        _sinceLastShrinkFrames = 0;
    }

    // --------------- Internal: capacity management ---------------
    private static int RoundUpToStep(int value, int step)
    {
        if (step <= 1) return value;
        int rem = value % step;
        return rem == 0 ? value : (value + (step - rem));
    }

    private static int RoundDownToStep(int value, int step)
    {
        if (step <= 1) return value;
        return (value / step) * step;
    }

    /// <summary>
    /// Additive growth: increase capacity in +_growthStep chunks until it can fit 'needed'.
    /// </summary>
    private static void EnsureCapacityAdditive(int needed)
    {
        int capTaa = _taa.capacity;
        int cap = math.max(capTaa, math.max(_positions.Capacity, math.max(_rotations.Capacity, _entryGen.Capacity)));

        if (needed <= cap) return;

        int target = cap;
        while (target < needed) target += _growthStep;

        // Apply new capacity (step-aligned)
        _taa.capacity = target;
        _positions.Capacity = target;
        _rotations.Capacity = target;
        _entryGen.Capacity = target;

        _mirror.Capacity = math.max(_mirror.Capacity, target);
        // No changes to lengths here.
    }

    /// <summary>
    /// Try to shrink capacity additively when sustained under-utilization is detected.
    /// </summary>
    private static void TryShrinkCapacity()
    {
        if (!_initialized) return;

        int len = _taa.length;
        int cap = math.max(_taa.capacity, math.max(_positions.Capacity, math.max(_rotations.Capacity, _entryGen.Capacity)));
        if (cap <= 0) return;

        float util = (len <= 0) ? 0f : (len / (float)cap);

        if (util < _shrinkUtilizationThreshold)
        {
            _underUtilFrameCounter++;
        }
        else
        {
            _underUtilFrameCounter = 0;
        }

        if (_underUtilFrameCounter >= _shrinkConsecutiveFrames && _sinceLastShrinkFrames >= _shrinkCooldownFrames)
        {
            // Compute target capacity: the smallest step-aligned >= length.
            int minTarget = RoundUpToStep(math.max(len, 1), _growthStep);

            // Only shrink if it actually reduces capacity.
            if (minTarget < cap)
            {
                // Important: capacities cannot go below Length.
                int newCap = Mathf.Max(minTarget, len);

                // Apply step-aligned new capacity to all containers.
                _taa.capacity = newCap;
                _positions.Capacity = newCap;
                _rotations.Capacity = newCap;
                _entryGen.Capacity = newCap;

                // Managed list
                if (_mirror.Capacity > newCap) _mirror.Capacity = newCap;

                _sinceLastShrinkFrames = 0;
            }

            _underUtilFrameCounter = 0; // reset streak after a shrink attempt
        }
    }

    /// <summary>Force capacity down to the smallest step-aligned >= length (useful after ClearAll).</summary>
    private static void TrimExcessCapacity()
    {
        if (!_initialized) return;
        int len = _taa.length;
        int newCap = RoundUpToStep(math.max(len, 1), _growthStep);

        _taa.capacity = newCap;
        _positions.Capacity = newCap;
        _rotations.Capacity = newCap;
        _entryGen.Capacity = newCap;

        if (_mirror.Capacity > newCap) _mirror.Capacity = newCap;
    }

    // --------------- Internal: flush & bookkeeping ---------------
    private static void RebuildIndexMapAndRealign()
    {
        int targetLen = _taa.length;

        while (_mirror.Count > targetLen) _mirror.RemoveAt(_mirror.Count - 1);
        while (_positions.Length > targetLen) _positions.RemoveAtSwapBack(_positions.Length - 1);
        while (_rotations.Length > targetLen) _rotations.RemoveAtSwapBack(_rotations.Length - 1);
        while (_entryGen.Length > targetLen) _entryGen.RemoveAtSwapBack(_entryGen.Length - 1);

        _indexOf.Clear();
        for (int i = 0; i < _mirror.Count; i++)
        {
            var tr = _mirror[i];
            if (tr != null && !_indexOf.ContainsKey(tr))
                _indexOf.Add(tr, i);
        }
    }

    private static void FlushRemovals()
    {
        if (_queuedRemovals.Count == 0) return;

        if (_taa.length != _mirror.Count ||
            _positions.Length != _taa.length ||
            _rotations.Length != _taa.length ||
            _entryGen.Length != _taa.length)
        {
            RebuildIndexMapAndRealign();
        }

        _tmpRemovalBuffer.Clear();
        foreach (var t in _queuedRemovals)
        {
            if (t != null) _tmpRemovalBuffer.Add(t);
        }

        for (int n = 0; n < _tmpRemovalBuffer.Count; n++)
        {
            var t = _tmpRemovalBuffer[n];

            if (!_indexOf.TryGetValue(t, out int idx))
            {
                idx = _mirror.IndexOf(t);
                if (idx < 0) continue;
            }

            if (idx < 0 || idx >= _taa.length)
            {
                RebuildIndexMapAndRealign();
                if (!_indexOf.TryGetValue(t, out idx) || idx < 0 || idx >= _taa.length) continue;
            }

            int lastIdx = _taa.length - 1;

            // Remove from TAA (swap-back)
            _taa.RemoveAtSwapBack(idx);

            // Mirror & arrays swap-back
            int lastMirrorIdx = _mirror.Count - 1;

            if (idx != lastIdx)
            {
                _positions[idx] = _positions[lastMirrorIdx];
                _rotations[idx] = _rotations[lastMirrorIdx];
                _entryGen[idx] = _entryGen[lastMirrorIdx];

                var movedT = _mirror[lastMirrorIdx];
                _mirror[idx] = movedT;

                if (movedT != null)
                    _indexOf[movedT] = idx;
            }

            _positions.RemoveAtSwapBack(lastMirrorIdx);
            _rotations.RemoveAtSwapBack(lastMirrorIdx);
            _entryGen.RemoveAtSwapBack(lastMirrorIdx);
            _mirror.RemoveAt(lastMirrorIdx);

            _indexOf.Remove(t);
        }

        _queuedRemovals.Clear();

        if (_taa.length != _mirror.Count ||
            _positions.Length != _taa.length ||
            _rotations.Length != _taa.length ||
            _entryGen.Length != _taa.length)
        {
            RebuildIndexMapAndRealign();
        }
    }

    private static readonly List<Transform> _tmpRemovalBuffer = new List<Transform>(1024);

    private static void FlushQueuedSets()
    {
        if (_queuedSetsThisFrame.Count == 0) return;

        // Pre-ensure capacity additively for all potential new entries
        int needed = _taa.length;
        for (int i = 0; i < _queuedSetsThisFrame.Count; i++)
        {
            var t = _queuedSetsThisFrame[i].t;
            if (t != null && !_indexOf.ContainsKey(t)) needed++;
        }
        EnsureCapacityAdditive(needed);

        for (int i = 0; i < _queuedSetsThisFrame.Count; i++)
        {
            var (t, p, r) = _queuedSetsThisFrame[i];
            if (t == null) continue;

            int idx;
            if (!_indexOf.TryGetValue(t, out idx))
            {
                int newIdx = _taa.length;
                _taa.Add(t);
                _mirror.Add(t);

                _positions.Add(p);
                _rotations.Add(r);
                _entryGen.Add(_currentGen); // mark as dirty this frame
                _indexOf[t] = newIdx;

                _dirtyCountThisGen++;
            }
            else
            {
                if (idx < 0 || idx >= _taa.length)
                {
                    RebuildIndexMapAndRealign();

                    if (!_indexOf.TryGetValue(t, out idx) || idx < 0 || idx >= _taa.length)
                    {
                        int newIdx = _taa.length;
                        _taa.Add(t);
                        _mirror.Add(t);

                        _positions.Add(p);
                        _rotations.Add(r);
                        _entryGen.Add(_currentGen);
                        _indexOf[t] = newIdx;

                        _dirtyCountThisGen++;
                        continue;
                    }
                }

                _positions[idx] = p;
                _rotations[idx] = r;

                if (_entryGen[idx] != _currentGen)
                {
                    _entryGen[idx] = _currentGen;
                    _dirtyCountThisGen++;
                }
            }
        }

        _queuedSetsThisFrame.Clear();
    }
}
