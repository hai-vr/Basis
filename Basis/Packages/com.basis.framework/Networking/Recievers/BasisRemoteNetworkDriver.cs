using Basis.Scripts.Networking.Receivers;
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public static partial class BasisRemoteNetworkDriver
{
    /// <summary>
    /// Returns the number of active (registered) players.
    /// </summary>
    public static int ActivePlayerCount => _activePlayers.Count;

    /// <summary>
    /// Muscle count used by the driver.
    /// </summary>
    public static int MuscleCount => _muscleCount;

    // Native buffers (size == _capacity, except muscles which is _capacity * _muscleCount)
    static NativeArray<float3> _prevPositions;
    static NativeArray<float3> _targetPositions;
    static NativeArray<float3> _prevScales;
    static NativeArray<float3> _targetScales;
    static NativeArray<quaternion> _prevRotations;
    static NativeArray<quaternion> _targetRotations;
    static NativeArray<float> _interpolationTimes; // per-player dt or interpolation t

    static NativeArray<float3> _outPositions;
    static NativeArray<float3> _outScales;
    static NativeArray<quaternion> _outRotations;

    // Muscles (flattened: players * muscles)
    static NativeArray<float> _prevMuscles;   // flattened
    static NativeArray<float> _targetMuscles; // flattened
    static NativeArray<float> _outMuscles;    // flattened (interpolated, before filter)

    // 1€ filter buffers (flattened: players * muscles)
    static NativeArray<float> euroValuesOutput;   // filtered output
    static NativeArray<float2> positionFilters;   // per-channel state
    static NativeArray<float2> derivativeFilters; // per-channel state

    // Parameters for Euro filter
    static float MinCutoff = 1.0f;
    static float Beta = 0.01f;
    static float DerivativeCutoff = 1.0f;

    // Managed bookkeeping
    static readonly List<BasisNetworkReceiver> _activePlayers = new List<BasisNetworkReceiver>(128);
    static readonly Dictionary<BasisNetworkReceiver, int> _indexMap = new Dictionary<BasisNetworkReceiver, int>(128);
    static readonly Stack<int> _freeIndices = new Stack<int>(128);

    static readonly List<BasisNetworkReceiver> _pendingAdd = new List<BasisNetworkReceiver>(32);
    static readonly List<BasisNetworkReceiver> _pendingRemove = new List<BasisNetworkReceiver>(32);

    static int _capacity;
    static int _muscleCount;
    static bool _initialized;
    static Allocator _allocator = Allocator.Persistent;

    static void EnsureInitialized()
    {
        if (!_initialized)
            throw new InvalidOperationException("BasisRemoteNetworkDriver.Initialize(...) must be called before use.");
    }

    /// <summary>
    /// Initialize the driver. Must be called before any Add/Simulate/Shutdown.
    /// </summary>
    public static void Initialize(int muscleCount, int initialCapacity = 64, Allocator allocator = Allocator.Persistent)
    {
        if (_initialized) return;

        if (muscleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(muscleCount));
        }
        _allocator = allocator;
        _muscleCount = muscleCount;
        _capacity = math.max(16, initialCapacity);

        AllocateAll(_capacity);

        _initialized = true;
    }

    /// <summary>
    /// Dispose all native allocations. Call on shutdown/domain unload.
    /// </summary>
    public static void Shutdown()
    {
        if (!_initialized) return;

        DisposeAll();

        _indexMap.Clear();
        _freeIndices.Clear();
        _activePlayers.Clear();
        _pendingAdd.Clear();
        _pendingRemove.Clear();

        _initialized = false;
    }

    /// <summary>
    /// Queue a player to be added. The player gets assigned a stable index at the start of the next Simulate().
    /// </summary>
    public static void AddPlayer(BasisNetworkReceiver player)
    {
        if (player == null) return;
        EnsureInitialized();
        if (_indexMap.ContainsKey(player)) return;
        _pendingAdd.Add(player);
    }

    /// <summary>
    /// Queue a player to be removed. Removal is applied at the start of the next Simulate().
    /// </summary>
    public static void RemovePlayer(BasisNetworkReceiver player)
    {
        if (player == null) return;
        EnsureInitialized();
        if (!_indexMap.ContainsKey(player)) return;
        _pendingRemove.Add(player);
    }

    /// <summary>
    /// Write the per-player inputs for this frame (previous/target transform, interpolation t, and muscles).
    /// Call after AddPlayer and before Compute().
    /// </summary>
    public static void SetPlayerInputs(
        BasisNetworkReceiver player,
        float3 prevPos, float3 targetPos,
        float3 prevScale, float3 targetScale,
        quaternion prevRot, quaternion targetRot,
        float interpolationTime,
        float[] prevMuscles, float[] targetMuscles)
    {
        EnsureInitialized();
        if (!_indexMap.TryGetValue(player, out var idx)) return;

        _prevPositions[idx] = prevPos;
        _targetPositions[idx] = targetPos;
        _prevScales[idx] = prevScale;
        _targetScales[idx] = targetScale;
        _prevRotations[idx] = prevRot;
        _targetRotations[idx] = targetRot;
        _interpolationTimes[idx] = math.clamp(interpolationTime, 0f, 1f);

        // Flattened write: [idx * MuscleCount .. (idx+1) * MuscleCount)
        int baseOffset = idx * _muscleCount;

        // Copy muscles (NativeArray.Copy handles same-length validation)
        NativeArray<float>.Copy(prevMuscles, 0, _prevMuscles, baseOffset, _muscleCount);
        NativeArray<float>.Copy(targetMuscles, 0, _targetMuscles, baseOffset, _muscleCount);
    }

    /// <summary>
    /// Read back the computed outputs for a player after Apply().
    /// </summary>
    public static bool GetPlayerOutputs(
        BasisNetworkReceiver player,
        out float3 outPos, out float3 outScale, out quaternion outRot,
        ref float[] outMuscles) // length == MuscleCount
    {
        EnsureInitialized();
        outPos = default;
        outScale = default;
        outRot = default;

        if (!_indexMap.TryGetValue(player, out var idx)) return false;

        outPos = _outPositions[idx];
        outScale = _outScales[idx];
        outRot = _outRotations[idx];

        int baseOffset = idx * _muscleCount;
        NativeArray<float>.Copy(euroValuesOutput, baseOffset, outMuscles, 0, _muscleCount);
        return true;
    }

    /// <summary>
    /// Run the batched jobs once for the current frame.
    /// It applies pending adds/removes in a safe place, resizes buffers if needed, and completes the jobs.
    /// </summary>
    public static void Compute()
    {
        EnsureInitialized();

        ApplyPendingAddsAndRemoves();

        int numPlayers = _activePlayers.Count;
        if (numPlayers == 0) return;

        var avatarJob = new UpdateAllAvatarsJob
        {
            PreviousPositions = _prevPositions,
            TargetPositions = _targetPositions,
            PreviousScales = _prevScales,
            TargetScales = _targetScales,
            PreviousRotations = _prevRotations,
            TargetRotations = _targetRotations,
            InterpolationTimes = _interpolationTimes,

            OutputPositions = _outPositions,
            OutputScales = _outScales,
            OutputRotations = _outRotations
        }.Schedule(numPlayers, 64);

        // Interpolate muscles across players * muscles (flattened)
        JobHandle musclesJob = new UpdateAllAvatarMusclesJob
        {
            PreviousMuscles = _prevMuscles,
            TargetMuscles = _targetMuscles,
            InterpolationTimes = _interpolationTimes, // IMPORTANT: this job must index by player, not flattened
            OutputMuscles = _outMuscles,
            MuscleCountPerAvatar = _muscleCount
        }.Schedule(numPlayers * _muscleCount, 128, avatarJob);

        // 1€ filter: read interpolated muscles, write filtered output
        oneEuroJob = new BasisOneEuroFilterParallelJob
        {
            InputValues = _outMuscles,          // raw/interpolated input per (player,muscle)
            OutputValues = euroValuesOutput,    // filtered output
            DeltaTime = _interpolationTimes,    // per-player dt / interpolation t
            MinCutoff = MinCutoff,
            Beta = Beta,
            DerivativeCutoff = DerivativeCutoff,
            PositionFilters = positionFilters,
            DerivativeFilters = derivativeFilters,
            MuscleCountPerAvatar = _muscleCount
        }.Schedule(numPlayers * _muscleCount, 64, musclesJob);
    }

    public static JobHandle oneEuroJob;

    /// <summary>
    /// Completes all scheduled work for this frame.
    /// </summary>
    public static void Apply()
    {
        oneEuroJob.Complete();
    }

    static void AllocateAll(int capacity)
    {
        // Transform data
        _prevPositions = new NativeArray<float3>(capacity, _allocator, NativeArrayOptions.UninitializedMemory);
        _targetPositions = new NativeArray<float3>(capacity, _allocator, NativeArrayOptions.UninitializedMemory);
        _prevScales = new NativeArray<float3>(capacity, _allocator, NativeArrayOptions.UninitializedMemory);
        _targetScales = new NativeArray<float3>(capacity, _allocator, NativeArrayOptions.UninitializedMemory);
        _prevRotations = new NativeArray<quaternion>(capacity, _allocator, NativeArrayOptions.UninitializedMemory);
        _targetRotations = new NativeArray<quaternion>(capacity, _allocator, NativeArrayOptions.UninitializedMemory);
        _interpolationTimes = new NativeArray<float>(capacity, _allocator, NativeArrayOptions.ClearMemory);

        _outPositions = new NativeArray<float3>(capacity, _allocator, NativeArrayOptions.UninitializedMemory);
        _outScales = new NativeArray<float3>(capacity, _allocator, NativeArrayOptions.UninitializedMemory);
        _outRotations = new NativeArray<quaternion>(capacity, _allocator, NativeArrayOptions.UninitializedMemory);

        // Muscles (flattened)
        int flat = capacity * _muscleCount;
        _prevMuscles = new NativeArray<float>(flat, _allocator, NativeArrayOptions.UninitializedMemory);
        _targetMuscles = new NativeArray<float>(flat, _allocator, NativeArrayOptions.UninitializedMemory);
        _outMuscles = new NativeArray<float>(flat, _allocator, NativeArrayOptions.UninitializedMemory);

        // Euro filter buffers (flattened)
        euroValuesOutput = new NativeArray<float>(flat, _allocator, NativeArrayOptions.UninitializedMemory);
        positionFilters = new NativeArray<float2>(flat, _allocator, NativeArrayOptions.UninitializedMemory);
        derivativeFilters = new NativeArray<float2>(flat, _allocator, NativeArrayOptions.UninitializedMemory);
    }

    static void DisposeAll()
    {
        // Dispose safely
        if (_prevPositions.IsCreated) _prevPositions.Dispose();
        if (_targetPositions.IsCreated) _targetPositions.Dispose();
        if (_prevScales.IsCreated) _prevScales.Dispose();
        if (_targetScales.IsCreated) _targetScales.Dispose();
        if (_prevRotations.IsCreated) _prevRotations.Dispose();
        if (_targetRotations.IsCreated) _targetRotations.Dispose();
        if (_interpolationTimes.IsCreated) _interpolationTimes.Dispose();

        if (_outPositions.IsCreated) _outPositions.Dispose();
        if (_outScales.IsCreated) _outScales.Dispose();
        if (_outRotations.IsCreated) _outRotations.Dispose();

        if (_prevMuscles.IsCreated) _prevMuscles.Dispose();
        if (_targetMuscles.IsCreated) _targetMuscles.Dispose();
        if (_outMuscles.IsCreated) _outMuscles.Dispose();

        if (euroValuesOutput.IsCreated) euroValuesOutput.Dispose();
        if (positionFilters.IsCreated) positionFilters.Dispose();
        if (derivativeFilters.IsCreated) derivativeFilters.Dispose();
    }

    static void GrowIfNeeded(int requiredPlayers)
    {
        if (requiredPlayers <= _capacity) return;

        int newCapacity = math.max(requiredPlayers, _capacity * 2);

        // Keep old references
        var old_prevPositions = _prevPositions;
        var old_targetPositions = _targetPositions;
        var old_prevScales = _prevScales;
        var old_targetScales = _targetScales;
        var old_prevRotations = _prevRotations;
        var old_targetRotations = _targetRotations;
        var old_interpolationTimes = _interpolationTimes;

        var old_outPositions = _outPositions;
        var old_outScales = _outScales;
        var old_outRotations = _outRotations;

        var old_prevMuscles = _prevMuscles;
        var old_targetMuscles = _targetMuscles;
        var old_outMuscles = _outMuscles;

        var old_euroValuesOutput = euroValuesOutput;
        var old_positionFilters = positionFilters;
        var old_derivativeFilters = derivativeFilters;

        // Allocate new
        AllocateAll(newCapacity);

        // Copy transform data
        NativeArray<float3>.Copy(old_prevPositions, _prevPositions, old_prevPositions.Length);
        NativeArray<float3>.Copy(old_targetPositions, _targetPositions, old_targetPositions.Length);
        NativeArray<float3>.Copy(old_prevScales, _prevScales, old_prevScales.Length);
        NativeArray<float3>.Copy(old_targetScales, _targetScales, old_targetScales.Length);
        NativeArray<quaternion>.Copy(old_prevRotations, _prevRotations, old_prevRotations.Length);
        NativeArray<quaternion>.Copy(old_targetRotations, _targetRotations, old_targetRotations.Length);
        NativeArray<float>.Copy(old_interpolationTimes, _interpolationTimes, old_interpolationTimes.Length);

        NativeArray<float3>.Copy(old_outPositions, _outPositions, old_outPositions.Length);
        NativeArray<float3>.Copy(old_outScales, _outScales, old_outScales.Length);
        NativeArray<quaternion>.Copy(old_outRotations, _outRotations, old_outRotations.Length);

        // Copy muscles
        NativeArray<float>.Copy(old_prevMuscles, _prevMuscles, old_prevMuscles.Length);
        NativeArray<float>.Copy(old_targetMuscles, _targetMuscles, old_targetMuscles.Length);
        NativeArray<float>.Copy(old_outMuscles, _outMuscles, old_outMuscles.Length);

        // Copy Euro filter buffers
        NativeArray<float>.Copy(old_euroValuesOutput, euroValuesOutput, old_euroValuesOutput.Length);
        NativeArray<float2>.Copy(old_positionFilters, positionFilters, old_positionFilters.Length);
        NativeArray<float2>.Copy(old_derivativeFilters, derivativeFilters, old_derivativeFilters.Length);

        // Dispose old
        old_prevPositions.Dispose();
        old_targetPositions.Dispose();
        old_prevScales.Dispose();
        old_targetScales.Dispose();
        old_prevRotations.Dispose();
        old_targetRotations.Dispose();
        old_interpolationTimes.Dispose();

        old_outPositions.Dispose();
        old_outScales.Dispose();
        old_outRotations.Dispose();

        old_prevMuscles.Dispose();
        old_targetMuscles.Dispose();
        old_outMuscles.Dispose();

        old_euroValuesOutput.Dispose();
        old_positionFilters.Dispose();
        old_derivativeFilters.Dispose();

        _capacity = newCapacity;
    }

    static void ApplyPendingAddsAndRemoves()
    {
        // Removals first
        if (_pendingRemove.Count > 0)
        {
            for (int i = 0; i < _pendingRemove.Count; i++)
            {
                var p = _pendingRemove[i];
                if (!_indexMap.TryGetValue(p, out var idx)) continue;

                // Clear data
                _prevPositions[idx] = default;
                _targetPositions[idx] = default;
                _prevScales[idx] = new float3(1, 1, 1);
                _targetScales[idx] = new float3(1, 1, 1);
                _prevRotations[idx] = quaternion.identity;
                _targetRotations[idx] = quaternion.identity;
                _interpolationTimes[idx] = 0f;

                int baseOffset = idx * _muscleCount;
                for (int m = 0; m < _muscleCount; m++)
                {
                    int flat = baseOffset + m;
                    _prevMuscles[flat] = 0f;
                    _targetMuscles[flat] = 0f;
                    _outMuscles[flat] = 0f;

                    // Reset Euro filter state
                    euroValuesOutput[flat] = 0f;
                    positionFilters[flat] = float2.zero;
                    derivativeFilters[flat] = float2.zero;
                }

                _indexMap.Remove(p);
                _activePlayers.Remove(p);
                _freeIndices.Push(idx);
            }
            _pendingRemove.Clear();
        }

        // Adds next
        if (_pendingAdd.Count > 0)
        {
            int requiredPlayers = _activePlayers.Count + _pendingAdd.Count;
            GrowIfNeeded(requiredPlayers);

            for (int Index = 0; Index < _pendingAdd.Count; Index++)
            {
                var p = _pendingAdd[Index];
                if (_indexMap.ContainsKey(p)) continue;

                int idx = _freeIndices.Count > 0 ? _freeIndices.Pop() : _activePlayers.Count;

                if (idx == _activePlayers.Count)
                {
                    _activePlayers.Add(p);
                }
                else
                {
                    while (_activePlayers.Count <= idx) _activePlayers.Add(null);
                    _activePlayers[idx] = p;
                }

                _indexMap[p] = idx;

                // Seed defaults
                _prevScales[idx] = new float3(1, 1, 1);
                _targetScales[idx] = new float3(1, 1, 1);
                _prevRotations[idx] = quaternion.identity;
                _targetRotations[idx] = quaternion.identity;
                _interpolationTimes[idx] = 0f;

                int baseOffset = idx * _muscleCount;
                for (int m = 0; m < _muscleCount; m++)
                {
                    int flat = baseOffset + m;
                    _prevMuscles[flat] = 0f;
                    _targetMuscles[flat] = 0f;
                    _outMuscles[flat] = 0f;

                    // Initialize Euro filter state
                    euroValuesOutput[flat] = 0f;
                    positionFilters[flat] = float2.zero;
                    derivativeFilters[flat] = float2.zero;
                }
            }

            _pendingAdd.Clear();
        }
    }
}
