// =======================================================
// Optimized BasisUlipSync changes:
// - Precompute standardized phonemes: _phonemesZ
// - Better memory ordering on audio/main exchange
// - Apply thresholded blendshape updates
// =======================================================
using System.Collections.Generic;
using System.Threading;
using uLipSync;
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

    NativeArray<float> _inputA, _inputB;
    volatile int _activeInputBuffer;
    volatile int _writeIndexA, _writeIndexB;
    volatile int _isDataReceived;

    NativeArray<float> _mfcc;
    NativeArray<float> _mfccForOther;
    NativeArray<float> _means;
    NativeArray<float> _standardDeviations;
    NativeArray<float> _phonemes;      // raw packed
    NativeArray<float> _phonemesZ;     // NEW: standardized packed
    NativeArray<float> _scores;
    NativeArray<LipSyncJob.Info> _info;

    public int phonemeCount;
    public int outputSampleRate;
    public int PhonemesCount;
    public int mfccsCount;

    float[] _phonemeRatios;

    public float NormalVolume;
    public float rawVolume;

    public int CachedInputSampleCount;

    public float globalMultiplier;
    public float MultipliedWeight;
    public float finalWeight;

    public SkinnedMeshRenderer skinnedMeshRenderer;
    public List<BlendShapeInfo> CachedblendShapes = new List<BlendShapeInfo>();
    public BlendShapeInfo[] BlendShapeInfos;

    public const float smoothness = 0.05f;
    public const float minVolume = -2.5f;
    public const float maxVolume = -1.5f;
    public const float VolumeDifference = maxVolume - minVolume;

    float _volume;
    float _openCloseVelocity;

    const float epsilon = 1e-6f;

    public bool HasJob;

    // Workspace + plans
    public LipSyncWorkspace ws;
    MelFilterPlan _melPlan;
    DctPlan _dctPlan;

    // Blendshape write-throttle
    float[] _lastApplied; // [meshBlendShapeCount]
    const float BlendshapeWriteEps = 0.25f; // tweak

    public void Simulate()
    {
        if (Interlocked.Exchange(ref _isDataReceived, 0) != 1) return;
        if (!_allocated) return;

        int oldActive = _activeInputBuffer;
        int newActive = oldActive ^ 1;

        // Swap first so audio thread writes elsewhere
        Volatile.Write(ref _activeInputBuffer, newActive);

        // Read frozen write index with memory fence
        int frozenStartIndex = oldActive == 0
            ? Volatile.Read(ref _writeIndexA)
            : Volatile.Read(ref _writeIndexB);

        NativeArray<float> frozenInput = oldActive == 0 ? _inputA : _inputB;

        var job = new LipSyncJob
        {
            input = frozenInput,
            startIndex = frozenStartIndex,

            outputSampleRate = outputSampleRate,
            targetSampleRate = profile.targetSampleRate,

            means = _means,
            standardDeviations = _standardDeviations,

            mfcc = _mfcc,
            phonemesZ = _phonemesZ, // NEW
            compareMethod = profile.compareMethod,
            scores = _scores,
            info = _info,
            restPhonemeIndex = 0,

            ws = ws,
            melPlan = _melPlan,
            dctPlan = _dctPlan,
        };

        _jobHandle = job.Schedule();
        HasJob = true;
    }

    public void Apply()
    {
        if (!HasJob) return;
        HasJob = false;

        _jobHandle.Complete();

        if (_mfccForOther.IsCreated && _mfcc.IsCreated)
            _mfccForOther.CopyFrom(_mfcc);

        if (_info.IsCreated && _info.Length > 0)
        {
            rawVolume = math.max(_info[0].volume, 0f);
            float logv = rawVolume > 0f ? math.log10(rawVolume) : 0f;
            NormalVolume = math.clamp(
                (logv - Common.DefaultMinVolume) / math.max(Common.DifferenceVolume, 1e-6f),
                0f, 1f
            );
        }

        if (_scores.IsCreated && phonemeCount > 0 && _phonemeRatios != null && _phonemeRatios.Length >= phonemeCount)
        {
            float sum = 0f;
            for (int i = 0; i < phonemeCount; i++) sum += _scores[i];
            float inv = sum > 0f ? 1f / sum : 0f;
            for (int i = 0; i < phonemeCount; i++) _phonemeRatios[i] = _scores[i] * inv;
        }

        var smr = skinnedMeshRenderer;
        if (smr == null || smr.sharedMesh == null) return;

        int meshBlendShapeCount = smr.sharedMesh.blendShapeCount;
        if (_lastApplied == null || _lastApplied.Length != meshBlendShapeCount)
            _lastApplied = new float[meshBlendShapeCount];

        float normVol = 0f;
        if (rawVolume > 0f)
        {
            float logv = Mathf.Log10(rawVolume);
            float denom = Mathf.Max(VolumeDifference, 1e-4f);
            normVol = Mathf.Clamp01((logv - minVolume) / denom);
        }

        _volume = SmoothDamp(_volume, normVol, ref _openCloseVelocity);
        globalMultiplier = _volume * 100f;

        var infos = BlendShapeInfos;
        if (infos == null || infos.Length == 0) return;

        float totalWeight = 0f;
        int phonemeRatioLength = _phonemeRatios != null ? _phonemeRatios.Length : 0;
        int blendInfoCount = infos.Length;

        for (int i = 0; i < blendInfoCount; i++)
        {
            var bs = infos[i];

            float targetWeight = 0f;
            int idx = bs.phonemeIndex;
            if ((uint)idx < (uint)phonemeRatioLength)
                targetWeight = _phonemeRatios[idx];

            float vel = bs.weightVelocity;
            bs.weight = SmoothDamp(bs.weight, targetWeight, ref vel);
            bs.weightVelocity = vel;

            totalWeight += bs.weight;
            infos[i] = bs;
        }

        float baseMultiply = (Mathf.Abs(totalWeight) > epsilon)
            ? (1f / totalWeight) * globalMultiplier
            : globalMultiplier;

        for (int i = 0; i < blendInfoCount; i++)
        {
            var bs = infos[i];
            int bsIndex = bs.index;
            if ((uint)bsIndex >= (uint)meshBlendShapeCount) continue;

            MultipliedWeight = bs.weight * baseMultiply;
            finalWeight = math.clamp(MultipliedWeight, 0f, 100f);
            if (float.IsNaN(finalWeight)) finalWeight = 0f;

            // NEW: avoid spamming SetBlendShapeWeight
            float prev = _lastApplied[bsIndex];
            if (math.abs(finalWeight - prev) > BlendshapeWriteEps)
            {
                smr.SetBlendShapeWeight(bsIndex, finalWeight);
                _lastApplied[bsIndex] = finalWeight;
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
        PhonemesCount = profile.mfccNum * phonemeCount;

        SafeCreate(ref _inputA, CachedInputSampleCount, NativeArrayOptions.UninitializedMemory);
        SafeCreate(ref _inputB, CachedInputSampleCount, NativeArrayOptions.UninitializedMemory);
        _activeInputBuffer = 0;
        _writeIndexA = 0;
        _writeIndexB = 0;
        _isDataReceived = 0;

        SafeCreate(ref _mfcc, profile.mfccNum);
        SafeCreate(ref _mfccForOther, profile.mfccNum);
        SafeCreate(ref _means, profile.mfccNum);
        SafeCreate(ref _standardDeviations, profile.mfccNum);
        SafeCreate(ref _scores, phonemeCount);
        SafeCreate(ref _phonemes, PhonemesCount);
        SafeCreate(ref _phonemesZ, PhonemesCount); // NEW
        SafeCreate(ref _info, 1);

        // Pack raw phonemes
        int write = 0;
        int max = math.min(mfccsCount, phonemeCount);
        for (int p = 0; p < max && write < PhonemesCount; p++)
        {
            var src = profile.mfccs[p].mfccNativeArray;
            int remaining = PhonemesCount - write;
            int len = math.min(profile.mfccNum, remaining);
            NativeArray<float>.Copy(src, 0, _phonemes, write, len);
            write += len;
        }

        // Means/std
        var meansArr = profile.means;
        if (meansArr != null && _means.IsCreated)
        {
            int dstLen = _means.Length;
            int len = math.min(meansArr.Length, dstLen);
            NativeArray<float>.Copy(meansArr, 0, _means, 0, len);
            for (int i = len; i < dstLen; i++) _means[i] = 0f;
        }

        var stdArr = profile.standardDeviation;
        if (stdArr != null && _standardDeviations.IsCreated)
        {
            int dstLen = _standardDeviations.Length;
            int len = math.min(stdArr.Length, dstLen);
            NativeArray<float>.Copy(stdArr, 0, _standardDeviations, 0, len);
            for (int i = len; i < dstLen; i++) _standardDeviations[i] = 1f;
        }

        // NEW: precompute standardized phonemesZ = (phoneme - mean)/std
        PrecomputePhonemesZ(_phonemes, _phonemesZ, _means, _standardDeviations, profile.mfccNum, phonemeCount);

        _phonemeRatios = new float[phonemeCount];

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
        int mfccLen2 = math.max(profile.mfccNum, 1);

        ws = LipSyncWorkspace.Create(
            inputLen: CachedInputSampleCount,
            outputSampleRate: outputSampleRate,
            targetSampleRate: targetRate,
            melDiv: melDiv,
            mfccLen: mfccLen2,
            fftN: 0,
            firRangeHz: 500f,
            allocator: Allocator.Persistent
        );

        _melPlan = MelFilterPlan.Build(
            fftN: ws.frame.Length,
            sampleRate: targetRate,
            melDiv: melDiv,
            alloc: Allocator.Persistent
        );

        _dctPlan = DctPlan.Build(
            melDiv: melDiv,
            mfccLen: mfccLen2,
            alloc: Allocator.Persistent
        );
    }

    static void PrecomputePhonemesZ(
        NativeArray<float> phonemesRaw,
        NativeArray<float> phonemesZ,
        NativeArray<float> means,
        NativeArray<float> std,
        int mfccLen,
        int phonemeCount)
    {
        int total = mfccLen * phonemeCount;
        if (phonemesRaw.Length < total || phonemesZ.Length < total) return;

        for (int p = 0; p < phonemeCount; p++)
        {
            int baseOff = p * mfccLen;
            for (int i = 0; i < mfccLen; i++)
            {
                float inv = math.rcp(std[i] + 1e-12f);
                phonemesZ[baseOff + i] = (phonemesRaw[baseOff + i] - means[i]) * inv;
            }
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
        SafeDispose(ref _scores);
        SafeDispose(ref _phonemes);
        SafeDispose(ref _phonemesZ);
        SafeDispose(ref _info);

        if (_melPlan.IsCreated) _melPlan.Dispose();
        if (_dctPlan.IsCreated) _dctPlan.Dispose();
        if (ws.IsCreated) ws.Dispose();

        _phonemeRatios = null;
        _lastApplied = null;
    }

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

            for (int s = 0; s < length; s += ch)
            {
                dst[w] = src[s];
                w++;
                if (w == cap) w = 0;
            }

            // publish write index with fence
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

    public float SmoothDamp(float value, float target, ref float velocity)
        => Mathf.SmoothDamp(value, target, ref velocity, smoothness);

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
