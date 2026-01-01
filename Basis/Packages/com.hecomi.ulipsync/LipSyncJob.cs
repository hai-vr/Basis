using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace uLipSync
{
    public struct MelFilterPlan : IDisposable
    {
        public NativeArray<int> starts;     // [melDiv]
        public NativeArray<int> lengths;    // [melDiv]
        public NativeArray<int> bins;       // [totalWeights]
        public NativeArray<float> weights;  // [totalWeights]

        public int melDiv;
        public int fftN;
        public int specLen;
        public float sampleRate;

        public bool IsCreated => starts.IsCreated && lengths.IsCreated && bins.IsCreated && weights.IsCreated;

        public void Dispose()
        {
            if (starts.IsCreated) starts.Dispose();
            if (lengths.IsCreated) lengths.Dispose();
            if (bins.IsCreated) bins.Dispose();
            if (weights.IsCreated) weights.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float ToMel(float hz, bool slaney = false)
        {
            float a = slaney ? 2595f : 1127f;
            return a * math.log(hz / 700f + 1f);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float ToHz(float mel, bool slaney = false)
        {
            float a = slaney ? 2595f : 1127f;
            return 700f * (math.exp(mel / a) - 1f);
        }

        public static MelFilterPlan Build(int fftN, float sampleRate, int melDiv, Allocator alloc, bool slaney = false)
        {
            int specLen = fftN / 2 + 1;

            float fMax = sampleRate * 0.5f;
            float melMax = ToMel(fMax, slaney);
            float df = fMax / (specLen - 1);
            float dMel = melMax / (melDiv + 1);

            // First pass: count weights
            int total = 0;
            var tmpStarts = new NativeArray<int>(melDiv, Allocator.Temp);
            var tmpLens = new NativeArray<int>(melDiv, Allocator.Temp);

            for (int n = 0; n < melDiv; n++)
            {
                float melBegin = dMel * n;
                float melCenter = dMel * (n + 1);
                float melEnd = dMel * (n + 2);

                float fBegin = ToHz(melBegin, slaney);
                float fCenter = ToHz(melCenter, slaney);
                float fEnd = ToHz(melEnd, slaney);

                int iBegin = math.clamp((int)math.ceil(fBegin / df), 0, specLen - 1);
                int iCenter = math.clamp((int)math.round(fCenter / df), 0, specLen - 1);
                int iEnd = math.clamp((int)math.floor(fEnd / df), 0, specLen - 1);

                if (iCenter < iBegin) iCenter = iBegin;
                if (iEnd < iCenter) iEnd = iCenter;

                int len = iEnd - iBegin + 1;
                tmpStarts[n] = total;
                tmpLens[n] = len;
                total += len;
            }

            var plan = new MelFilterPlan
            {
                melDiv = melDiv,
                fftN = fftN,
                specLen = specLen,
                sampleRate = sampleRate,

                starts = new NativeArray<int>(melDiv, alloc),
                lengths = new NativeArray<int>(melDiv, alloc),
                bins = new NativeArray<int>(total, alloc),
                weights = new NativeArray<float>(total, alloc),
            };

            // Second pass: fill weights
            int cursor = 0;
            for (int n = 0; n < melDiv; n++)
            {
                plan.starts[n] = tmpStarts[n];
                plan.lengths[n] = tmpLens[n];

                float melBegin = dMel * n;
                float melCenter = dMel * (n + 1);
                float melEnd = dMel * (n + 2);

                float fBegin = ToHz(melBegin, slaney);
                float fCenter = ToHz(melCenter, slaney);
                float fEnd = ToHz(melEnd, slaney);

                int iBegin = math.clamp((int)math.ceil(fBegin / df), 0, specLen - 1);
                int iCenter = math.clamp((int)math.round(fCenter / df), 0, specLen - 1);
                int iEnd = math.clamp((int)math.floor(fEnd / df), 0, specLen - 1);

                if (iCenter < iBegin) iCenter = iBegin;
                if (iEnd < iCenter) iEnd = iCenter;

                float denomL = math.max(fCenter - fBegin, 1e-12f);
                float denomR = math.max(fEnd - fCenter, 1e-12f);

                float norm = 0.5f / math.max(fEnd - fBegin, 1e-12f);

                for (int i = iBegin; i <= iEnd; i++)
                {
                    float f = df * i;
                    float a = (i < iCenter) ? ((f - fBegin) / denomL) : ((fEnd - f) / denomR);
                    a = math.max(a, 0f) * norm;

                    plan.bins[cursor] = i;
                    plan.weights[cursor] = a;
                    cursor++;
                }
            }

            tmpStarts.Dispose();
            tmpLens.Dispose();
            return plan;
        }
    }

    public struct DctPlan : IDisposable
    {
        public NativeArray<float> cosTable; // [mfccLen * melDiv]
        public int melDiv;
        public int mfccLen;

        public bool IsCreated => cosTable.IsCreated;

        public void Dispose()
        {
            if (cosTable.IsCreated) cosTable.Dispose();
        }

        public static DctPlan Build(int melDiv, int mfccLen, Allocator alloc)
        {
            var plan = new DctPlan
            {
                melDiv = melDiv,
                mfccLen = mfccLen,
                cosTable = new NativeArray<float>(mfccLen * melDiv, alloc)
            };

            float a = math.PI / melDiv;

            for (int r = 0; r < mfccLen; r++)
            {
                int i = r + 1; // DCT index (skip c0)
                int baseIdx = r * melDiv;

                for (int j = 0; j < melDiv; j++)
                {
                    float ang = (j + 0.5f) * i * a;
                    plan.cosTable[baseIdx + j] = math.cos(ang);
                }
            }

            return plan;
        }
    }
    public struct LipSyncWorkspace : IDisposable
    {
        public NativeArray<float> buffer;          // inputLen
        public NativeArray<float> down;            // downLen
        public NativeArray<float> frame;           // fftN
        public NativeArray<float> powerHalf;       // fftN/2+1
        public NativeArray<float> melSpectrum;     // melDiv

        public NativeArray<float> tmp;             // >= max(inputLen, downLen, fftN)
        public NativeArray<float> fftRe;           // fftN
        public NativeArray<float> fftIm;           // fftN

        public NativeArray<float> firTaps;         // firLen (precomputed)
        public NativeArray<float> hammingWindow;   // fftN (precomputed)

        public bool IsCreated =>
            buffer.IsCreated && down.IsCreated && frame.IsCreated &&
            powerHalf.IsCreated && melSpectrum.IsCreated &&
            tmp.IsCreated && fftRe.IsCreated && fftIm.IsCreated &&
            firTaps.IsCreated && hammingWindow.IsCreated;

        public void Dispose()
        {
            if (buffer.IsCreated) buffer.Dispose();
            if (down.IsCreated) down.Dispose();
            if (frame.IsCreated) frame.Dispose();
            if (powerHalf.IsCreated) powerHalf.Dispose();
            if (melSpectrum.IsCreated) melSpectrum.Dispose();
            if (tmp.IsCreated) tmp.Dispose();
            if (fftRe.IsCreated) fftRe.Dispose();
            if (fftIm.IsCreated) fftIm.Dispose();
            if (firTaps.IsCreated) firTaps.Dispose();
            if (hammingWindow.IsCreated) hammingWindow.Dispose();
        }

        public static int ComputeDownsampleLength(int inputLen, int outputSampleRate, int targetSampleRate)
        {
            if (outputSampleRate <= targetSampleRate) return inputLen;

            if (outputSampleRate % targetSampleRate == 0)
            {
                int skip = outputSampleRate / targetSampleRate;
                return inputLen / skip;
            }

            float df = (float)outputSampleRate / targetSampleRate;
            return (int)math.round(inputLen / df);
        }

        public static int ComputeLowPassFirLength(float sampleRate, float cutoffHz, float rangeHz)
        {
            float range = rangeHz / sampleRate;

            int n = (int)math.round(3.1f / range);
            if (((n + 1) & 1) == 0) n += 1;
            return n;
        }

        static int NextPow2(int x)
        {
            x = math.max(1, x);
            x--;
            x |= x >> 1;
            x |= x >> 2;
            x |= x >> 4;
            x |= x >> 8;
            x |= x >> 16;
            return x + 1;
        }

        public static LipSyncWorkspace Create(
            int inputLen,
            int outputSampleRate,
            int targetSampleRate,
            int melDiv,
            int fftN,
            float firRangeHz,
            Allocator allocator)
        {
            int downLen = ComputeDownsampleLength(inputLen, outputSampleRate, targetSampleRate);

            if (fftN <= 0) fftN = NextPow2(downLen);
            if ((fftN & (fftN - 1)) != 0) fftN = NextPow2(fftN);
            fftN = math.max(fftN, downLen);

            float cutoffHz = targetSampleRate * 0.5f;
            int firLen = ComputeLowPassFirLength(outputSampleRate, cutoffHz, firRangeHz);

            int specLen = fftN / 2 + 1;

            var ws = new LipSyncWorkspace
            {
                buffer = new NativeArray<float>(inputLen, allocator),
                down = new NativeArray<float>(downLen, allocator),
                frame = new NativeArray<float>(fftN, allocator),
                powerHalf = new NativeArray<float>(specLen, allocator),
                melSpectrum = new NativeArray<float>(melDiv, allocator),

                tmp = new NativeArray<float>(math.max(math.max(inputLen, downLen), fftN), allocator),
                fftRe = new NativeArray<float>(fftN, allocator),
                fftIm = new NativeArray<float>(fftN, allocator),

                firTaps = new NativeArray<float>(firLen, allocator),
                hammingWindow = new NativeArray<float>(fftN, allocator),
            };

            PrecomputeLowPassTaps(ws.firTaps, outputSampleRate, cutoffHz, firRangeHz);
            PrecomputeHamming(ws.hammingWindow);

            return ws;
        }

        static void PrecomputeHamming(NativeArray<float> window)
        {
            unsafe
            {
                float* w = (float*)window.GetUnsafePtr();
                int len = window.Length;
                float inv = 1f / (len - 1);

                for (int i = 0; i < len; i++)
                {
                    float x = i * inv;
                    w[i] = 0.54f - 0.46f * math.cos(2f * math.PI * x);
                }
            }
        }

        static void PrecomputeLowPassTaps(NativeArray<float> taps, float sampleRate, float cutoffHz, float rangeHz)
        {
            float cutoff = (cutoffHz - rangeHz) / sampleRate;
            float range = rangeHz / sampleRate;

            int n = (int)math.round(3.1f / range);
            if (((n + 1) & 1) == 0) n += 1;
            n = math.min(n, taps.Length);

            unsafe
            {
                float* b = (float*)taps.GetUnsafePtr();
                float half = (n - 1) * 0.5f;

                for (int i = 0; i < n; i++)
                {
                    float x = i - half;
                    float ang = 2f * math.PI * cutoff * x;
                    if (math.abs(ang) < 1e-12f) b[i] = 2f * cutoff;
                    else b[i] = 2f * cutoff * math.sin(ang) / ang;
                }
                for (int i = n; i < taps.Length; i++)
                    b[i] = 0f;
            }
        }
    }
    [BurstCompile]
    public unsafe struct LipSyncJob : IJob
    {
        public struct Info
        {
            public float volume;
            public int mainPhonemeIndex;
        }

        [ReadOnly] public NativeArray<float> input;
        [ReadOnly] public int startIndex;

        [ReadOnly] public int outputSampleRate;
        [ReadOnly] public int targetSampleRate;

        [ReadOnly] public CompareMethod compareMethod;
        [ReadOnly] public NativeArray<float> means;
        [ReadOnly] public NativeArray<float> standardDeviations;
        [ReadOnly] public NativeArray<float> phonemes;
        [ReadOnly] public int restPhonemeIndex;

        // Precomputed plans (passed by value into job; internal arrays are read-only)
        [ReadOnly] public MelFilterPlan melPlan;
        [ReadOnly] public DctPlan dctPlan;

        // Outputs
        public NativeArray<float> mfcc;     // length = mfccLen (profile.mfccNum)
        public NativeArray<float> scores;   // length = phonemeCount
        public NativeArray<Info> info;      // length >= 1

        // Workspace
        public LipSyncWorkspace ws;

        private const float FIR_RANGE_HZ = 500f;
        private const float EPS = 1e-12f;
        private const float LN10 = 2.302585092994046f;

        public int ScoresLength;
        public int MFCCLength;

        public void Execute()
        {
            ScoresLength = scores.Length;
            MFCCLength = mfcc.Length;

            // 1) Copy ring -> ws.buffer
            CopyRingBuffer(input, ws.buffer, startIndex);

            // 2) Lowpass with precomputed taps
            LowPassFilterInPlace_Precomputed(ws.buffer, ws.tmp, ws.firTaps);

            // 3) Downsample -> ws.down
            DownSample(ws.buffer, ws.down, outputSampleRate, targetSampleRate);

            // 4) Pre-emphasis
            PreEmphasisInPlace(ws.down, ws.tmp, 0.97f);

            // 5) Prepare FFT frame: copy down, pad, apply precomputed hamming
            PrepareWindowedFrame(ws.down, ws.frame, ws.hammingWindow);

            // 6) Normalize (kept)
            NormalizeInPlace(ws.frame, 1f);

            // 7) FFT power spectrum half
            FFTPowerHalf(ws.frame, ws.powerHalf, ws.fftRe, ws.fftIm);

            // 8) Mel filter bank using POINTER helper (Burst-safe)
            ApplyMelPlan_BurstSafe(ws.powerHalf, ws.melSpectrum, melPlan);

            // floor to EPS (avoid log10 issues)
            for (int k = 0; k < ws.melSpectrum.Length; k++)
            {
                ws.melSpectrum[k] = math.max(ws.melSpectrum[k], EPS);
            }

            // 9) power->dB
            PowerToDbInPlace(ws.melSpectrum);

            // 10) DCT using POINTER helper (Burst-safe) -> mfcc
            DctMfccFromPlan_BurstSafe(ws.melSpectrum, mfcc, dctPlan);

            // sanitize MFCC
            for (int i = 0; i < MFCCLength; i++)
            {
                float v = mfcc[i];
                mfcc[i] = IsFinite(v) ? v : 0f;
            }

            // 11) Silence fallback
            if (IsLowEnergy())
            {
                int rest = SafeRestIndex(restPhonemeIndex, ScoresLength);
                OneHotRest(scores, rest);
                info[0] = new Info
                {
                    volume = GetRMSVolume(ws.buffer),
                    mainPhonemeIndex = rest
                };
                return;
            }

            // 12) Scores
            CalcScoresSIMD();

            int winner = GetVowelOrRest();
            info[0] = new Info
            {
                volume = GetRMSVolume(ws.buffer),
                mainPhonemeIndex = winner
            };
        }
        static void ApplyMelPlan_BurstSafe(NativeArray<float> powerHalf, NativeArray<float> melOut, in MelFilterPlan plan)
        {
            unsafe
            {
                ApplyMelPlanPtr(
                    (float*)powerHalf.GetUnsafeReadOnlyPtr(),
                    (float*)melOut.GetUnsafePtr(),
                    (int*)plan.starts.GetUnsafeReadOnlyPtr(),
                    (int*)plan.lengths.GetUnsafeReadOnlyPtr(),
                    (int*)plan.bins.GetUnsafeReadOnlyPtr(),
                    (float*)plan.weights.GetUnsafeReadOnlyPtr(),
                    plan.melDiv);
            }
        }

        static void DctMfccFromPlan_BurstSafe(NativeArray<float> melDb, NativeArray<float> mfccOut, in DctPlan plan)
        {
            unsafe
            {
                DctMfccFromCosTablePtr(
                    (float*)melDb.GetUnsafeReadOnlyPtr(),
                    (float*)mfccOut.GetUnsafePtr(),
                    (float*)plan.cosTable.GetUnsafeReadOnlyPtr(),
                    plan.melDiv,
                    plan.mfccLen);
            }
        }
        [BurstCompile]
        static unsafe void ApplyMelPlanPtr(
            float* powerHalf,                 // [specLen]
            float* melOut,                    // [melDiv]
            int* starts, int* lengths,        // [melDiv]
            int* bins, float* weights,        // [totalWeights]
            int melDiv)
        {
            for (int n = 0; n < melDiv; n++)
            {
                int start = starts[n];
                int len = lengths[n];

                float sum = 0f;
                for (int k = 0; k < len; k++)
                {
                    int idx = start + k;
                    sum += powerHalf[bins[idx]] * weights[idx];
                }
                melOut[n] = sum;
            }
        }

        [BurstCompile]
        static unsafe void DctMfccFromCosTablePtr(
            float* melDb,     // [melDiv]
            float* mfccOut,   // [mfccLen]
            float* cosTable,  // [mfccLen * melDiv]
            int melDiv,
            int mfccLen)
        {
            for (int r = 0; r < mfccLen; r++)
            {
                float sum = 0f;
                int baseIdx = r * melDiv;

                for (int j = 0; j < melDiv; j++)
                    sum += melDb[j] * cosTable[baseIdx + j];

                mfccOut[r] = sum;
            }
        }
        [BurstCompile]
        void CalcScoresSIMD()
        {
            float sum = 0f;

            for (int i = 0; i < ScoresLength; i++)
            {
                float s = CalcScoreSIMD(i);
                if (!IsFinite(s) || s < 0f) s = 0f;
                scores[i] = s;
                sum += s;
            }

            if (sum > 0f && IsFinite(sum))
            {
                float inv = math.rcp(sum);
                for (int i = 0; i < ScoresLength; i++)
                    scores[i] *= inv;
            }
        }

        [BurstCompile]
        float CalcScoreSIMD(int index)
        {
            switch (compareMethod)
            {
                case CompareMethod.L1Norm: return CalcL1NormScoreSIMD(index);
                case CompareMethod.L2Norm: return CalcL2NormScoreSIMD(index);
                case CompareMethod.CosineSimilarity: return CalcCosineSimilarityScoreSIMD(index);
                default: return 0f;
            }
        }

        [BurstCompile]
        float CalcL1NormScoreSIMD(int index)
        {
            int baseOffset = index * MFCCLength;
            float accum = 0f;

            int i = 0;
            int limit = MFCCLength & ~3;

            for (; i < limit; i += 4)
            {
                float4 invStd = new float4(
                    math.rcp(standardDeviations[i + 0] + EPS),
                    math.rcp(standardDeviations[i + 1] + EPS),
                    math.rcp(standardDeviations[i + 2] + EPS),
                    math.rcp(standardDeviations[i + 3] + EPS)
                );

                float4 mx = new float4(
                    mfcc[i + 0] - means[i + 0],
                    mfcc[i + 1] - means[i + 1],
                    mfcc[i + 2] - means[i + 2],
                    mfcc[i + 3] - means[i + 3]
                ) * invStd;

                float4 my = new float4(
                    phonemes[baseOffset + i + 0] - means[i + 0],
                    phonemes[baseOffset + i + 1] - means[i + 1],
                    phonemes[baseOffset + i + 2] - means[i + 2],
                    phonemes[baseOffset + i + 3] - means[i + 3]
                ) * invStd;

                float4 d = math.abs(mx - my);
                accum += d.x + d.y + d.z + d.w;
            }

            for (; i < MFCCLength; i++)
            {
                float invStd = math.rcp(standardDeviations[i] + EPS);
                float x = (mfcc[i] - means[i]) * invStd;
                float y = (phonemes[baseOffset + i] - means[i]) * invStd;
                accum += math.abs(x - y);
            }

            float distance = accum * math.rcp(MFCCLength);
            return math.exp(-distance * LN10);
        }

        [BurstCompile]
        float CalcL2NormScoreSIMD(int index)
        {
            int baseOffset = index * MFCCLength;
            float accum = 0f;

            int i = 0;
            int limit = MFCCLength & ~3;

            for (; i < limit; i += 4)
            {
                float4 invStd = new float4(
                    math.rcp(standardDeviations[i + 0] + EPS),
                    math.rcp(standardDeviations[i + 1] + EPS),
                    math.rcp(standardDeviations[i + 2] + EPS),
                    math.rcp(standardDeviations[i + 3] + EPS)
                );

                float4 x = (new float4(mfcc[i + 0], mfcc[i + 1], mfcc[i + 2], mfcc[i + 3])
                            - new float4(means[i + 0], means[i + 1], means[i + 2], means[i + 3])) * invStd;

                float4 y = (new float4(phonemes[baseOffset + i + 0], phonemes[baseOffset + i + 1], phonemes[baseOffset + i + 2], phonemes[baseOffset + i + 3])
                            - new float4(means[i + 0], means[i + 1], means[i + 2], means[i + 3])) * invStd;

                float4 d = x - y;
                accum += math.dot(d, d);
            }

            for (; i < MFCCLength; i++)
            {
                float invStd = math.rcp(standardDeviations[i] + EPS);
                float x = (mfcc[i] - means[i]) * invStd;
                float y = (phonemes[baseOffset + i] - means[i]) * invStd;
                float d = x - y;
                accum += d * d;
            }

            float distance = math.sqrt(accum * math.rcp(MFCCLength));
            return math.exp(-distance * LN10);
        }

        [BurstCompile]
        float CalcCosineSimilarityScoreSIMD(int index)
        {
            int baseOffset = index * MFCCLength;

            float prod = 0f;
            float nnx = 0f;
            float nny = 0f;

            int i = 0;
            int limit = MFCCLength & ~3;

            for (; i < limit; i += 4)
            {
                float4 invStd = new float4(
                    math.rcp(standardDeviations[i + 0] + EPS),
                    math.rcp(standardDeviations[i + 1] + EPS),
                    math.rcp(standardDeviations[i + 2] + EPS),
                    math.rcp(standardDeviations[i + 3] + EPS)
                );

                float4 xm = new float4(
                    mfcc[i + 0] - means[i + 0],
                    mfcc[i + 1] - means[i + 1],
                    mfcc[i + 2] - means[i + 2],
                    mfcc[i + 3] - means[i + 3]
                ) * invStd;

                float4 ym = new float4(
                    phonemes[baseOffset + i + 0] - means[i + 0],
                    phonemes[baseOffset + i + 1] - means[i + 1],
                    phonemes[baseOffset + i + 2] - means[i + 2],
                    phonemes[baseOffset + i + 3] - means[i + 3]
                ) * invStd;

                prod += math.dot(xm, ym);
                nnx += math.dot(xm, xm);
                nny += math.dot(ym, ym);
            }

            for (; i < MFCCLength; i++)
            {
                float invStd = math.rcp(standardDeviations[i] + EPS);
                float x = (mfcc[i] - means[i]) * invStd;
                float y = (phonemes[baseOffset + i] - means[i]) * invStd;
                prod += x * y;
                nnx += x * x;
                nny += y * y;
            }

            float denom = math.sqrt(nnx) * math.sqrt(nny) + EPS;
            float similarity = prod / denom;

            if (!IsFinite(similarity)) similarity = 0f;
            similarity = math.clamp(similarity, 0f, 1f);

            float s = math.max(similarity, EPS);
            float s2 = s * s;
            float s4 = s2 * s2;
            float s8 = s4 * s4;
            float s16 = s8 * s8;
            return s16;
        }

        [BurstCompile]
        int GetVowelOrRest()
        {
            int index = -1;
            float maxScore = -1f;

            for (int i = 0; i < ScoresLength; i++)
            {
                float s = scores[i];
                if (!IsFinite(s)) continue;
                if (s > maxScore)
                {
                    maxScore = s;
                    index = i;
                }
            }

            if (index < 0 || maxScore <= 0f)
            {
                int rest = SafeRestIndex(restPhonemeIndex, ScoresLength);
                OneHotRest(scores, rest);
                return rest;
            }

            return index;
        }
        public static void CopyRingBuffer(in NativeArray<float> src, NativeArray<float> dst, int startSrcIndex)
        {
            CopyRingBuffer((float*)src.GetUnsafeReadOnlyPtr(), (float*)dst.GetUnsafePtr(), src.Length, startSrcIndex);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CopyRingBuffer(float* input, float* output, int len, int startSrcIndex)
        {
            startSrcIndex %= len;
            if (startSrcIndex < 0) startSrcIndex += len;

            if (startSrcIndex == 0)
            {
                UnsafeUtility.MemCpy(output, input, (long)len * sizeof(float));
                return;
            }

            int first = len - startSrcIndex;
            UnsafeUtility.MemCpy(output, input + startSrcIndex, (long)first * sizeof(float));
            UnsafeUtility.MemCpy(output + first, input, (long)(len - first) * sizeof(float));
        }

        static void LowPassFilterInPlace_Precomputed(NativeArray<float> data, NativeArray<float> tmp, NativeArray<float> taps)
        {
            UnsafeUtility.MemCpy(tmp.GetUnsafePtr(), data.GetUnsafeReadOnlyPtr(), (long)data.Length * sizeof(float));

            LowPassFilterWithTaps(
                (float*)data.GetUnsafePtr(),
                data.Length,
                (float*)tmp.GetUnsafeReadOnlyPtr(),
                (float*)taps.GetUnsafeReadOnlyPtr(),
                taps.Length);
        }

        [BurstCompile]
        static void LowPassFilterWithTaps(float* data, int len, float* src, float* b, int bLen)
        {
            // keeping your original accumulation behavior
            for (int i = 0; i < len; i++)
            {
                float acc = data[i];
                for (int j = 0; j < bLen; j++)
                {
                    int k = i - j;
                    if (k >= 0)
                    {
                        acc += b[j] * src[k];
                    }
                    else
                    {
                        break;
                    }
                }
                data[i] = acc;
            }
        }

        public static void DownSample(in NativeArray<float> input, NativeArray<float> output, int sampleRate, int targetSampleRate)
        {
            if (sampleRate <= targetSampleRate)
            {
                UnsafeUtility.MemCpy(output.GetUnsafePtr(), input.GetUnsafeReadOnlyPtr(), (long)output.Length * sizeof(float));
                return;
            }

            if (sampleRate % targetSampleRate == 0)
            {
                int skip = sampleRate / targetSampleRate;
                DownSample1((float*)input.GetUnsafeReadOnlyPtr(), (float*)output.GetUnsafePtr(), output.Length, skip);
            }
            else
            {
                float df = (float)sampleRate / targetSampleRate;
                DownSample2((float*)input.GetUnsafeReadOnlyPtr(), input.Length, (float*)output.GetUnsafePtr(), output.Length, df);
            }
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DownSample1(float* input, float* output, int outputLen, int skip)
        {
            for (int i = 0; i < outputLen; i++)
            {
                output[i] = input[i * skip];
            }
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DownSample2(float* input, int inputLen, float* output, int outputLen, float df)
        {
            for (int j = 0; j < outputLen; j++)
            {
                float fIndex = df * j;
                int i0 = (int)math.floor(fIndex);
                int i1 = math.min(i0 + 1, inputLen - 1);

                float t = fIndex - i0;
                float x0 = input[i0];
                float x1 = input[i1];
                output[j] = math.lerp(x0, x1, t);
            }
        }

        static void PreEmphasisInPlace(NativeArray<float> data, NativeArray<float> tmp, float p)
        {
            UnsafeUtility.MemCpy(tmp.GetUnsafePtr(), data.GetUnsafeReadOnlyPtr(), (long)data.Length * sizeof(float));
            PreEmphasis((float*)data.GetUnsafePtr(), (float*)tmp.GetUnsafeReadOnlyPtr(), data.Length, p);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PreEmphasis(float* data, float* src, int len, float p)
        {
            for (int i = 1; i < len; i++)
            {
                data[i] = src[i] - p * src[i - 1];
            }
        }

        static void PrepareWindowedFrame(NativeArray<float> down, NativeArray<float> frame, NativeArray<float> window)
        {
            unsafe
            {
                float* src = (float*)down.GetUnsafeReadOnlyPtr();
                float* dst = (float*)frame.GetUnsafePtr();
                float* w = (float*)window.GetUnsafeReadOnlyPtr();

                int downLen = down.Length;
                int N = frame.Length;

                int i = 0;
                for (; i < downLen; i++)
                {
                    dst[i] = src[i];
                }

                for (; i < N; i++)
                {
                    dst[i] = 0f;
                }

                for (int k = 0; k < N; k++)
                {
                    dst[k] *= w[k];
                }
            }
        }

        static void NormalizeInPlace(NativeArray<float> array, float value = 1f)
        {
            Normalize((float*)array.GetUnsafePtr(), array.Length, value);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void Normalize(float* array, int len, float value = 1f)
        {
            float max = GetMaxValue(array, len);
            if (max < math.EPSILON) return;

            float r = value / max;
            float* p = array;
            float* end = p + len;

            for (; p + 4 <= end; p += 4)
            {
                p[0] *= r; p[1] *= r; p[2] *= r; p[3] *= r;
            }
            for (; p < end; p++)
                *p *= r;
        }

        static void FFTPowerHalf(NativeArray<float> timeFrame, NativeArray<float> powerHalf, NativeArray<float> re, NativeArray<float> im)
        {
            FFTPowerHalf(
                (float*)timeFrame.GetUnsafeReadOnlyPtr(),
                (float*)powerHalf.GetUnsafePtr(),
                (float*)re.GetUnsafePtr(),
                (float*)im.GetUnsafePtr(),
                timeFrame.Length);
        }

        [BurstCompile]
        static void FFTPowerHalf(float* input, float* powOut, float* re, float* im, int N)
        {
            for (int i = 0; i < N; i++) { re[i] = input[i]; im[i] = 0f; }

            for (int i = 1, j = 0; i < N; i++)
            {
                int bit = N >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;

                if (i < j)
                {
                    (re[j], re[i]) = (re[i], re[j]);
                    (im[j], im[i]) = (im[i], im[j]);
                }
            }

            for (int len = 2; len <= N; len <<= 1)
            {
                float ang = -2f * math.PI / len;
                float wlenRe = math.cos(ang);
                float wlenIm = math.sin(ang);

                for (int i = 0; i < N; i += len)
                {
                    float wRe = 1f;
                    float wIm = 0f;

                    int half = len >> 1;
                    for (int j = 0; j < half; j++)
                    {
                        int u = i + j;
                        int v = u + half;

                        float vr = re[v] * wRe - im[v] * wIm;
                        float vi = re[v] * wIm + im[v] * wRe;

                        float ur = re[u];
                        float ui = im[u];

                        re[u] = ur + vr;
                        im[u] = ui + vi;
                        re[v] = ur - vr;
                        im[v] = ui - vi;

                        float nwRe = wRe * wlenRe - wIm * wlenIm;
                        float nwIm = wRe * wlenIm + wIm * wlenRe;
                        wRe = nwRe;
                        wIm = nwIm;
                    }
                }
            }

            int halfOut = N >> 1;
            for (int i = 0; i <= halfOut; i++)
            {
                powOut[i] = re[i] * re[i] + im[i] * im[i];
            }
        }

        static void PowerToDbInPlace(NativeArray<float> array)
        {
            PowerToDb((float*)array.GetUnsafePtr(), array.Length);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PowerToDb(float* array, int len)
        {
            float* p = array;
            float* end = p + len;

            for (; p + 4 <= end; p += 4)
            {
                p[0] = 10f * math.log10(p[0]);
                p[1] = 10f * math.log10(p[1]);
                p[2] = 10f * math.log10(p[2]);
                p[3] = 10f * math.log10(p[3]);
            }
            for (; p < end; p++)
            {
                *p = 10f * math.log10(*p);
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        bool IsLowEnergy()
        {
            float acc = 0f;
            for (int i = 0; i < MFCCLength; i++)
            {
                acc += mfcc[i] * mfcc[i];
            }

            return acc <= 1e-8f;
        }

        static int SafeRestIndex(int rest, int len)
        {
            if (len <= 0) return 0;
            if (rest < 0 || rest >= len) return 0;
            return rest;
        }

        static void OneHotRest(NativeArray<float> s, int rest)
        {
            rest = SafeRestIndex(rest, s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                s[i] = 0f;
            }

            if (s.Length > 0)
            {
                s[rest] = 1f;
            }
        }

        public static float GetRMSVolume(in NativeArray<float> array)
        {
            return GetRMSVolume((float*)array.GetUnsafeReadOnlyPtr(), array.Length);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float GetRMSVolume(float* array, int len)
        {
            float sum = 0f;
            float* p = array;
            float* end = p + len;

            for (; p + 4 <= end; p += 4)
            {
                float x0 = p[0]; sum += x0 * x0;
                float x1 = p[1]; sum += x1 * x1;
                float x2 = p[2]; sum += x2 * x2;
                float x3 = p[3]; sum += x3 * x3;
            }
            for (; p < end; p++)
            {
                float x = *p;
                sum += x * x;
            }

            return math.sqrt(sum / math.max(1, len));
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float GetMaxValue(float* array, int len)
        {
            float max = 0f;
            float* p = array;
            float* end = p + len;

            for (; p + 4 <= end; p += 4)
            {
                float a0 = math.abs(p[0]);
                float a1 = math.abs(p[1]);
                float a2 = math.abs(p[2]);
                float a3 = math.abs(p[3]);
                max = math.max(max, a0);
                max = math.max(max, a1);
                max = math.max(max, a2);
                max = math.max(max, a3);
            }
            for (; p < end; p++)
            {
                max = math.max(max, math.abs(*p));
            }

            return max;
        }
    }
}
