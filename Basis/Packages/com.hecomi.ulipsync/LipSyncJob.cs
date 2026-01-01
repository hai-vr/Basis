using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace uLipSync
{
    // ============================
    // FFT Plan (precompute once)
    // ============================
    public struct FftPlan : IDisposable
    {
        public NativeArray<int> bitrev;       // [N]
        public NativeArray<int> stageOffsets; // [stages+1] offsets into twiddles
        public NativeArray<float> twRe;       // packed twiddles
        public NativeArray<float> twIm;

        public int N;
        public int stages;

        public bool IsCreated => bitrev.IsCreated && stageOffsets.IsCreated && twRe.IsCreated && twIm.IsCreated;

        public void Dispose()
        {
            if (bitrev.IsCreated) bitrev.Dispose();
            if (stageOffsets.IsCreated) stageOffsets.Dispose();
            if (twRe.IsCreated) twRe.Dispose();
            if (twIm.IsCreated) twIm.Dispose();
        }

        static int Log2Pow2(int n)
        {
            int s = 0;
            while ((1 << s) < n) s++;
            return s;
        }

        public static FftPlan Build(int N, Allocator alloc)
        {
            // N must be pow2
            int stages = Log2Pow2(N);

            // bit reversal indices
            var bitrev = new NativeArray<int>(N, alloc);
            for (int i = 0; i < N; i++)
            {
                int x = i;
                int r = 0;
                for (int b = 0; b < stages; b++)
                {
                    r = (r << 1) | (x & 1);
                    x >>= 1;
                }
                bitrev[i] = r;
            }

            // twiddles per stage:
            // for each stage len = 2..N, half=len/2, twiddle for j=0..half-1
            // pack all in one array to keep it Burst-friendly.
            int totalTw = 0;
            var stageOffsets = new NativeArray<int>(stages + 1, alloc);
            int len = 2;
            for (int s = 0; s < stages; s++, len <<= 1)
            {
                stageOffsets[s] = totalTw;
                totalTw += (len >> 1);
            }
            stageOffsets[stages] = totalTw;

            var twRe = new NativeArray<float>(totalTw, alloc);
            var twIm = new NativeArray<float>(totalTw, alloc);

            len = 2;
            for (int s = 0; s < stages; s++, len <<= 1)
            {
                int half = len >> 1;
                float ang = -2f * math.PI / len;
                for (int j = 0; j < half; j++)
                {
                    float a = ang * j;
                    int idx = stageOffsets[s] + j;
                    twRe[idx] = math.cos(a);
                    twIm[idx] = math.sin(a);
                }
            }

            return new FftPlan
            {
                N = N,
                stages = stages,
                bitrev = bitrev,
                stageOffsets = stageOffsets,
                twRe = twRe,
                twIm = twIm
            };
        }
    }

    // ============================
    // Mel + DCT plans unchanged
    // (your existing MelFilterPlan/DctPlan are good)
    // ============================

    public struct LipSyncWorkspace : IDisposable
    {
        public NativeArray<float> buffer;        // inputLen
        public NativeArray<float> down;          // downLen
        public NativeArray<float> frame;         // fftN
        public NativeArray<float> powerHalf;     // fftN/2+1
        public NativeArray<float> melSpectrum;   // melDiv

        public NativeArray<float> tmp;           // scratch >= max(inputLen, downLen, fftN)
        public NativeArray<float> fftRe;         // fftN
        public NativeArray<float> fftIm;         // fftN

        public NativeArray<float> firTaps;       // firLen (precomputed)
        public NativeArray<float> hammingWindow; // fftN (precomputed)

        // NEW: standardization scratch (per frame)
        public NativeArray<float> mfccZ;         // mfccLen

        // NEW: FFT plan
        public FftPlan fftPlan;

        public bool IsCreated =>
            buffer.IsCreated && down.IsCreated && frame.IsCreated &&
            powerHalf.IsCreated && melSpectrum.IsCreated &&
            tmp.IsCreated && fftRe.IsCreated && fftIm.IsCreated &&
            firTaps.IsCreated && hammingWindow.IsCreated &&
            mfccZ.IsCreated && fftPlan.IsCreated;

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
            if (mfccZ.IsCreated) mfccZ.Dispose();
            if (fftPlan.IsCreated) fftPlan.Dispose();
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
            int mfccLen,
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

                mfccZ = new NativeArray<float>(mfccLen, allocator),

                fftPlan = FftPlan.Build(fftN, allocator),
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
                for (int i = n; i < taps.Length; i++) b[i] = 0f;
            }
        }
    }
    // =======================================================
    // MelFilterPlan
    // - Precomputes sparse triangular mel filter bank mapping
    // - Packed as CSR-ish: starts/lengths + bins/weights
    // - Burst-friendly: apply via pointers
    // =======================================================
    public struct MelFilterPlan : IDisposable
    {
        public NativeArray<int> starts;     // [melDiv] start offset into bins/weights
        public NativeArray<int> lengths;    // [melDiv] number of weights per mel band
        public NativeArray<int> bins;       // [totalWeights] spectrum bin index per weight
        public NativeArray<float> weights;  // [totalWeights] weight per bin

        public int melDiv;
        public int fftN;
        public int specLen;
        public float sampleRate;

        public bool IsCreated =>
            starts.IsCreated && lengths.IsCreated &&
            bins.IsCreated && weights.IsCreated;

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
            // HTK-ish (1127) or Slaney-ish (2595). Both are common.
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
            fftN = math.max(2, fftN);
            melDiv = math.max(1, melDiv);

            int specLen = fftN / 2 + 1;

            float fMax = sampleRate * 0.5f;
            float melMax = ToMel(fMax, slaney);

            float df = fMax / (specLen - 1);      // Hz per bin
            float dMel = melMax / (melDiv + 1);   // mel spacing including endpoints

            // First pass: figure out total number of weights
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

            // Second pass: fill bins + weights
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

                // Normalization: keeps overall scale somewhat consistent.
                // (This is not the only possible normalization; just a sane one.)
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

    // =======================================================
    // DctPlan
    // - Precomputes cos table for MFCC DCT-II (skipping c0)
    // - Layout: row-major [mfccLen * melDiv]
    // - Burst-friendly: apply via pointers
    // =======================================================
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
            melDiv = math.max(1, melDiv);
            mfccLen = math.max(1, mfccLen);

            var plan = new DctPlan
            {
                melDiv = melDiv,
                mfccLen = mfccLen,
                cosTable = new NativeArray<float>(mfccLen * melDiv, alloc)
            };

            float a = math.PI / melDiv;

            // r = 0..mfccLen-1 corresponds to DCT index i=r+1 (skip c0)
            for (int r = 0; r < mfccLen; r++)
            {
                int i = r + 1;
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

    // =======================================================
    // LipSyncJob with the optimizations
    // =======================================================
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

        // NEW: standardized phonemes
        [ReadOnly] public NativeArray<float> phonemesZ; // [phonemeCount * mfccLen]

        [ReadOnly] public int restPhonemeIndex;

        [ReadOnly] public MelFilterPlan melPlan;
        [ReadOnly] public DctPlan dctPlan;

        public NativeArray<float> mfcc;
        public NativeArray<float> scores;
        public NativeArray<Info> info;

        public LipSyncWorkspace ws;

        const float EPS = 1e-12f;
        const float LN10 = 2.302585092994046f;
        const float DB_SCALE = 10f / LN10;     // 10*log10(x) = (10/LN10)*ln(x)
        const float PREEMPH = 0.97f;

        int ScoresLength => scores.Length;
        int MFCCLength => mfcc.Length;

        public void Execute()
        {
            // 1) Copy ring -> ws.buffer
            CopyRingBuffer(input, ws.buffer, startIndex);

            // 2) FIR lowpass (FIXED: proper convolution, no double-add)
            LowPassFilterInPlace_Precomputed(ws.buffer, ws.tmp, ws.firTaps);

            // NEW: early silence check (cheap RMS on ws.buffer)
            float rms = GetRMSVolume(ws.buffer);
            if (rms < 1e-4f) // tune threshold
            {
                int rest = SafeRestIndex(restPhonemeIndex, ScoresLength);
                OneHotRest(scores, rest);
                info[0] = new Info { volume = rms, mainPhonemeIndex = rest };
                // keep mfcc stable-ish
                for (int i = 0; i < MFCCLength; i++) mfcc[i] = 0f;
                return;
            }

            // 3) Downsample + PreEmphasis fused into ws.down (no tmp memcpy)
            DownSampleAndPreEmphasis(ws.buffer, ws.down, outputSampleRate, targetSampleRate, PREEMPH);

            // 4) Prepare FFT frame + window
            PrepareWindowedFrame(ws.down, ws.frame, ws.hammingWindow);

            // 5) FFT power spectrum half using precomputed plan
            FFTPowerHalf_Planned(ws.frame, ws.powerHalf, ws.fftRe, ws.fftIm, ws.fftPlan);

            // 6) Mel filterbank
            ApplyMelPlan_BurstSafe(ws.powerHalf, ws.melSpectrum, melPlan);

            // floor to EPS
            for (int k = 0; k < ws.melSpectrum.Length; k++)
                ws.melSpectrum[k] = math.max(ws.melSpectrum[k], EPS);

            // 7) power -> dB using ln
            PowerToDbLnInPlace(ws.melSpectrum);

            // 8) DCT -> mfcc
            DctMfccFromPlan_BurstSafe(ws.melSpectrum, mfcc, dctPlan);

            // sanitize + standardize mfccZ once
            StandardizeMfccToZ(mfcc, ws.mfccZ, means, standardDeviations);

            // 9) Score against standardized phonemesZ (fast)
            CalcScoresAgainstPhonemesZ(ws.mfccZ);

            int winner = GetMaxIndexOrRest();
            info[0] = new Info { volume = rms, mainPhonemeIndex = winner };
        }

        // -------------------------------
        // Burst-safe plan applications
        // -------------------------------
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
        static unsafe void ApplyMelPlanPtr(float* powerHalf, float* melOut,
            int* starts, int* lengths, int* bins, float* weights, int melDiv)
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
        static unsafe void DctMfccFromCosTablePtr(float* melDb, float* mfccOut, float* cosTable, int melDiv, int mfccLen)
        {
            for (int r = 0; r < mfccLen; r++)
            {
                float sum = 0f;
                int baseIdx = r * melDiv;
                for (int j = 0; j < melDiv; j++) sum += melDb[j] * cosTable[baseIdx + j];
                mfccOut[r] = sum;
            }
        }

        // -------------------------------
        // Ring copy
        // -------------------------------
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

        // -------------------------------
        // FIR lowpass (fixed + predictable)
        // -------------------------------
        static void LowPassFilterInPlace_Precomputed(NativeArray<float> data, NativeArray<float> tmp, NativeArray<float> taps)
        {
            UnsafeUtility.MemCpy(tmp.GetUnsafePtr(), data.GetUnsafeReadOnlyPtr(), (long)data.Length * sizeof(float));
            LowPassFilterWithTaps(
                (float*)data.GetUnsafePtr(), data.Length,
                (float*)tmp.GetUnsafeReadOnlyPtr(),
                (float*)taps.GetUnsafeReadOnlyPtr(), taps.Length);
        }

        [BurstCompile]
        static void LowPassFilterWithTaps(float* dst, int len, float* src, float* b, int bLen)
        {
            // Proper FIR: dst[i] = sum_j b[j] * src[i-j]
            for (int i = 0; i < len; i++)
            {
                float acc = 0f;
                int maxJ = math.min(bLen, i + 1);
                for (int j = 0; j < maxJ; j++)
                    acc += b[j] * src[i - j];
                dst[i] = acc;
            }
        }

        // -------------------------------
        // Downsample + PreEmphasis fused
        // -------------------------------
        static void DownSampleAndPreEmphasis(in NativeArray<float> input, NativeArray<float> output, int sampleRate, int targetSampleRate, float p)
        {
            if (sampleRate <= targetSampleRate)
            {
                // copy + preemph in one pass
                unsafe
                {
                    float* src = (float*)input.GetUnsafeReadOnlyPtr();
                    float* dst = (float*)output.GetUnsafePtr();
                    int n = output.Length;
                    if (n <= 0) return;

                    dst[0] = src[0];
                    for (int i = 1; i < n; i++)
                        dst[i] = src[i] - p * src[i - 1];
                }
                return;
            }

            if (sampleRate % targetSampleRate == 0)
            {
                int skip = sampleRate / targetSampleRate;
                unsafe
                {
                    float* src = (float*)input.GetUnsafeReadOnlyPtr();
                    float* dst = (float*)output.GetUnsafePtr();
                    int n = output.Length;
                    if (n <= 0) return;

                    float prev = src[0];
                    dst[0] = prev;

                    for (int i = 1; i < n; i++)
                    {
                        float cur = src[i * skip];
                        dst[i] = cur - p * prev;
                        prev = cur;
                    }
                }
            }
            else
            {
                float df = (float)sampleRate / targetSampleRate;
                unsafe
                {
                    float* src = (float*)input.GetUnsafeReadOnlyPtr();
                    float* dst = (float*)output.GetUnsafePtr();
                    int n = output.Length;
                    int inLen = input.Length;
                    if (n <= 0) return;

                    // j=0
                    float fIndex0 = 0f;
                    int i0 = 0;
                    float x0 = src[0];
                    dst[0] = x0;
                    float prev = x0;

                    for (int j = 1; j < n; j++)
                    {
                        float fIndex = df * j;
                        int a = (int)math.floor(fIndex);
                        int b = math.min(a + 1, inLen - 1);
                        float t = fIndex - a;

                        float cur = math.lerp(src[a], src[b], t);
                        dst[j] = cur - p * prev;
                        prev = cur;
                    }
                }
            }
        }

        // -------------------------------
        // Windowed frame
        // -------------------------------
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
                for (; i < downLen; i++) dst[i] = src[i];
                for (; i < N; i++) dst[i] = 0f;

                for (int k = 0; k < N; k++) dst[k] *= w[k];
            }
        }

        // -------------------------------
        // Planned FFT
        // -------------------------------
        static void FFTPowerHalf_Planned(NativeArray<float> timeFrame, NativeArray<float> powerHalf, NativeArray<float> re, NativeArray<float> im, in FftPlan plan)
        {
            FFTPowerHalf_Planned(
                (float*)timeFrame.GetUnsafeReadOnlyPtr(),
                (float*)powerHalf.GetUnsafePtr(),
                (float*)re.GetUnsafePtr(),
                (float*)im.GetUnsafePtr(),
                (int*)plan.bitrev.GetUnsafeReadOnlyPtr(),
                (int*)plan.stageOffsets.GetUnsafeReadOnlyPtr(),
                (float*)plan.twRe.GetUnsafeReadOnlyPtr(),
                (float*)plan.twIm.GetUnsafeReadOnlyPtr(),
                plan.N,
                plan.stages);
        }

        [BurstCompile]
        static unsafe void FFTPowerHalf_Planned(
            float* input,
            float* powOut,
            float* re,
            float* im,
            int* bitrev,
            int* stageOffsets,
            float* twRe,
            float* twIm,
            int N,
            int stages)
        {
            // bitrev reorder into re/im
            for (int i = 0; i < N; i++)
            {
                int j = bitrev[i];
                re[i] = input[j];
                im[i] = 0f;
            }

            int len = 2;
            for (int s = 0; s < stages; s++, len <<= 1)
            {
                int half = len >> 1;
                int twOff = stageOffsets[s];

                for (int i = 0; i < N; i += len)
                {
                    for (int j = 0; j < half; j++)
                    {
                        int u = i + j;
                        int v = u + half;

                        float wRe = twRe[twOff + j];
                        float wIm = twIm[twOff + j];

                        float vr = re[v] * wRe - im[v] * wIm;
                        float vi = re[v] * wIm + im[v] * wRe;

                        float ur = re[u];
                        float ui = im[u];

                        re[u] = ur + vr;
                        im[u] = ui + vi;
                        re[v] = ur - vr;
                        im[v] = ui - vi;
                    }
                }
            }

            int halfOut = N >> 1;
            for (int i = 0; i <= halfOut; i++)
                powOut[i] = re[i] * re[i] + im[i] * im[i];
        }

        // -------------------------------
        // Power -> dB using ln (faster than log10)
        // -------------------------------
        static void PowerToDbLnInPlace(NativeArray<float> array)
        {
            unsafe
            {
                float* p = (float*)array.GetUnsafePtr();
                int len = array.Length;
                for (int i = 0; i < len; i++)
                    p[i] = DB_SCALE * math.log(math.max(p[i], EPS));
            }
        }

        // -------------------------------
        // Standardize mfcc once -> z
        // -------------------------------
        static void StandardizeMfccToZ(NativeArray<float> mfcc, NativeArray<float> z, NativeArray<float> means, NativeArray<float> std)
        {
            int n = mfcc.Length;
            for (int i = 0; i < n; i++)
            {
                float v = mfcc[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) v = 0f;
                mfcc[i] = v;

                float inv = math.rcp(std[i] + EPS);
                z[i] = (v - means[i]) * inv;
            }
        }

        // -------------------------------
        // Scoring using standardized vectors
        // -------------------------------
        void CalcScoresAgainstPhonemesZ(NativeArray<float> z)
        {
            float sum = 0f;

            switch (compareMethod)
            {
                case CompareMethod.L1Norm:
                    for (int p = 0; p < ScoresLength; p++)
                    {
                        float s = ScoreL1_Z(p, z);
                        scores[p] = s;
                        sum += s;
                    }
                    break;

                case CompareMethod.L2Norm:
                    for (int p = 0; p < ScoresLength; p++)
                    {
                        float s = ScoreL2_Z(p, z);
                        scores[p] = s;
                        sum += s;
                    }
                    break;

                case CompareMethod.CosineSimilarity:
                    for (int p = 0; p < ScoresLength; p++)
                    {
                        float s = ScoreCos_Z(p, z);
                        scores[p] = s;
                        sum += s;
                    }
                    break;

                default:
                    for (int p = 0; p < ScoresLength; p++) scores[p] = 0f;
                    sum = 0f;
                    break;
            }

            if (sum > 0f && !(float.IsNaN(sum) || float.IsInfinity(sum)))
            {
                float inv = math.rcp(sum);
                for (int i = 0; i < ScoresLength; i++) scores[i] *= inv;
            }
        }

        [BurstCompile]
        float ScoreL1_Z(int index, NativeArray<float> z)
        {
            int baseOff = index * MFCCLength;
            float acc = 0f;

            int i = 0;
            int limit = MFCCLength & ~3;

            for (; i < limit; i += 4)
            {
                float4 a = new float4(z[i], z[i + 1], z[i + 2], z[i + 3]);
                float4 b = new float4(
                    phonemesZ[baseOff + i],
                    phonemesZ[baseOff + i + 1],
                    phonemesZ[baseOff + i + 2],
                    phonemesZ[baseOff + i + 3]
                );
                float4 d = math.abs(a - b);
                acc += d.x + d.y + d.z + d.w;
            }

            for (; i < MFCCLength; i++)
                acc += math.abs(z[i] - phonemesZ[baseOff + i]);

            float distance = acc * math.rcp(MFCCLength);
            // keep your original exp mapping
            return math.exp(-distance * LN10);
        }

        [BurstCompile]
        float ScoreL2_Z(int index, NativeArray<float> z)
        {
            int baseOff = index * MFCCLength;
            float acc = 0f;

            int i = 0;
            int limit = MFCCLength & ~3;

            for (; i < limit; i += 4)
            {
                float4 a = new float4(z[i], z[i + 1], z[i + 2], z[i + 3]);
                float4 b = new float4(
                    phonemesZ[baseOff + i],
                    phonemesZ[baseOff + i + 1],
                    phonemesZ[baseOff + i + 2],
                    phonemesZ[baseOff + i + 3]
                );
                float4 d = a - b;
                acc += math.dot(d, d);
            }

            for (; i < MFCCLength; i++)
            {
                float d = z[i] - phonemesZ[baseOff + i];
                acc += d * d;
            }

            float distance = math.sqrt(acc * math.rcp(MFCCLength));
            return math.exp(-distance * LN10);
        }

        [BurstCompile]
        float ScoreCos_Z(int index, NativeArray<float> z)
        {
            int baseOff = index * MFCCLength;

            float prod = 0f;
            float nnx = 0f;
            float nny = 0f;

            int i = 0;
            int limit = MFCCLength & ~3;

            for (; i < limit; i += 4)
            {
                float4 a = new float4(z[i], z[i + 1], z[i + 2], z[i + 3]);
                float4 b = new float4(
                    phonemesZ[baseOff + i],
                    phonemesZ[baseOff + i + 1],
                    phonemesZ[baseOff + i + 2],
                    phonemesZ[baseOff + i + 3]
                );

                prod += math.dot(a, b);
                nnx += math.dot(a, a);
                nny += math.dot(b, b);
            }

            for (; i < MFCCLength; i++)
            {
                float a = z[i];
                float b = phonemesZ[baseOff + i];
                prod += a * b;
                nnx += a * a;
                nny += b * b;
            }

            float denom = math.sqrt(nnx) * math.sqrt(nny) + EPS;
            float sim = prod / denom;
            if (float.IsNaN(sim) || float.IsInfinity(sim)) sim = 0f;
            sim = math.clamp(sim, 0f, 1f);

            // keep your sharpness curve
            float s = math.max(sim, EPS);
            float s2 = s * s;
            float s4 = s2 * s2;
            float s8 = s4 * s4;
            float s16 = s8 * s8;
            return s16;
        }

        int GetMaxIndexOrRest()
        {
            int idx = -1;
            float best = -1f;

            for (int i = 0; i < ScoresLength; i++)
            {
                float s = scores[i];
                if (s > best)
                {
                    best = s;
                    idx = i;
                }
            }

            if (idx < 0 || best <= 0f)
            {
                int rest = SafeRestIndex(restPhonemeIndex, ScoresLength);
                OneHotRest(scores, rest);
                return rest;
            }

            return idx;
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
            for (int i = 0; i < s.Length; i++) s[i] = 0f;
            if (s.Length > 0) s[rest] = 1f;
        }

        public static float GetRMSVolume(in NativeArray<float> array) => GetRMSVolume((float*)array.GetUnsafeReadOnlyPtr(), array.Length);

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
    }
}
