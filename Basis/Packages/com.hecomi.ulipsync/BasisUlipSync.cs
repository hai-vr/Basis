using System;
using System.Collections.Generic;
using System.Threading;
using uLipSync;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

[System.Serializable]
public unsafe class BasisUlipSync
{
    public static Profile profile;

    JobHandle _jobHandle;
    bool _allocated;

    // Double-buffered audio ring
    NativeArray<float> _inputA, _inputB;
    volatile int _activeInputBuffer;
    volatile int _writeIndexA, _writeIndexB;
    volatile int _isDataReceived;

    // MFCC + scoring buffers
    NativeArray<float> _mfcc;
    NativeArray<float> _mfccForOther;

    NativeArray<float> _means;
    NativeArray<float> _standardDeviations;

    NativeArray<float> _invStd;         // 1/(std+eps) precomputed
    NativeArray<float> _phonemes;       // raw packed
    NativeArray<float> _phonemesZ;      // standardized packed
    NativeArray<float> _phonemeNorms;   // per-phoneme norm for cosine (optional)

    NativeArray<float> _scores;                  // phoneme scores output from BasisLipSyncJob
    NativeArray<BasisLipSyncJob.Info> _info;     // volume etc output from BasisLipSyncJob

    public int phonemeCount;
    public int outputSampleRate;
    public int PhonemesCount;
    public int mfccsCount;

    public int CachedInputSampleCount;

    // Debug
    public float globalMultiplier;
    public float MultipliedWeight;
    public float finalWeight;

    public SkinnedMeshRenderer skinnedMeshRenderer;
    public int Count;
    public Mesh sharedMesh;

    public List<BlendShapeInfo> CachedblendShapes = new List<BlendShapeInfo>();
    public BlendShapeInfo[] BlendShapeInfos;

    public const float smoothness = 0.05f;
    public const float minVolume = -2.5f;
    public const float maxVolume = -1.5f;
    public const float VolumeDifference = maxVolume - minVolume;

    const float epsilon = 1e-6f;
    const float Z_EPS = 1e-12f;

    public bool HasJob;

    // Workspace + plans
    public BasisLipSyncWorkspace ws;
    BasisMelFilterPlan _melPlan;
    BasisDctPlan _dctPlan;

    // Main-thread write throttle (still needed because SetBlendShapeWeight is main thread)
    float[] _lastApplied; // [meshBlendShapeCount]
    const float BlendshapeWriteEps = 0.25f; // tweak

    // If you keep job normalization off (recommended for speed), but want _phonemeRatios normalized:
    // set this to true. Otherwise it just copies scores as-is.
    public bool NormalizeRatiosInApply = false;

    // -------- NEW: native blendshape mapping + state + output --------
    public struct BlendMap
    {
        public int blendShapeIndex; // index in mesh
        public int phonemeIndex;    // index in scores
    }

    NativeArray<BlendMap> _blendMap;        // length = BlendShapeInfos.Length
    NativeArray<float> _bsWeight;           // smoothed weight per entry
    NativeArray<float> _bsVelocity;         // reserved (not used in exp smoothing, but kept for future)
    NativeArray<float> _finalByBlendShape;  // length = meshBlendShapeCount
    NativeArray<float> _volState;           // length 2: [0]=volume, [1]=velocity (vel reserved)

    // NEW: only-driven blendshape indices (unique) so Apply() does NOT loop the whole mesh
    NativeArray<int> _drivenBlendShapes;    // unique blendshape indices we actually set

    // ---------------------------------------------------------------
    // Jobs
    // ---------------------------------------------------------------

    // Job that converts scores + volume into final per-blendshape weights.
    // This moves almost everything out of Apply().
    [BurstCompile]
    public struct BasisBlendshapeApplyJob : IJob
    {
        [ReadOnly] public NativeArray<float> scores;                 // phoneme scores
        [ReadOnly] public NativeArray<BlendMap> map;                 // mapping entries
        [ReadOnly] public NativeArray<BasisLipSyncJob.Info> info;    // for volume

        public NativeArray<float> bsWeight;      // persistent smoothing state
        public NativeArray<float> bsVelocity;    // reserved
        public NativeArray<float> volState;      // [0]=volume, [1]=vel reserved

        public NativeArray<float> finalByBlendShape; // output by blendshape index

        public int phonemeCount;
        public float dt;
        public float smoothness;
        public float minVolume;
        public float maxVolume;

        public void Execute()
        {
            // Clear full output. If you have huge blendshape counts and few driven shapes,
            // the next step would be to clear only used indices, but full clear is simple + safe.
            for (int i = 0; i < finalByBlendShape.Length; i++)
                finalByBlendShape[i] = 0f;

            float rawVolume = 0f;
            if (info.IsCreated && info.Length > 0)
                rawVolume = math.max(info[0].volume, 0f);

            // ---- Normalize + smooth volume (exp smoothing) ----
            float normVol = 0f;
            if (rawVolume > 0f)
            {
                float logv = math.log10(rawVolume);
                float denom = math.max((maxVolume - minVolume), 1e-4f);
                normVol = math.saturate((logv - minVolume) / denom);
            }

            float volume = volState[0];

            // Treat smoothness like a time constant (tau). Smaller = snappier.
            float tau = math.max(smoothness, 1e-4f);
            float a = 1f - math.exp(-dt / tau); // stable for varying dt
            volume = math.lerp(volume, normVol, a);
            volState[0] = volume;

            float globalMultiplier = volume * 100f;

            // ---- Smooth weights + sum ----
            float total = 0f;

            for (int i = 0; i < map.Length; i++)
            {
                int p = map[i].phonemeIndex;
                float target = ((uint)p < (uint)phonemeCount) ? scores[p] : 0f;

                float w = bsWeight[i];
                w = math.lerp(w, target, a);
                bsWeight[i] = w;

                total += w;
            }

            float baseMultiply = (math.abs(total) > 1e-6f) ? (globalMultiplier / total) : globalMultiplier;

            // ---- Write final weights by blendshape index ----
            for (int i = 0; i < map.Length; i++)
            {
                int bsIndex = map[i].blendShapeIndex;
                if ((uint)bsIndex >= (uint)finalByBlendShape.Length) continue;

                float fw = bsWeight[i] * baseMultiply;
                fw = math.clamp(fw, 0f, 100f);
                if (!math.isfinite(fw)) fw = 0f;

                finalByBlendShape[bsIndex] = fw;
            }
        }
    }
    public void Simulate(float DeltaTime)
    {
        if (Interlocked.Exchange(ref _isDataReceived, 0) != 1) return;
        if (!_allocated) return;

        int oldActive = _activeInputBuffer;
        int newActive = oldActive ^ 1;

        // Swap first so audio thread writes elsewhere
        Volatile.Write(ref _activeInputBuffer, newActive);

        // Read frozen write index
        int frozenStartIndex = oldActive == 0
            ? Volatile.Read(ref _writeIndexA)
            : Volatile.Read(ref _writeIndexB);

        NativeArray<float> frozenInput = oldActive == 0 ? _inputA : _inputB;

        // Normalize scores in job? (0 = skip, 1 = normalize)
        byte normalizeScores = NormalizeRatiosInApply ? (byte)0 : (byte)1; // if we normalize in Apply, skip in job

        var scoreJob = new BasisLipSyncJob
        {
            input = frozenInput,
            startIndex = frozenStartIndex,

            outputSampleRate = outputSampleRate,
            targetSampleRate = profile.targetSampleRate,

            means = _means,
            standardDeviations = _standardDeviations,
            invStd = _invStd,
            phonemesZ = _phonemesZ,
            phonemeNorms = _phonemeNorms,
            compareMethod = profile.compareMethod,

            mfcc = _mfcc,
            scores = _scores,
            info = _info,

            restPhonemeIndex = 0,

            ws = ws,
            melPlan = _melPlan,
            dctPlan = _dctPlan,

            normalizeScores = normalizeScores,
        };

        JobHandle h0 = scoreJob.Schedule();

        // Chain the blendshape apply job right after scoring job (all Burst)
        // Capture dt on main thread; jobs cannot read Time.deltaTime.
        if (_finalByBlendShape.IsCreated &&
            _blendMap.IsCreated && _blendMap.Length > 0 &&
            _bsWeight.IsCreated && _volState.IsCreated &&
            _info.IsCreated)
        {
            var applyJob = new BasisBlendshapeApplyJob
            {
                scores = _scores,
                map = _blendMap,
                info = _info,

                bsWeight = _bsWeight,
                bsVelocity = _bsVelocity,
                volState = _volState,

                finalByBlendShape = _finalByBlendShape,

                phonemeCount = phonemeCount,
                dt = DeltaTime,
                smoothness = smoothness,
                minVolume = minVolume,
                maxVolume = maxVolume,
            };

            _jobHandle = applyJob.Schedule(h0);
        }
        else
        {
            _jobHandle = h0;
        }

        HasJob = true;
    }

    public void Apply()
    {
        if (!HasJob)
        {
            return;
        }

        HasJob = false;

        _jobHandle.Complete();

        // keep this if another system reads MFCCs
        if (_mfccForOther.IsCreated && _mfcc.IsCreated)
        {
            _mfccForOther.CopyFrom(_mfcc);
        }
        if (!_finalByBlendShape.IsCreated || _finalByBlendShape.Length != Count)
        {
            return;
        }
        if (_lastApplied == null || _lastApplied.Length != Count)
        {
            _lastApplied = new float[Count];
        }
        if (!_drivenBlendShapes.IsCreated || _drivenBlendShapes.Length == 0)
        {
            return;
        }
        int length = _drivenBlendShapes.Length;

        for (int Index = 0; Index < length; Index++)
        {
            int bsIndex = _drivenBlendShapes[Index];
            if ((uint)bsIndex >= (uint)Count)
            {
                continue;
            }

            float fw = _finalByBlendShape[bsIndex];
            float prev = _lastApplied[bsIndex];
            float d = fw - prev;

            if (d == 0f)
            {
                continue;
            }

            if (d * d > BlendshapeWriteEps * BlendshapeWriteEps)
            {
                skinnedMeshRenderer.SetBlendShapeWeight(bsIndex, fw);
                _lastApplied[bsIndex] = fw;
            }
        }
    }
    public void Initalize()
    {
        if (profile == null)
        {
            DisposeBuffers();
            _allocated = false;
            return;
        }

        if (_allocated) DisposeBuffers();

        if (!_jobHandle.Equals(default(JobHandle)))
        {
            _jobHandle.Complete();
            _jobHandle = default;
        }

        _allocated = true;

        outputSampleRate = AudioSettings.outputSampleRate;
        float r = (float)outputSampleRate / math.max(profile.targetSampleRate, 1);
        CachedInputSampleCount = Mathf.CeilToInt(math.max(profile.sampleCount, 1) * r);

        int Count = profile.mfccs.Count;
        phonemeCount = math.max(Count, 1);
        mfccsCount = Count;
        int mfccLen = math.max(profile.mfccNum, 1);
        PhonemesCount = mfccLen * phonemeCount;

        SafeCreate(ref _inputA, CachedInputSampleCount, NativeArrayOptions.UninitializedMemory);
        SafeCreate(ref _inputB, CachedInputSampleCount, NativeArrayOptions.UninitializedMemory);
        _activeInputBuffer = 0;
        _writeIndexA = 0;
        _writeIndexB = 0;
        _isDataReceived = 0;

        SafeCreate(ref _mfcc, mfccLen);
        SafeCreate(ref _mfccForOther, mfccLen);

        SafeCreate(ref _means, mfccLen);
        SafeCreate(ref _standardDeviations, mfccLen);
        SafeCreate(ref _invStd, mfccLen);

        SafeCreate(ref _scores, phonemeCount);
        SafeCreate(ref _phonemes, PhonemesCount);
        SafeCreate(ref _phonemesZ, PhonemesCount);
        SafeCreate(ref _phonemeNorms, phonemeCount);

        SafeCreate(ref _info, 1);

        // Pack raw phonemes
        int write = 0;
        int max = math.min(mfccsCount, phonemeCount);
        for (int p = 0; p < max && write < PhonemesCount; p++)
        {
            var src = profile.mfccs[p].mfccNativeArray;
            int remaining = PhonemesCount - write;
            int len = math.min(mfccLen, remaining);
            NativeArray<float>.Copy(src, 0, _phonemes, write, len);
            write += len;
        }

        // Means
        var meansArr = profile.means;
        if (meansArr != null && _means.IsCreated)
        {
            int dstLen = _means.Length;
            int len = math.min(meansArr.Length, dstLen);
            NativeArray<float>.Copy(meansArr, 0, _means, 0, len);
            for (int i = len; i < dstLen; i++) _means[i] = 0f;
        }

        // Std
        var stdArr = profile.standardDeviation;
        if (stdArr != null && _standardDeviations.IsCreated)
        {
            int dstLen = _standardDeviations.Length;
            int len = math.min(stdArr.Length, dstLen);
            NativeArray<float>.Copy(stdArr, 0, _standardDeviations, 0, len);
            for (int i = len; i < dstLen; i++) _standardDeviations[i] = 1f;
        }

        // invStd once
        PrecomputeInvStd(_standardDeviations, _invStd);

        // precompute standardized phonemesZ
        PrecomputePhonemesZ(_phonemes, _phonemesZ, _means, _invStd, mfccLen, phonemeCount);

        // precompute phoneme norms for cosine
        PrecomputePhonemeNorms(_phonemesZ, _phonemeNorms, mfccLen, phonemeCount);

        // Pre-map phoneme name -> index once
        if (BlendShapeInfos != null)
        {
            Dictionary<string, int> map = new Dictionary<string, int>(32);
            for (int i = 0; i < phonemeCount; i++)
            {
                var name = profile.GetPhoneme(i);
                if (!string.IsNullOrEmpty(name)) map[name] = i;
            }

            for (int i = 0; i < BlendShapeInfos.Length; i++)
            {
                var bs = BlendShapeInfos[i];
                bs.phonemeIndex = (!string.IsNullOrEmpty(bs.phoneme) && map.TryGetValue(bs.phoneme, out int idx)) ? idx : -1;
                BlendShapeInfos[i] = bs;
            }
        }

        int targetRate = math.max(profile.targetSampleRate, 1);
        int melDiv = math.max(profile.melFilterBankChannels, 1);

        ws = BasisLipSyncWorkspace.Create(
            inputLen: CachedInputSampleCount,
            outputSampleRate: outputSampleRate,
            targetSampleRate: targetRate,
            melDiv: melDiv,
            mfccLen: mfccLen,
            fftN: 0,
            firRangeHz: 500f,
            allocator: Allocator.Persistent
        );

        _melPlan = BasisMelFilterPlan.Build(
            fftN: ws.frame.Length,
            sampleRate: targetRate,
            melDiv: melDiv,
            alloc: Allocator.Persistent
        );

        _dctPlan = BasisDctPlan.Build(
            melDiv: melDiv,
            mfccLen: mfccLen,
            alloc: Allocator.Persistent
        );

        // Build native blendshape mapping/state/output once
        BuildNativeBlendMapping();
    }

    void BuildNativeBlendMapping()
    {
        // Dispose old mapping/state/output if any
        SafeDispose(ref _blendMap);
        SafeDispose(ref _bsWeight);
        SafeDispose(ref _bsVelocity);
        SafeDispose(ref _finalByBlendShape);
        SafeDispose(ref _volState);

        // NEW
        SafeDispose(ref _drivenBlendShapes);

        int blendInfoCount = (BlendShapeInfos != null) ? BlendShapeInfos.Length : 0;

        if (Count <= 0 || blendInfoCount <= 0) return;

        _blendMap = new NativeArray<BlendMap>(blendInfoCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        _bsWeight = new NativeArray<float>(blendInfoCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _bsVelocity = new NativeArray<float>(blendInfoCount, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        _finalByBlendShape = new NativeArray<float>(Count, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        _volState = new NativeArray<float>(2, Allocator.Persistent, NativeArrayOptions.ClearMemory);

        // Fill mapping entries
        for (int i = 0; i < blendInfoCount; i++)
        {
            var bs = BlendShapeInfos[i];
            _blendMap[i] = new BlendMap
            {
                blendShapeIndex = bs.index,
                phonemeIndex = bs.phonemeIndex
            };
        }
        HashSet<int> driven = new HashSet<int>();

        for (int i = 0; i < blendInfoCount; i++)
        {
            int idx = BlendShapeInfos[i].index;
            if ((uint)idx < (uint)Count)
                driven.Add(idx);
        }

        int drivenCount = driven.Count;
        if (drivenCount > 0)
        {
            _drivenBlendShapes = new NativeArray<int>(drivenCount, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            int w = 0;
            foreach (int idx in driven)
                _drivenBlendShapes[w++] = idx;
        }

        // Reset main-thread cache & throttle counter
        _lastApplied = null;
    }

    [BurstCompile]
    static void PrecomputeInvStd(NativeArray<float> std, NativeArray<float> invStd)
    {
        int n = math.min(std.Length, invStd.Length);
        float* s = (float*)std.GetUnsafeReadOnlyPtr();
        float* inv = (float*)invStd.GetUnsafePtr();
        for (int i = 0; i < n; i++)
            inv[i] = math.rcp(s[i] + Z_EPS);
    }

    [BurstCompile]
    static void PrecomputePhonemesZ(
        NativeArray<float> phonemesRaw,
        NativeArray<float> phonemesZ,
        NativeArray<float> means,
        NativeArray<float> invStd,
        int mfccLen,
        int phonemeCount)
    {
        int total = mfccLen * phonemeCount;
        if (phonemesRaw.Length < total || phonemesZ.Length < total) return;
        if (means.Length < mfccLen || invStd.Length < mfccLen) return;

        float* raw = (float*)phonemesRaw.GetUnsafeReadOnlyPtr();
        float* z = (float*)phonemesZ.GetUnsafePtr();
        float* mu = (float*)means.GetUnsafeReadOnlyPtr();
        float* inv = (float*)invStd.GetUnsafeReadOnlyPtr();

        int vecLimit = mfccLen & ~3;

        for (int p = 0; p < phonemeCount; p++)
        {
            int baseOff = p * mfccLen;

            int i = 0;
            for (; i < vecLimit; i += 4)
            {
                float4 r = *(float4*)(raw + baseOff + i);
                float4 m = *(float4*)(mu + i);
                float4 k = *(float4*)(inv + i);
                *(float4*)(z + baseOff + i) = (r - m) * k;
            }

            for (; i < mfccLen; i++)
                z[baseOff + i] = (raw[baseOff + i] - mu[i]) * inv[i];
        }
    }

    [BurstCompile]
    static void PrecomputePhonemeNorms(NativeArray<float> phonemesZ, NativeArray<float> norms, int mfccLen, int phonemeCount)
    {
        int total = mfccLen * phonemeCount;
        if (phonemesZ.Length < total || norms.Length < phonemeCount) return;

        float* z = (float*)phonemesZ.GetUnsafeReadOnlyPtr();
        float* outN = (float*)norms.GetUnsafePtr();

        int vecLimit = mfccLen & ~3;

        for (int p = 0; p < phonemeCount; p++)
        {
            int baseOff = p * mfccLen;
            float sum = 0f;

            int i = 0;
            for (; i < vecLimit; i += 4)
            {
                float4 v = *(float4*)(z + baseOff + i);
                sum += math.dot(v, v);
            }
            for (; i < mfccLen; i++)
            {
                float v = z[baseOff + i];
                sum += v * v;
            }

            outN[p] = math.sqrt(sum) + Z_EPS;
        }
    }

    public void OnDestroy() => DisposeBuffers();

    void DisposeBuffers()
    {
        _allocated = false;

        if (!_jobHandle.Equals(default(JobHandle)))
        {
            _jobHandle.Complete();
            _jobHandle = default;
        }

        SafeDispose(ref _inputA);
        SafeDispose(ref _inputB);

        SafeDispose(ref _mfcc);
        SafeDispose(ref _mfccForOther);

        SafeDispose(ref _means);
        SafeDispose(ref _standardDeviations);
        SafeDispose(ref _invStd);

        SafeDispose(ref _scores);
        SafeDispose(ref _phonemes);
        SafeDispose(ref _phonemesZ);
        SafeDispose(ref _phonemeNorms);

        SafeDispose(ref _info);

        SafeDispose(ref _blendMap);
        SafeDispose(ref _bsWeight);
        SafeDispose(ref _bsVelocity);
        SafeDispose(ref _finalByBlendShape);
        SafeDispose(ref _volState);

        // NEW
        SafeDispose(ref _drivenBlendShapes);

        if (_melPlan.IsCreated) _melPlan.Dispose();
        if (_dctPlan.IsCreated) _dctPlan.Dispose();
        if (ws.IsCreated) ws.Dispose();
        _lastApplied = null;
    }

    // ---------------------------------------------------------------
    // Audio thread -> ring buffer
    // ---------------------------------------------------------------
    public void OnDataReceived(float[] input, int channels, int length)
    {
        if (!_allocated || input == null || length <= 0) return;

        int cap = CachedInputSampleCount;
        if (cap <= 0) return;

        int ch = math.max(channels, 1);

        int buf = Volatile.Read(ref _activeInputBuffer);
        NativeArray<float> dstArr = (buf == 0) ? _inputA : _inputB;

        float* dst = (float*)NativeArrayUnsafeUtility.GetUnsafePtr(dstArr);

        fixed (float* src = input)
        {
            int w = (buf == 0) ? Volatile.Read(ref _writeIndexA) : Volatile.Read(ref _writeIndexB);

            // Write mono-downmixed (take first channel) in ring
            for (int s = 0; s < length; s += ch)
            {
                dst[w] = src[s];
                w++;
                if (w == cap) w = 0;
            }

            if (buf == 0) Volatile.Write(ref _writeIndexA, w);
            else Volatile.Write(ref _writeIndexB, w);
        }

        Interlocked.Exchange(ref _isDataReceived, 1);
    }

    static void SafeCreate<T>(ref NativeArray<T> array, int length, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        where T : struct
    {
        if (array.IsCreated)
        {
            if (array.Length == length) return;
            array.Dispose();
        }
        array = new NativeArray<T>(length, Allocator.Persistent, options);
    }

    static void SafeDispose<T>(ref NativeArray<T> array) where T : struct
    {
        if (array.IsCreated) array.Dispose();
        array = default;
    }

    public void AddBlendShape(string phoneme, int blendShape)
    {
        var bs = CachedblendShapes.Find(info => info.phoneme == phoneme);
        if (bs == null)
        {
            bs = new BlendShapeInfo { phoneme = phoneme };
            CachedblendShapes.Add(bs);
        }
        if (skinnedMeshRenderer != null) bs.index = blendShape;
    }
}
