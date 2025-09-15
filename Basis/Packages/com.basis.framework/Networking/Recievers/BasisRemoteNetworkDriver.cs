using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Jobs;

public static partial class BasisRemoteNetworkDriver
{
    /// <summary>Fixed capacity for this driver instance.</summary>
    public const int FixedCapacity = 1024;

    /// <summary>Returns the number of active indices (max index that has been written + 1).</summary>
    public static int ActivePlayerCount => _activeCount;

    /// <summary>Muscle count used by the driver.</summary>
    public static int MuscleCount => _muscleCount;

    // Native buffers (size == FixedCapacity, except muscles which is FixedCapacity * _muscleCount)
    static NativeArray<float3> _prevPositions;
    static NativeArray<float3> _targetPositions;
    static NativeArray<float3> _prevScales;
    static NativeArray<float3> _targetScales;
    static NativeArray<quaternion> _prevRotations;
    static NativeArray<quaternion> _targetRotations;
    static NativeArray<float> _interpolationTimes; // per-index dt or interpolation t

    static NativeArray<float3> _outPositions;
    static NativeArray<float3> _outScales;
    static NativeArray<quaternion> _outRotations;

    // New: per-avatar animator human scale and precomputed body position (scaled)
    static NativeArray<float> _humanScales;          // Player.BasisAvatar.AnimatorHumanScale
    static NativeArray<float3> _scaledBodyPositions;  // = ApplyingPosition * SafeDivide(humanScale, ApplyingScale)

    // Muscles (flattened: players * muscles)
    static NativeArray<float> _prevMuscles;   // flattened
    static NativeArray<float> _targetMuscles; // flattened
    static NativeArray<float> _outMuscles;    // flattened (interpolated, before filter)

    // 1€ filter buffers (flattened: players * muscles)
    static NativeArray<float> euroValuesOutput;   // filtered output
    static NativeArray<float2> positionFilters;   // per-channel state
    static NativeArray<float2> derivativeFilters; // per-channel state


    // State
    static int _muscleCount;
    static bool _initialized;
    static int _activeCount; // highest index written + 1
    static Allocator _allocator = Allocator.Persistent;

    public static JobHandle oneEuroJob;         // final frame fence (combined deps)


    /// <summary>Initialize the driver with a fixed capacity of 1024. Must be called before SetInputs/Compute/Apply/GetOutputs.</summary>
    public static void Initialize(int muscleCount, Allocator allocator = Allocator.Persistent)
    {
        if (_initialized) return;

        if (muscleCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(muscleCount));

        _allocator = allocator;
        _muscleCount = muscleCount;
        _activeCount = 0;

        AllocateAll(FixedCapacity);

        // Seed defaults
        for (int i = 0; i < FixedCapacity; i++)
        {
            _prevScales[i] = new float3(1, 1, 1);
            _targetScales[i] = new float3(1, 1, 1);
            _prevRotations[i] = quaternion.identity;
            _targetRotations[i] = quaternion.identity;
            _interpolationTimes[i] = 0f;

            // New: default human scale to 1
            _humanScales[i] = 1;
            _scaledBodyPositions[i] = float3.zero;
        }

        // Seed muscles/filter state
        int flat = FixedCapacity * _muscleCount;
        for (int c = 0; c < flat; c++)
        {
            _prevMuscles[c] = 0f;
            _targetMuscles[c] = 0f;
            _outMuscles[c] = 0f;
            euroValuesOutput[c] = 0f;
            positionFilters[c] = float2.zero;
            derivativeFilters[c] = float2.zero;
        }

        _initialized = true;
    }

    /// <summary>Dispose all native allocations. Call on shutdown/domain unload.</summary>
    public static void Shutdown()
    {
        if (!_initialized) return;

        // Make sure no jobs are still using our arrays
        if (!oneEuroJob.IsCompleted) oneEuroJob.Complete();

        DisposeAll();
        _activeCount = 0;
        _muscleCount = 0;
        _initialized = false;
    }

    /// <summary>Write inputs for a given index (0..FixedCapacity-1) for this frame.</summary>
    public static void SetInputs(
        int index, float humanScale,
        float3 prevPos, float3 targetPos,
        float3 prevScale, float3 targetScale,
        quaternion prevRot, quaternion targetRot,
        float interpolationTime,
        NativeArray<float> prevMuscles,
        NativeArray<float> targetMuscles)
    {
        if ((uint)index >= FixedCapacity)
            throw new IndexOutOfRangeException($"index {index} is out of range [0,{FixedCapacity - 1}]");

        _humanScales[index] = humanScale;
        _prevPositions[index] = prevPos;
        _targetPositions[index] = targetPos;
        _prevScales[index] = prevScale;
        _targetScales[index] = targetScale;
        _prevRotations[index] = prevRot;
        _targetRotations[index] = targetRot;
        _interpolationTimes[index] = interpolationTime;

        // Flattened write: [index * MuscleCount .. (index+1) * MuscleCount)
        int baseOffset = index * _muscleCount;
        FastCopyMuscles(prevMuscles, 0, _prevMuscles, baseOffset, _muscleCount);
        FastCopyMuscles(targetMuscles, 0, _targetMuscles, baseOffset, _muscleCount);

        // Advance active count if needed
        if (index + 1 > _activeCount) _activeCount = index + 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static unsafe void FastCopyMuscles(NativeArray<float> src, int srcStart, NativeArray<float> dst, int dstStart, int count)
    {
        var bytes = (long)count * sizeof(float);
        var srcPtr = (byte*)src.GetUnsafeReadOnlyPtr() + (long)srcStart * sizeof(float);
        var dstPtr = (byte*)dst.GetUnsafePtr() + (long)dstStart * sizeof(float);
        UnsafeUtility.MemCpy(dstPtr, srcPtr, bytes);
    }

    /// <summary>Optional: reset a given index back to defaults (zeros/identity).</summary>
    public static void ResetIndex(int index)
    {
        if ((uint)index >= FixedCapacity)
            throw new IndexOutOfRangeException($"index {index} is out of range [0,{FixedCapacity - 1}]");

        // Make sure no jobs are still reading/writing these buffers
        if (!oneEuroJob.IsCompleted) oneEuroJob.Complete();

        _prevPositions[index] = default;
        _targetPositions[index] = default;
        _prevScales[index] = new float3(1, 1, 1);
        _targetScales[index] = new float3(1, 1, 1);
        _prevRotations[index] = quaternion.identity;
        _targetRotations[index] = quaternion.identity;
        _interpolationTimes[index] = 0f;

        _humanScales[index] =  1;
        _scaledBodyPositions[index] = float3.zero;

        int baseOffset = index * _muscleCount;
        for (int m = 0; m < _muscleCount; m++)
        {
            int flat = baseOffset + m;
            _prevMuscles[flat] = 0f;
            _targetMuscles[flat] = 0f;
            _outMuscles[flat] = 0f;

            euroValuesOutput[flat] = 0f;
            positionFilters[flat] = float2.zero;
            derivativeFilters[flat] = float2.zero;
        }

        // Optionally shrink active count if we cleared the tail
        if (index == _activeCount - 1)
        {
            int newCount = index;
            _activeCount = newCount;
        }
    }

    /// <summary>Run the batched jobs once for the current frame.</summary>
    public static void Compute()
    {
        int num = _activeCount;
        if (num <= 0) return;

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
        }.Schedule(num, 128);

        // Precompute scaled body position with guarded divide (Burst)
        var scaledBodyJob = new ComputeScaledBodyJob
        {
            OutputPositions = _outPositions,
            OutputScales = _outScales,
            HumanScales = _humanScales,
            ScaledBodyPositions = _scaledBodyPositions
        }.Schedule(num, 128, avatarJob);

        // Interpolate muscles across players * muscles (flattened)
        JobHandle musclesJob = new UpdateAllAvatarMusclesJob
        {
            PreviousMuscles = _prevMuscles,
            TargetMuscles = _targetMuscles,
            InterpolationTimes = _interpolationTimes, // index-based per "player"
            OutputMuscles = _outMuscles,
            MuscleCountPerAvatar = _muscleCount
        }.Schedule(num * _muscleCount, 128, avatarJob);

        // 1€ filter: read interpolated muscles, write filtered output
        JobHandle euroJobHandle = new BasisOneEuroFilterParallelJob
        {
            InputValues = _outMuscles,          // raw/interpolated input per (index,muscle)
            OutputValues = euroValuesOutput,    // filtered output
            DeltaTime = _interpolationTimes,    // per-index dt / interpolation t
            MinCutoff = MinCutoff,
            Beta = Beta,
            DerivativeCutoff = DerivativeCutoff,
            PositionFilters = positionFilters,
            DerivativeFilters = derivativeFilters,
            MuscleCountPerAvatar = _muscleCount
        }.Schedule(num * _muscleCount, 128, musclesJob);

        // Combine all deps so Apply() has a single fence
        oneEuroJob = JobHandle.CombineDependencies(euroJobHandle, scaledBodyJob);
    }

    /*
     * BasicOneEuroFilterParallelJob.cs
     * Author: Dario Mazzanti (dario.mazzanti@iit.it), 2016
     *
     * This Unity C# utility is based on the C++ implementation of the OneEuroFilter algorithm by Nicolas Roussel (http://www.lifl.fr/~casiez/1euro/OneEuroFilter.cc)
     * More info on the 1€ filter by Géry Casiez at http://www.lifl.fr/~casiez/1euro/
     *
     */
    [BurstCompile]
    public struct BasisOneEuroFilterParallelJob : IJobParallelFor
    {
        // Input signal (flattened: players * muscles)
        [ReadOnly] public NativeArray<float> InputValues;

        // Output signal (flattened: players * muscles)
        public NativeArray<float> OutputValues;

        // Per-player deltaTime (or sampling period proxy). Length == numPlayers
        [ReadOnly] public NativeArray<float> DeltaTime;

        // Filter state per flattened channel (same length as OutputValues)
        public NativeArray<float2> PositionFilters;   // x = previous input, y = previous output
        public NativeArray<float2> DerivativeFilters; // x = previous derivative input, y = previous derivative output

        // Parameters
        public float MinCutoff;
        public float Beta;
        public float DerivativeCutoff;

        // Stride to recover the player index from the flattened channel index
        // i.e., the number of muscles per avatar
        [ReadOnly] public int MuscleCountPerAvatar;

        public void Execute(int index)
        {
            int playerIndex = MuscleCountPerAvatar > 0 ? (index / MuscleCountPerAvatar) : 0;

            if ((uint)playerIndex >= (uint)DeltaTime.Length) return;
            if ((uint)index >= (uint)InputValues.Length) return;
            if ((uint)index >= (uint)OutputValues.Length) return;
            if ((uint)index >= (uint)PositionFilters.Length) return;
            if ((uint)index >= (uint)DerivativeFilters.Length) return;

            float dt = DeltaTime[playerIndex];
            if (dt <= 0f) dt = 1e-3f; // guard

            float frequency = 1.0f / dt;
            float inputValue = InputValues[index];

            float prevFiltered = PositionFilters[index].y;
            float prevRaw = PositionFilters[index].x;

            float dValue = (inputValue - prevRaw) * frequency;

            float alphaD = Alpha(DerivativeCutoff, frequency);
            float prevDerivFiltered = DerivativeFilters[index].y;
            float edValue = alphaD * dValue + (1.0f - alphaD) * prevDerivFiltered;

            float cutoff = MinCutoff + Beta * Mathf.Abs(edValue);

            float alphaX = Alpha(cutoff, frequency);
            float filtered = alphaX * inputValue + (1.0f - alphaX) * prevFiltered;

            OutputValues[index] = filtered;

            PositionFilters[index] = new float2(inputValue, filtered);
            DerivativeFilters[index] = new float2(dValue, edValue);
        }

        private static float Alpha(float cutoff, float frequency)
        {
            float te = 1.0f / frequency;
            float tau = 1.0f / (2.0f * Mathf.PI * math.max(cutoff, 1e-4f));
            return 1.0f / (1.0f + tau / te);
        }
    }

    [BurstCompile]
    public struct UpdateAllAvatarsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> PreviousPositions;
        [ReadOnly] public NativeArray<float3> TargetPositions;

        [ReadOnly] public NativeArray<float3> PreviousScales;
        [ReadOnly] public NativeArray<float3> TargetScales;

        [ReadOnly] public NativeArray<quaternion> PreviousRotations;
        [ReadOnly] public NativeArray<quaternion> TargetRotations;

        [ReadOnly] public NativeArray<float> InterpolationTimes;

        public NativeArray<float3> OutputPositions;
        public NativeArray<float3> OutputScales;
        public NativeArray<quaternion> OutputRotations;

        public void Execute(int index)
        {
            float t = InterpolationTimes[index];

            OutputPositions[index] = math.lerp(PreviousPositions[index], TargetPositions[index], t);
            OutputScales[index] = math.lerp(PreviousScales[index], TargetScales[index], t);
            OutputRotations[index] = math.slerp(PreviousRotations[index], TargetRotations[index], t);
        }
    }

    [BurstCompile]
    public struct UpdateAllAvatarMusclesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float> PreviousMuscles; // Flattened array
        [ReadOnly] public NativeArray<float> TargetMuscles;
        [ReadOnly] public NativeArray<float> InterpolationTimes;

        public NativeArray<float> OutputMuscles;
        public int MuscleCountPerAvatar;

        public void Execute(int index)
        {
            int playerIndex = index / MuscleCountPerAvatar;
            float t = InterpolationTimes[playerIndex];

            OutputMuscles[index] = math.lerp(
                PreviousMuscles[index],
                TargetMuscles[index],
                t
            );
        }
    }

    /// <summary>Guarded divide + scaled body position (Burst).</summary>
    [BurstCompile]
    public struct ComputeScaledBodyJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> OutputPositions; // from UpdateAllAvatarsJob
        [ReadOnly] public NativeArray<float3> OutputScales;    // from UpdateAllAvatarsJob
        [ReadOnly] public NativeArray<float> HumanScales;     // per avatar

        [WriteOnly] public NativeArray<float3> ScaledBodyPositions;

        public void Execute(int i)
        {
            float3 applyScale = OutputScales[i];
            float baseScale = HumanScales[i];
            float3 Scale = Vector3.one / baseScale;
            // SafeDivide(baseScale, applyScale) component-wise with epsilon guard.
            const float eps = 1e-6f;
            float3 mask = new float3(
                math.abs(applyScale.x) > eps ? 1f : 0f,
                math.abs(applyScale.y) > eps ? 1f : 0f,
                math.abs(applyScale.z) > eps ? 1f : 0f);

            float3 safeDiv = new float3(
                mask.x > 0f ? Scale.x / applyScale.x : Scale.x,
                mask.y > 0f ? Scale.y / applyScale.y : Scale.y,
                mask.z > 0f ? Scale.z / applyScale.z : Scale.z);

            ScaledBodyPositions[i] = OutputPositions[i] * safeDiv;
        }
    }

    /// <summary>Completes all scheduled work for this frame.</summary>
    public static void Apply()
    {
        if (!_initialized) return;
        oneEuroJob.Complete(); // also fences scaledBody + transform jobs via combined deps
    }

    /// <summary>Read back the computed outputs for an index after Apply().</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetOutputs_NoAlloc(
        int index,
        out float3 outPos,
        out float3 outScale,
        out quaternion outRot,
        out float3 BodyPosition,
        float[] outMuscles /* must be length == _muscleCount */
      )
    {
        // minimal guards; no allocations
        outPos = default; outScale = default; outRot = default; BodyPosition = default;
        if ((uint)index >= FixedCapacity) return false;
        if (outMuscles == null || outMuscles.Length != _muscleCount) return false;

        outPos = _outPositions[index];
        outScale = _outScales[index];
        outRot = _outRotations[index];

        int baseOffset = index * _muscleCount;

        unsafe
        {
            // source: NativeArray<float> (contiguous)
            float* src = (float*)euroValuesOutput.GetUnsafeReadOnlyPtr() + baseOffset;

            // dest: managed float[] pinned just for the copy
            fixed (float* dst = outMuscles)
            {
                UnsafeUtility.MemCpy(dst, src, _muscleCount * sizeof(float));
            }
        }
        BodyPosition = _scaledBodyPositions[index];
        return true;
    }

    /// <summary>
    /// Update the One Euro filter parameters on the shared network singleton and (optionally)
    /// reset the filter internal state so it "forgets" previous history and re-converges
    /// from the current inputs.
    /// </summary>
    public static void UpdateOneEuroParameters(float minCutoff, float beta, float derivativeCutoff, bool resetState = true)
    {
        // Ensure no jobs are currently touching the buffers.
        if (!oneEuroJob.IsCompleted) oneEuroJob.Complete();

        // Push values to the source of truth used in Compute()
        MinCutoff = minCutoff;
        Beta = beta;
        DerivativeCutoff = derivativeCutoff;

        if (resetState)
        {
            ResetFilterStateAll();
        }
    }

    // Parameters for Euro filter
    public static float MinCutoff = 0.05f;
    public static float Beta = 0.01f;
    public static float DerivativeCutoff = 1.0f;

    /// <summary>Resets the filter state for ALL avatars/muscles.</summary>
    public static void ResetFilterStateAll()
    {
        if (!oneEuroJob.IsCompleted) oneEuroJob.Complete();

        int flat = FixedCapacity * _muscleCount;
        for (int i = 0; i < flat; i++)
        {
            positionFilters[i] = float2.zero;   // previous raw (x) and filtered (y)
            derivativeFilters[i] = float2.zero; // previous derivative raw (x) and filtered (y)
            euroValuesOutput[i] = 0f;           // clear last filtered output
        }
    }

    /// <summary>Resets the filter state for a single avatar index (0..FixedCapacity-1).</summary>
    public static void ResetFilterStateForIndex(int index)
    {
        if ((uint)index >= FixedCapacity)
            throw new IndexOutOfRangeException($"index {index} is out of range [0,{FixedCapacity - 1}]");

        if (!oneEuroJob.IsCompleted) oneEuroJob.Complete();

        int baseOffset = index * _muscleCount;
        for (int m = 0; m < _muscleCount; m++)
        {
            int flat = baseOffset + m;
            positionFilters[flat] = float2.zero;
            derivativeFilters[flat] = float2.zero;
            euroValuesOutput[flat] = 0f;
        }
    }

    // ---------------- internal allocation helpers ----------------

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

        // New: human scale + scaled body
        _humanScales = new NativeArray<float>(capacity, _allocator, NativeArrayOptions.UninitializedMemory);
        _scaledBodyPositions = new NativeArray<float3>(capacity, _allocator, NativeArrayOptions.UninitializedMemory);

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

        if (_humanScales.IsCreated) _humanScales.Dispose();
        if (_scaledBodyPositions.IsCreated) _scaledBodyPositions.Dispose();

        if (_prevMuscles.IsCreated) _prevMuscles.Dispose();
        if (_targetMuscles.IsCreated) _targetMuscles.Dispose();
        if (_outMuscles.IsCreated) _outMuscles.Dispose();

        if (euroValuesOutput.IsCreated) euroValuesOutput.Dispose();
        if (positionFilters.IsCreated) positionFilters.Dispose();
        if (derivativeFilters.IsCreated) derivativeFilters.Dispose();
    }
}
