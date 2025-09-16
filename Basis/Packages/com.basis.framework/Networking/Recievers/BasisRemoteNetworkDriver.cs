using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine.Jobs;

public static partial class BasisRemoteNetworkDriver
{
    /// <summary>Fixed capacity for this driver instance.</summary>
    public const int FixedCapacity = 1024;

    /// <summary>Returns the number of active indices (max index that has been written + 1).</summary>
    public static int ActivePlayerCount => _activeCount;

    /// <summary>Muscle count used by the driver.</summary>
    public static int MuscleCount => _muscleCount;

    // ---------------- sanitation knobs (tune as needed) ----------------
    public const float MinScaleComponent = 1e-3f;    // no zeros/tiny scales
    public const float MaxScaleComponent = 1e2f;     // avoid runaway giants
    public const float MaxPositionComponent = 1e5f;  // world-space clamp

    public const float MinHumanScale = 0.01f;
    public const float MaxHumanScale = 150;

    // Interp t is [0,1]; filter dt is clamped to sensible ranges
    public const float MinDt = 1e-4f;   // 0.1ms
    public const float MaxDt = 0.25f;   // <= 4 Hz

    public const float MuscleMin = -180f;
    public const float MuscleMax = 180f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float SanitizeFloat(float v, float def, float min, float max)
    {
        return math.isfinite(v) ? math.clamp(v, min, max) : def;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float3 SanitizePosition(float3 p)
    {
        float x = math.isfinite(p.x) ? math.clamp(p.x, -MaxPositionComponent, MaxPositionComponent) : 0f;
        float y = math.isfinite(p.y) ? math.clamp(p.y, -MaxPositionComponent, MaxPositionComponent) : 0f;
        float z = math.isfinite(p.z) ? math.clamp(p.z, -MaxPositionComponent, MaxPositionComponent) : 0f;
        return new float3(x, y, z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static float3 SanitizeScale(float3 s)
    {
        // Force positive, kill NaNs/Infs, clamp magnitude
        float x = SanitizeFloat(math.abs(s.x), 1f, MinScaleComponent, MaxScaleComponent);
        float y = SanitizeFloat(math.abs(s.y), 1f, MinScaleComponent, MaxScaleComponent);
        float z = SanitizeFloat(math.abs(s.z), 1f, MinScaleComponent, MaxScaleComponent);
        return new float3(x, y, z);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    static quaternion SanitizeRotation(quaternion q)
    {
        float4 v = q.value;
        if (!math.all(math.isfinite(v))) return quaternion.identity;
        float len2 = math.lengthsq(v);
        return (len2 > 1e-8f) ? math.normalize(q) : quaternion.identity;
    }

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

    // Per-avatar animator human scale and precomputed body position (scaled)
    static NativeArray<float> _humanScales;          // Player.BasisAvatar.AnimatorHumanScale
    static NativeArray<float3> _scaledBodyPositions; // = ApplyingPosition * SafeDivide(humanScale, ApplyingScale)

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

    public static JobHandle oneEuroJob; // final frame fence (combined deps)

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

            _humanScales[i] = 1f;
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

        // --- NO SANITIZATION HERE ANYMORE: write raw values ---
        _humanScales[index] = humanScale;

        _prevPositions[index] = prevPos;
        _targetPositions[index] = targetPos;

        _prevScales[index] = prevScale;
        _targetScales[index] = targetScale;

        _prevRotations[index] = prevRot;
        _targetRotations[index] = targetRot;

        // raw t as well; clamped later on threads
        _interpolationTimes[index] = interpolationTime;

        // Flattened write: [index * MuscleCount .. (index+1) * MuscleCount)
        int baseOffset = index * _muscleCount;
        FastCopyMuscles(prevMuscles, 0, _prevMuscles, baseOffset, _muscleCount);
        FastCopyMuscles(targetMuscles, 0, _targetMuscles, baseOffset, _muscleCount);

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

        if (!oneEuroJob.IsCompleted) oneEuroJob.Complete();

        _prevPositions[index] = default;
        _targetPositions[index] = default;
        _prevScales[index] = new float3(1, 1, 1);
        _targetScales[index] = new float3(1, 1, 1);
        _prevRotations[index] = quaternion.identity;
        _targetRotations[index] = quaternion.identity;
        _interpolationTimes[index] = 0f;

        _humanScales[index] = 1f;
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

        // 0) sanitize all raw inputs on threads
        var sanitizeInputsJob = new SanitizeAvatarInputsJob
        {
            PrevPositions = _prevPositions,
            TargetPositions = _targetPositions,
            PrevScales = _prevScales,
            TargetScales = _targetScales,
            PrevRotations = _prevRotations,
            TargetRotations = _targetRotations,
            InterpolationTimes = _interpolationTimes,
            HumanScales = _humanScales
        }.Schedule(num, 128);

        // 1) transforms (depends on sanitized inputs)
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
        }.Schedule(num, 128, sanitizeInputsJob);

        // 2) precompute scaled body (after transforms)
        var scaledBodyJob = new ComputeScaledBodyJob
        {
            OutputPositions = _outPositions,
            OutputScales = _outScales,
            HumanScales = _humanScales,
            ScaledBodyPositions = _scaledBodyPositions
        }.Schedule(num, 128, avatarJob);

        // 3) sanitize raw muscle buffers (still on threads) — depends on sanitized t
        int flatChannels = num * _muscleCount;
        var sanitizeMuscleInputs = new SanitizeMuscleBuffersJob
        {
            A = _prevMuscles,
            B = _targetMuscles
        }.Schedule(flatChannels, 128, sanitizeInputsJob);

        // 4) interpolate muscles (needs sanitized t + sanitized muscles)
        JobHandle musclesJob = new UpdateAllAvatarMusclesJob
        {
            PreviousMuscles = _prevMuscles,
            TargetMuscles = _targetMuscles,
            InterpolationTimes = _interpolationTimes,
            OutputMuscles = _outMuscles,
            MuscleCountPerAvatar = _muscleCount
        }.Schedule(flatChannels, 128, sanitizeMuscleInputs);

        // 5) filter and clamp
        JobHandle euroJobHandle = new BasisOneEuroFilterParallelJob
        {
            InputValues = _outMuscles,
            OutputValues = euroValuesOutput,
            DeltaTime = _interpolationTimes,
            MinCutoff = MinCutoff,
            Beta = Beta,
            DerivativeCutoff = DerivativeCutoff,
            PositionFilters = positionFilters,
            DerivativeFilters = derivativeFilters,
            MuscleCountPerAvatar = _muscleCount
        }.Schedule(flatChannels, 128, musclesJob);

        var clampFiltered = new SanitizeSingleBufferJob
        {
            Buffer = euroValuesOutput
        }.Schedule(flatChannels, 128, euroJobHandle);

        // 6) single fence
        oneEuroJob = JobHandle.CombineDependencies(clampFiltered, scaledBodyJob);
    }
    [BurstCompile]
    public struct SanitizeAvatarInputsJob : IJobParallelFor
    {
        public NativeArray<float3> PrevPositions;
        public NativeArray<float3> TargetPositions;
        public NativeArray<float3> PrevScales;
        public NativeArray<float3> TargetScales;
        public NativeArray<quaternion> PrevRotations;
        public NativeArray<quaternion> TargetRotations;
        public NativeArray<float> InterpolationTimes;
        public NativeArray<float> HumanScales;

        public void Execute(int index)
        {
            HumanScales[index] = SanitizeFloat(HumanScales[index], 1f, MinHumanScale, MaxHumanScale);
            PrevPositions[index] = SanitizePosition(PrevPositions[index]);
            TargetPositions[index] = SanitizePosition(TargetPositions[index]);
            PrevScales[index] = SanitizeScale(PrevScales[index]);
            TargetScales[index] = SanitizeScale(TargetScales[index]);
            PrevRotations[index] = SanitizeRotation(PrevRotations[index]);
            TargetRotations[index] = SanitizeRotation(TargetRotations[index]);
            InterpolationTimes[index] = SanitizeFloat(InterpolationTimes[index], 0f, 0f, 1f);
        }
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
            // Kill NaNs/Infs and clamp to sane [MinDt, MaxDt]
            if (!math.isfinite(dt)) dt = 1f / 90f;
            dt = math.clamp(dt, MinDt, MaxDt);

            float frequency = 1.0f / dt;

            float inputValue = InputValues[index];
            inputValue = math.isfinite(inputValue) ? inputValue : 0f;

            float prevFiltered = PositionFilters[index].y;
            prevFiltered = math.isfinite(prevFiltered) ? prevFiltered : 0f;

            float prevRaw = PositionFilters[index].x;
            prevRaw = math.isfinite(prevRaw) ? prevRaw : 0f;

            float dValue = (inputValue - prevRaw) * frequency;
            dValue = math.isfinite(dValue) ? dValue : 0f;

            float alphaD = Alpha(DerivativeCutoff, frequency);
            float prevDerivFiltered = DerivativeFilters[index].y;
            prevDerivFiltered = math.isfinite(prevDerivFiltered) ? prevDerivFiltered : 0f;

            float edValue = alphaD * dValue + (1.0f - alphaD) * prevDerivFiltered;

            float cutoff = MinCutoff + Beta * math.abs(edValue);
            cutoff = math.max(cutoff, 1e-4f);

            float alphaX = Alpha(cutoff, frequency);
            float filtered = alphaX * inputValue + (1.0f - alphaX) * prevFiltered;

            // final clamp to muscle bounds
            filtered = math.isfinite(filtered) ? math.clamp(filtered, MuscleMin, MuscleMax) : 0f;

            OutputValues[index] = filtered;

            PositionFilters[index] = new float2(inputValue, filtered);
            DerivativeFilters[index] = new float2(dValue, edValue);
        }

        private static float Alpha(float cutoff, float frequency)
        {
            float te = 1.0f / frequency;
            float c = math.max(cutoff, 1e-4f);
            float tau = 1.0f / (2.0f * math.PI * c);
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
            float t = math.saturate(InterpolationTimes[index]);

            float3 p0 = PreviousPositions[index];
            float3 p1 = TargetPositions[index];
            float3 s0 = PreviousScales[index];
            float3 s1 = TargetScales[index];

            float3 outP = math.lerp(p0, p1, t);
            float3 outS = math.lerp(s0, s1, t);
            quaternion outR = math.slerp(PreviousRotations[index], TargetRotations[index], t);

            // sanitize outputs
            OutputPositions[index] = new float3(
                math.isfinite(outP.x) ? math.clamp(outP.x, -MaxPositionComponent, MaxPositionComponent) : 0f,
                math.isfinite(outP.y) ? math.clamp(outP.y, -MaxPositionComponent, MaxPositionComponent) : 0f,
                math.isfinite(outP.z) ? math.clamp(outP.z, -MaxPositionComponent, MaxPositionComponent) : 0f
            );

            float3 s = outS;
            s = new float3(
                math.isfinite(s.x) ? math.clamp(math.abs(s.x), MinScaleComponent, MaxScaleComponent) : 1f,
                math.isfinite(s.y) ? math.clamp(math.abs(s.y), MinScaleComponent, MaxScaleComponent) : 1f,
                math.isfinite(s.z) ? math.clamp(math.abs(s.z), MinScaleComponent, MaxScaleComponent) : 1f
            );


            OutputScales[index] = s;

            float4 rv = outR.value;
            quaternion cleanR = (!math.all(math.isfinite(rv)) || math.lengthsq(rv) <= 1e-8f) ? quaternion.identity : math.normalize(outR);
            OutputRotations[index] = cleanR;
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
            int playerIndex = MuscleCountPerAvatar > 0 ? (index / MuscleCountPerAvatar) : 0;
            float t = math.saturate(InterpolationTimes[playerIndex]);

            float v = math.lerp(PreviousMuscles[index], TargetMuscles[index], t);
            v = math.isfinite(v) ? math.clamp(v, MuscleMin, MuscleMax) : 0f;

            OutputMuscles[index] = v;
        }
    }

    /// <summary>Guarded divide + scaled body position (Burst).</summary>
    [BurstCompile]
    public struct ComputeScaledBodyJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> OutputPositions; // from UpdateAllAvatarsJob
        [ReadOnly] public NativeArray<float3> OutputScales;    // from UpdateAllAvatarsJob
        [ReadOnly] public NativeArray<float> HumanScales;      // per avatar

        [WriteOnly] public NativeArray<float3> ScaledBodyPositions;

        public void Execute(int i)
        {
            const float eps = 1e-6f;

            float3 applyScale = OutputScales[i];

            // Sanitize baseScale: avoid 0 / NaN / Inf before reciprocal
            float baseScale = HumanScales[i];
            bool baseBad = !math.isfinite(baseScale) | (math.abs(baseScale) <= eps);
            float invBase = math.select(math.rcp(baseScale), 1f, baseBad);

            float3 scale = new float3(invBase);

            bool3 validApply = math.isfinite(applyScale) & (math.abs(applyScale) > eps);

            float3 safeDiv = math.select(scale, scale / applyScale, validApply);

            float3 scaled = OutputPositions[i] * safeDiv;

            // final clamp to world bounds
            ScaledBodyPositions[i] = math.clamp(scaled,
                new float3(-MaxPositionComponent),
                new float3(MaxPositionComponent));
        }
    }

    /// <summary>Completes all scheduled work for this frame.</summary>
    public static void Apply()
    {
        if (!_initialized) return;
        oneEuroJob.Complete();
    }
    /* must be length == _muscleCount */
    /// <summary>Read back the computed outputs for an index after Apply().</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool GetOutputs_NoAlloc(int index,out float3 outPos,out quaternion outRot, out float3 BodyPosition,float[] outMuscles)
    {
        //outScale = default;
        outPos = default;  outRot = default; BodyPosition = default;
        if ((uint)index >= FixedCapacity) return false;
        if (outMuscles == null || outMuscles.Length != _muscleCount) return false;

        outPos = _outPositions[index];
      //  outScale = _outScales[index];
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
    /// Update the One Euro filter parameters and optionally reset filter state.
    /// </summary>
    public static void UpdateOneEuroParameters(float minCutoff, float beta, float derivativeCutoff, bool resetState = true)
    {
        if (!oneEuroJob.IsCompleted) oneEuroJob.Complete();

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

    // ---------------- sanitizing jobs ----------------

    [BurstCompile]
    public struct SanitizeMuscleBuffersJob : IJobParallelFor
    {
        public NativeArray<float> A; // prev
        public NativeArray<float> B; // target

        public void Execute(int i)
        {
            float a = A[i];
            float b = B[i];
            a = math.isfinite(a) ? math.clamp(a, MuscleMin, MuscleMax) : 0f;
            b = math.isfinite(b) ? math.clamp(b, MuscleMin, MuscleMax) : 0f;
            A[i] = a;
            B[i] = b;
        }
    }

    [BurstCompile]
    public struct SanitizeSingleBufferJob : IJobParallelFor
    {
        public NativeArray<float> Buffer;
        public void Execute(int i)
        {
            float v = Buffer[i];
            Buffer[i] = math.isfinite(v) ? math.clamp(v, MuscleMin, MuscleMax) : 0f;
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

        // Human scale + scaled body
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
