using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using System.Collections.Generic;
using System.Threading;
namespace uLipSync
{
    [System.Serializable]
    public class BasisUlipSync
    {
        public static Profile profile;
        JobHandle _jobHandle;
        readonly object _lockObject = new object();
        bool _allocated = false;
        // Audio ring-buffer write index
        int _writeIndex = 0;
        // flip-flop to indicate new data is ready
        volatile int _isDataReceived = 0; // 0 = false, 1 = true
        // Native buffers
        NativeArray<float> _inputData;             // ring buffer in native space
        NativeArray<float> _mfcc;                  // computed MFCCs (for our job)
        NativeArray<float> _mfccForOther;          // exposed copy for other systems
        NativeArray<float> _means;
        NativeArray<float> _standardDeviations;
        NativeArray<float> _phonemes;              // reference MFCCs packed [phonemeCount * mfccNum]
        NativeArray<float> _scores;                // output scores from job
        NativeArray<LipSyncJob.Info> _info;        // volume + main phoneme idx
        public int phonemeCount;
        public float[] Inputs;                     // managed mirror used for feeding audio only
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
        public void Simulate()
        {
            // If last scheduled job is still running, skip this frame.
            if (!_jobHandle.Equals(default(JobHandle)) && !_jobHandle.IsCompleted)
            {
                return;
            }
            // If a job existed, complete it and consume results.
            if (!_jobHandle.Equals(default(JobHandle)))
            {
                _jobHandle.Complete();
                _jobHandle = default;
                // Copy MFCC for external access in one go (no managed allocs)
                if (_mfccForOther.IsCreated && _mfcc.IsCreated)
                {
                    _mfccForOther.CopyFrom(_mfcc);
                }
                // Pull main phoneme + volume
                if (_info.IsCreated && _info.Length > 0)
                {
                    rawVolume = math.max(_info[0].volume, 0f);
                    // single log10, normalized to [0..1]
                    float logv = rawVolume > 0f ? math.log10(rawVolume) : 0f;
                    NormalVolume = math.clamp((logv - Common.DefaultMinVolume) / math.max(Common.DifferenceVolume, 1e-6f), 0f, 1f);
                }
                // Compute phoneme ratios from scores (single pass, no managed copy)
                if (_scores.IsCreated && phonemeCount > 0)
                {
                    float sum = 0f;
                    for (int i = 0; i < phonemeCount; i++)
                    {
                        sum += _scores[i];
                    }

                    float inv = sum > 0f ? 1f / sum : 0f;
                    for (int Index = 0; Index < phonemeCount; Index++)
                    {
                        _phonemeRatios[Index] = _scores[Index] * inv;
                    }
                }
                // Apply to blendshapes
                var smr = skinnedMeshRenderer;
                if (smr == null || smr.sharedMesh == null)
                {
                    return;
                }
                int blendShapeCount = smr.sharedMesh.blendShapeCount;
                // Volume smoothing (open/close)
                float normVol = 0f;
                if (rawVolume > 0f)
                {
                    float logv = Mathf.Log10(rawVolume);
                    float denom = Mathf.Max(BasisUlipSync.VolumeDifference, 1e-4f);
                    normVol = Mathf.Clamp01((logv - BasisUlipSync.minVolume) / denom);
                }
                _volume = SmoothDamp(_volume, normVol, ref _openCloseVelocity);
                globalMultiplier = _volume * 100;
                var infos = BlendShapeInfos;
                // First pass: compute target weights + sum (write back if struct)
                float totalWeight = 0f;
                int PhonemeRatioLength = _phonemeRatios.Length;
                int BlendShapeCount = infos.Length;
                for (int Index = 0; Index < BlendShapeCount; Index++)
                {
                    var bs = infos[Index];
                    float targetWeight = 0f;
                    int idx = bs.phonemeIndex;
                    if ((uint)idx < (uint)PhonemeRatioLength) // fast bounds check trick
                    {
                        targetWeight = _phonemeRatios[idx];
                    }
                    float vel = bs.weightVelocity;
                    bs.weight = SmoothDamp(bs.weight, targetWeight, ref vel);
                    bs.weightVelocity = vel;
                    totalWeight += bs.weight;
                    infos[Index] = bs; // write back if struct
                }
                // Base multiply (normalize only when sum is not ~zero)
                float baseMultiply = (Mathf.Abs(totalWeight) > epsilon) ? (1f / totalWeight) * globalMultiplier : globalMultiplier;
                // Second pass: apply to renderer
                for (int Index = 0; Index < BlendShapeCount; Index++)
                {
                    var bs = infos[Index];
                    if (bs.index < 0 || bs.index >= blendShapeCount)
                    {
                        continue;
                    }
                    MultipliedWeight = bs.weight * baseMultiply;
                    finalWeight = math.clamp(MultipliedWeight, 0f, 100f);
                    if (float.IsNaN(finalWeight))
                    {
                        finalWeight = 0f;
                    }
                    smr.SetBlendShapeWeight(bs.index, finalWeight);
                }
            }

            // If new audio arrived, schedule a new job using the ring buffer snapshot.
            if (Interlocked.Exchange(ref _isDataReceived, 0) == 1)
            {
                if (!_allocated) return;

                // Copy managed inputs -> native ringbuffer (fast; single copy)
                int startIndexSnapshot;
                float[] inputsSnapshot;
                lock (_lockObject)
                {
                    startIndexSnapshot = _writeIndex;
                    inputsSnapshot = Inputs; // reference copy; content is stable while we copy into native
                }

                // Copy the entire managed ring buffer into native (keeps job simple)
                if (_inputData.IsCreated && inputsSnapshot != null && inputsSnapshot.Length == _inputData.Length)
                {
                    _inputData.CopyFrom(inputsSnapshot);
                }

                // Build and dispatch the job
                var lipSyncJob = new LipSyncJob
                {
                    input = _inputData,
                    startIndex = startIndexSnapshot,
                    outputSampleRate = outputSampleRate,
                    targetSampleRate = profile.targetSampleRate,
                    melFilterBankChannels = profile.melFilterBankChannels,
                    means = _means,
                    standardDeviations = _standardDeviations,
                    mfcc = _mfcc,
                    phonemes = _phonemes,
                    compareMethod = profile.compareMethod,
                    scores = _scores,
                    info = _info,
                    silenceRmsThreshold = 0.05f,
                    restPhonemeIndex = 0,
                };

                _jobHandle = lipSyncJob.Schedule();
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

            lock (_lockObject)
            {
                outputSampleRate = AudioSettings.outputSampleRate;
                float r = (float)outputSampleRate / math.max(profile.targetSampleRate, 1);
                CachedInputSampleCount = Mathf.CeilToInt(math.max(profile.sampleCount, 1) * r);

                int Count = profile.mfccs.Count;
                phonemeCount = math.max(Count, 1);
                mfccsCount = Count;
                PhonemesCount = profile.mfccNum * phonemeCount;

                // Managed ring buffer for feeding audio
                Inputs = new float[CachedInputSampleCount];

                // Native buffers (create or recreate)
                SafeCreate(ref _inputData, CachedInputSampleCount);
                SafeCreate(ref _mfcc, profile.mfccNum);
                SafeCreate(ref _mfccForOther, profile.mfccNum);
                SafeCreate(ref _means, profile.mfccNum);
                SafeCreate(ref _standardDeviations, profile.mfccNum);
                SafeCreate(ref _scores, phonemeCount);
                SafeCreate(ref _phonemes, PhonemesCount);
                SafeCreate(ref _info, 1);

                // Keep a tightly packed copy of profile phoneme MFCCs in native array
                int write = 0;
                int max = math.min(mfccsCount, phonemeCount);
                for (int p = 0; p < max && write < PhonemesCount; p++)
                {
                    var src = profile.mfccs[p].mfccNativeArray; // assumed NativeArray<float> of length >= mfccNum
                    int remaining = PhonemesCount - write;
                    int len = math.min(profile.mfccNum, remaining);
                    NativeArray<float>.Copy(src, 0, _phonemes, write, len);
                    write += len;
                }
            }

            // Copy stats from profile (managed float[] → NativeArray<float>)
            var meansArr = profile.means; // float[]
            if (meansArr != null && _means.IsCreated)
            {
                int MeansLength = _means.Length;
                int len = math.min(meansArr.Length, MeansLength);
                NativeArray<float>.Copy(meansArr, 0, _means, 0, len);
                // zero-fill any remainder if profile array is shorter
                for (int i = len; i < MeansLength; i++)
                {
                    _means[i] = 0f;
                }
            }

            var stdArr = profile.standardDeviation; // float[]
            if (stdArr != null && _standardDeviations.IsCreated)
            {
                int StandardDeviationsLength = _standardDeviations.Length;
                int len = math.min(stdArr.Length, StandardDeviationsLength);
                NativeArray<float>.Copy(stdArr, 0, _standardDeviations, 0, len);
                for (int Index = len; Index < StandardDeviationsLength; Index++)
                {
                    _standardDeviations[Index] = 1f; // sane default
                }
            }
            // Phoneme names + map
            _phonemeRatios = new float[phonemeCount];
            if (profile == null || BlendShapeInfos == null)
            {
                return;
            }
            // Build a temporary dictionary once (or use Option B below)
            Dictionary<string, int> _phonemeNameToIndex = new Dictionary<string, int>(32);
            for (int Index = 0; Index < phonemeCount; Index++)
            {
                var name = profile.GetPhoneme(Index);
                if (!string.IsNullOrEmpty(name))
                {
                    _phonemeNameToIndex[name] = Index;
                }
            }
            int BlendShapeLength = BlendShapeInfos.Length;
            for (int Index = 0; Index < BlendShapeLength; Index++)
            {
                var bs = BlendShapeInfos[Index];
                bs.phonemeIndex = !string.IsNullOrEmpty(bs.phoneme) && _phonemeNameToIndex.TryGetValue(bs.phoneme, out int idx) ? idx : -1;
                BlendShapeInfos[Index] = bs; // if struct
            }
        }
        public void OnDestroy()
        {
            if (!_jobHandle.Equals(default(JobHandle)))
            {
                _jobHandle.Complete();
                _jobHandle = default;
            }
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

            lock (_lockObject)
            {
                Inputs = null;

                SafeDispose(ref _inputData);
                SafeDispose(ref _mfcc);
                SafeDispose(ref _mfccForOther);
                SafeDispose(ref _means);
                SafeDispose(ref _standardDeviations);
                SafeDispose(ref _scores);
                SafeDispose(ref _phonemes);
                SafeDispose(ref _info);
            }
        }
        public void OnDataReceived(float[] input, int channels, int length)
        {
            if (!_allocated || input == null || length <= 0) return;

            // Write mono (left) samples into ring buffer
            lock (_lockObject)
            {
                int cap = CachedInputSampleCount;
                if (cap <= 0 || Inputs == null) return;

                int w = _writeIndex;
                int ch = math.max(channels, 1);
                for (int i = 0; i < length; i += ch)
                {
                    Inputs[w] = input[i];
                    w++;
                    if (w >= cap) w = 0;
                }
                _writeIndex = w;
            }

            // Signal new data for the next LateUpdate
            Interlocked.Exchange(ref _isDataReceived, 1);
        }
        static void SafeCreate<T>(ref NativeArray<T> array, int length, NativeArrayOptions options = NativeArrayOptions.ClearMemory) where T : struct
        {
            if (array.IsCreated)
            {
                if (array.Length == length)
                {
                    return;
                }
                array.Dispose();
            }
            array = new NativeArray<T>(length, Allocator.Persistent, options);
        }
        static void SafeDispose<T>(ref NativeArray<T> array) where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
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
