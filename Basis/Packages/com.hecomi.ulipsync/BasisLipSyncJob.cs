using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace uLipSync
{

    // =======================================================
    // LipSyncJob with the optimizations
    // =======================================================
    [BurstCompile]
    public unsafe struct BasisLipSyncJob : IJob
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

        [ReadOnly] public BasisMelFilterPlan melPlan;
        [ReadOnly] public BasisDctPlan dctPlan;

        public NativeArray<float> mfcc;
        public NativeArray<float> scores;
        public NativeArray<Info> info;

        public BasisLipSyncWorkspace ws;

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
        static void ApplyMelPlan_BurstSafe(NativeArray<float> powerHalf, NativeArray<float> melOut, in BasisMelFilterPlan plan)
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

        static void DctMfccFromPlan_BurstSafe(NativeArray<float> melDb, NativeArray<float> mfccOut, in BasisDctPlan plan)
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
        static void FFTPowerHalf_Planned(NativeArray<float> timeFrame, NativeArray<float> powerHalf, NativeArray<float> re, NativeArray<float> im, in BasisFftPlan plan)
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
