using UnityEngine;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Threading;

namespace uLipSync
{
    [System.Serializable]
    public unsafe class BasisUlipSync
    {
        public static Profile profile;

        JobHandle _jobHandle;
        bool _allocated = false;

        // =========================
        // Native double-buffered ring buffers (audio writes -> active, job reads -> frozen)
        // =========================
        NativeArray<float> _inputA;
        NativeArray<float> _inputB;

        // 0 => A, 1 => B (audio thread writes to active buffer)
        volatile int _activeInputBuffer = 0;

        // write indices (audio thread updates for the active buffer)
        volatile int _writeIndexA = 0;
        volatile int _writeIndexB = 0;

        // job snapshot
        NativeArray<float> _jobInput; // points to frozen buffer for job
        int _frozenStartIndex = 0;

        // flip-flop to indicate new data is ready
        volatile int _isDataReceived = 0; // 0 = false, 1 = true

        // =========================
        // Other native buffers
        // =========================
        NativeArray<float> _mfcc;                  // computed MFCCs (for our job)
        NativeArray<float> _mfccForOther;          // exposed copy for other systems
        NativeArray<float> _means;
        NativeArray<float> _standardDeviations;
        NativeArray<float> _phonemes;              // reference MFCCs packed [phonemeCount * mfccNum]
        NativeArray<float> _scores;                // output scores from job
        NativeArray<LipSyncJob.Info> _info;        // volume + main phoneme idx

        public int phonemeCount;
        public int outputSampleRate;
        public int PhonemesCount;
        public int mfccsCount;

        // runtime mix values
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
        public const float VolumeDifference = BasisUlipSync.maxVolume - BasisUlipSync.minVolume;

        public float _volume = 0f;
        public float _openCloseVelocity = 0f;

        const float epsilon = 1e-6f;

        public bool HasJob = false;

        // =========================
        // NEW: Workspace + precomputed plans (allocate once, reuse)
        // =========================
        public LipSyncWorkspace ws;
        MelFilterPlan _melPlan;
        DctPlan _dctPlan;

        // =========================================================
        // Fast scheduling: swap buffers, job reads frozen buffer
        // =========================================================
        public void Simulate()
        {
            // Only schedule when new audio arrived since last schedule.
            if (Interlocked.Exchange(ref _isDataReceived, 0) != 1) return;
            if (!_allocated) return;

            // Swap active buffer so audio thread starts writing into the other one.
            int oldActive = _activeInputBuffer;
            int newActive = oldActive ^ 1;
            _activeInputBuffer = newActive;

            // Freeze snapshot for the job: use the old buffer + its current write index.
            _jobInput = (oldActive == 0) ? _inputA : _inputB;
            _frozenStartIndex = (oldActive == 0) ? _writeIndexA : _writeIndexB;

            LipSyncJob lipSyncJob = new LipSyncJob
            {
                input = _jobInput,
                startIndex = _frozenStartIndex,
                outputSampleRate = outputSampleRate,
                targetSampleRate = profile.targetSampleRate,
                means = _means,
                standardDeviations = _standardDeviations,
                mfcc = _mfcc,
                phonemes = _phonemes,
                compareMethod = profile.compareMethod,
                scores = _scores,
                info = _info,
                restPhonemeIndex = 0,

                // NEW: pass workspace + precomputed plans
                ws = ws,
                melPlan = _melPlan,
                dctPlan = _dctPlan,
            };

            _jobHandle = lipSyncJob.Schedule();
            HasJob = true;
        }

        // =========================================================
        // Apply results: do NOT stall unless job is completed
        // =========================================================
        public void Apply()
        {
            if (!HasJob) return;

            HasJob = false;
            _jobHandle.Complete();

            // Copy MFCC for external access in one go (optional; gate this if not needed)
            if (_mfccForOther.IsCreated && _mfcc.IsCreated)
            {
                _mfccForOther.CopyFrom(_mfcc);
            }

            // Pull volume
            if (_info.IsCreated && _info.Length > 0)
            {
                rawVolume = math.max(_info[0].volume, 0f);

                // normalized volume using your Common range
                float logv = rawVolume > 0f ? math.log10(rawVolume) : 0f;
                NormalVolume = math.clamp(
                    (logv - Common.DefaultMinVolume) / math.max(Common.DifferenceVolume, 1e-6f),
                    0f, 1f
                );
            }

            // Compute phoneme ratios from scores (single pass)
            if (_scores.IsCreated && phonemeCount > 0 && _phonemeRatios != null && _phonemeRatios.Length >= phonemeCount)
            {
                float sum = 0f;
                for (int i = 0; i < phonemeCount; i++) sum += _scores[i];

                float inv = sum > 0f ? (1f / sum) : 0f;
                for (int i = 0; i < phonemeCount; i++) _phonemeRatios[i] = _scores[i] * inv;
            }

            // Apply to blendshapes
            var smr = skinnedMeshRenderer;
            if (smr == null || smr.sharedMesh == null) return;

            int meshBlendShapeCount = smr.sharedMesh.blendShapeCount;

            // Volume smoothing (open/close) - keep your original range mapping
            float normVol = 0f;
            if (rawVolume > 0f)
            {
                float logv = Mathf.Log10(rawVolume);
                float denom = Mathf.Max(BasisUlipSync.VolumeDifference, 1e-4f);
                normVol = Mathf.Clamp01((logv - BasisUlipSync.minVolume) / denom);
            }

            _volume = SmoothDamp(_volume, normVol, ref _openCloseVelocity);
            globalMultiplier = _volume * 100f;

            var infos = BlendShapeInfos;
            if (infos == null || infos.Length == 0) return;

            // Pass 1: smooth weights + sum
            float totalWeight = 0f;
            int phonemeRatioLength = _phonemeRatios != null ? _phonemeRatios.Length : 0;
            int blendInfoCount = infos.Length;

            for (int i = 0; i < blendInfoCount; i++)
            {
                var bs = infos[i];

                float targetWeight = 0f;
                int idx = bs.phonemeIndex;
                if ((uint)idx < (uint)phonemeRatioLength) // fast bounds check
                {
                    targetWeight = _phonemeRatios[idx];
                }

                float vel = bs.weightVelocity;
                bs.weight = SmoothDamp(bs.weight, targetWeight, ref vel);
                bs.weightVelocity = vel;

                totalWeight += bs.weight;

                infos[i] = bs; // write back if BlendShapeInfo is a struct
            }

            // Normalize sum (only when sum is non-trivial)
            float baseMultiply = (Mathf.Abs(totalWeight) > epsilon)
                ? (1f / totalWeight) * globalMultiplier
                : globalMultiplier;

            // Pass 2: apply to renderer
            for (int i = 0; i < blendInfoCount; i++)
            {
                var bs = infos[i];
                int bsIndex = bs.index;

                if ((uint)bsIndex >= (uint)meshBlendShapeCount)
                    continue;

                MultipliedWeight = bs.weight * baseMultiply;
                finalWeight = math.clamp(MultipliedWeight, 0f, 100f);

                if (float.IsNaN(finalWeight))
                    finalWeight = 0f;

                smr.SetBlendShapeWeight(bsIndex, finalWeight);
            }
        }

        // =========================================================
        // Initialization / teardown
        // =========================================================
        public void Initalize()
        {
            if (profile == null)
            {
                DisposeBuffers();
                _allocated = false;
                return;
            }

            if (_allocated)
            {
                DisposeBuffers();
            }

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

            // Allocate native double ring buffers (Uninitialized = faster; we overwrite as audio arrives)
            SafeCreate(ref _inputA, CachedInputSampleCount, NativeArrayOptions.UninitializedMemory);
            SafeCreate(ref _inputB, CachedInputSampleCount, NativeArrayOptions.UninitializedMemory);
            _activeInputBuffer = 0;
            _writeIndexA = 0;
            _writeIndexB = 0;

            SafeCreate(ref _mfcc, profile.mfccNum);
            SafeCreate(ref _mfccForOther, profile.mfccNum);
            SafeCreate(ref _means, profile.mfccNum);
            SafeCreate(ref _standardDeviations, profile.mfccNum);
            SafeCreate(ref _scores, phonemeCount);
            SafeCreate(ref _phonemes, PhonemesCount);
            SafeCreate(ref _info, 1);

            // Pack profile phoneme MFCCs into native array
            int write = 0;
            int max = math.min(mfccsCount, phonemeCount);
            for (int p = 0; p < max && write < PhonemesCount; p++)
            {
                var src = profile.mfccs[p].mfccNativeArray; // assumed NativeArray<float> length >= mfccNum
                int remaining = PhonemesCount - write;
                int len = math.min(profile.mfccNum, remaining);
                NativeArray<float>.Copy(src, 0, _phonemes, write, len);
                write += len;
            }

            // Copy means
            var meansArr = profile.means; // float[]
            if (meansArr != null && _means.IsCreated)
            {
                int dstLen = _means.Length;
                int len = math.min(meansArr.Length, dstLen);
                NativeArray<float>.Copy(meansArr, 0, _means, 0, len);
                for (int i = len; i < dstLen; i++) _means[i] = 0f;
            }

            // Copy std devs
            var stdArr = profile.standardDeviation; // float[]
            if (stdArr != null && _standardDeviations.IsCreated)
            {
                int dstLen = _standardDeviations.Length;
                int len = math.min(stdArr.Length, dstLen);
                NativeArray<float>.Copy(stdArr, 0, _standardDeviations, 0, len);
                for (int i = len; i < dstLen; i++) _standardDeviations[i] = 1f;
            }

            // Phoneme ratios array (managed, small)
            _phonemeRatios = new float[phonemeCount];

            // Map phoneme names -> index once
            if (BlendShapeInfos != null)
            {
                Dictionary<string, int> phonemeNameToIndex = new Dictionary<string, int>(32);
                for (int i = 0; i < phonemeCount; i++)
                {
                    var name = profile.GetPhoneme(i);
                    if (!string.IsNullOrEmpty(name)) phonemeNameToIndex[name] = i;
                }

                int blendShapeLength = BlendShapeInfos.Length;
                for (int i = 0; i < blendShapeLength; i++)
                {
                    var bs = BlendShapeInfos[i];
                    bs.phonemeIndex =
                        !string.IsNullOrEmpty(bs.phoneme) && phonemeNameToIndex.TryGetValue(bs.phoneme, out int idx)
                            ? idx
                            : -1;

                    BlendShapeInfos[i] = bs; // if struct
                }
            }

            // =========================
            // NEW: Allocate workspace + precomputed mel/dct plans
            // =========================
            int targetRate = math.max(profile.targetSampleRate, 1);
            int melDiv = math.max(profile.melFilterBankChannels, 1);

            // Choose MFCC length = what you output (profile.mfccNum)
            int mfccLen = math.max(profile.mfccNum, 1);

            // Create workspace. IMPORTANT:
            // This assumes you're using the updated LipSyncWorkspace.Create signature from the optimized code:
            // Create(inputLen, outputSampleRate, targetSampleRate, melDiv, fftN, firRangeHz, allocator)
            // Pass fftN=0 to auto-pick next pow2 >= downsample length.
            ws = LipSyncWorkspace.Create(
                inputLen: CachedInputSampleCount,
                outputSampleRate: outputSampleRate,
                targetSampleRate: targetRate,
                melDiv: melDiv,
                fftN: 0,
                firRangeHz: 500f,
                allocator: Allocator.Persistent
            );

            // Build plans using the workspace FFT size (ws.frame.Length == fftN)
            // Mel plan should use TARGET sample rate (because spectrum is computed after downsample to target rate)
            _melPlan = MelFilterPlan.Build(
                fftN: ws.frame.Length,
                sampleRate: targetRate,
                melDiv: melDiv,
                alloc: Allocator.Persistent
            );

            _dctPlan = DctPlan.Build(
                melDiv: melDiv,
                mfccLen: mfccLen,
                alloc: Allocator.Persistent
            );
        }

        public void OnDestroy()
        {
            DisposeBuffers();
        }

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
            SafeDispose(ref _info);

            // NEW: dispose plans + workspace
            if (_melPlan.IsCreated) _melPlan.Dispose();
            if (_dctPlan.IsCreated) _dctPlan.Dispose();
            if (ws.IsCreated) ws.Dispose();

            _phonemeRatios = null;
        }

        // =========================================================
        // Audio callback: write directly into native ring buffer (no lock, no managed mirror)
        // =========================================================
        public void OnDataReceived(float[] input, int channels, int length)
        {
            if (!_allocated || input == null || length <= 0) return;

            int cap = CachedInputSampleCount;
            if (cap <= 0) return;

            int ch = math.max(channels, 1);

            int buf = _activeInputBuffer;
            NativeArray<float> dstArr = (buf == 0) ? _inputA : _inputB;

            float* dst = (float*)NativeArrayUnsafeUtility.GetUnsafePtr(dstArr);

            fixed (float* src = input)
            {
                int w = (buf == 0) ? _writeIndexA : _writeIndexB;

                // Write mono (left channel only)
                for (int s = 0; s < length; s += ch)
                {
                    dst[w] = src[s];
                    w++;
                    if (w == cap) w = 0;
                }

                if (buf == 0) _writeIndexA = w;
                else _writeIndexB = w;
            }

            // Signal new data for the next Update/LateUpdate
            Interlocked.Exchange(ref _isDataReceived, 1);
        }

        // =========================================================
        // Utilities
        // =========================================================
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
        {
            return Mathf.SmoothDamp(value, target, ref velocity, smoothness);
        }

        public void AddBlendShape(string phoneme, int blendShape)
        {
            var bs = CachedblendShapes.Find(info => info.phoneme == phoneme);
            if (bs == null)
            {
                bs = new BlendShapeInfo { phoneme = phoneme };
                CachedblendShapes.Add(bs);
            }
            if (skinnedMeshRenderer != null)
            {
                bs.index = blendShape;
            }
        }
    }
}
