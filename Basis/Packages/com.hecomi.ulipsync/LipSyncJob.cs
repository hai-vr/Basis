using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace uLipSync
{

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
        [ReadOnly] public int melFilterBankChannels;
        [ReadOnly] public CompareMethod compareMethod;
        [ReadOnly] public NativeArray<float> means;
        [ReadOnly] public NativeArray<float> standardDeviations;
        [ReadOnly] public NativeArray<float> phonemes;

        // NEW: Silence handling (set these from your caller)
        [ReadOnly] public int restPhonemeIndex;    // index of neutral/closed mouth

        public NativeArray<float> mfcc;
        public NativeArray<float> scores;
        public NativeArray<Info> info;

        private const int range = 500;

        // constants
        const float EPS = 1e-12f;
        const float LN10 = 2.302585092994046f;
        public int ScoresLength;
        public int MFCCLength;
        public void Execute()
        {
            ScoresLength = scores.Length;
            MFCCLength = mfcc.Length;

            CopyRingBuffer(input, out var buffer, startIndex);
            LowPassFilter(ref buffer, outputSampleRate, targetSampleRate / 2, range);
            DownSample(buffer, out var data, outputSampleRate, targetSampleRate);
            PreEmphasis(ref data, 0.97f);
            HammingWindow(ref data);
            Normalize(ref data, 1f);
            FFT(data, out var spectrum);
            MelFilterBank(spectrum, out var melSpectrum, targetSampleRate, melFilterBankChannels);

            int length = melSpectrum.Length;
            // Floor powers before dB to avoid -inf and NaNs on silence/near-silence
            for (int k = 0; k < length; ++k)
            {
                melSpectrum[k] = math.max(melSpectrum[k], EPS);
            }

            PowerToDb(ref melSpectrum);
            DCT(melSpectrum, out var melCepstrum);

            // Copy MFCCs (skip c0) and sanitize to finite values

            for (int INdex = 0; INdex < MFCCLength; ++INdex)
            {
                float v = melCepstrum[INdex + 1];
                mfcc[INdex] = IsFinite(v) ? v : 0f;
            }

            // If MFCC energy is basically gone (e.g., after strong normalization), fall back to rest
            if (IsLowEnergy())
            {
                OneHotRest(scores, restPhonemeIndex);
                info[0] = new Info { volume = GetRMSVolume(input), mainPhonemeIndex = SafeRestIndex(restPhonemeIndex, ScoresLength) };
                buffer.Dispose();
                data.Dispose();
                spectrum.Dispose();
                melSpectrum.Dispose();
                melCepstrum.Dispose();
                return;
            }

            CalcScoresSIMD();

            int winner = GetVowelOrRest();

            info[0] = new Info
            {
                volume = GetRMSVolume(input),
                mainPhonemeIndex = winner
            };
            buffer.Dispose();
            data.Dispose();
            spectrum.Dispose();
            melSpectrum.Dispose();
            melCepstrum.Dispose();
        }

        // ---------- Optimized + hardened scoring ----------

        [BurstCompile]
        void CalcScoresSIMD()
        {
            float sum = 0f;

            for (int i = 0; i < ScoresLength; ++i)
            {
                float s = CalcScoreSIMD(i);
                // sanitize: replace non-finite/negative with 0
                if (!IsFinite(s) || s < 0f) s = 0f;
                scores[i] = s;
                sum += s;
            }

            // Normalize; if everything is zero, leave it for fallback to rest
            if (sum > 0f && IsFinite(sum))
            {
                float inv = math.rcp(sum);
                for (int i = 0; i < ScoresLength; ++i)
                {
                    scores[i] *= inv;
                }
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

            for (; i < MFCCLength; ++i)
            {
                float invStd = math.rcp(standardDeviations[i] + EPS);
                float x = (mfcc[i] - means[i]) * invStd;
                float y = (phonemes[baseOffset + i] - means[i]) * invStd;
                accum += math.abs(x - y);
            }

            float distance = accum * math.rcp(MFCCLength);
            return math.exp(-distance * LN10); // 10^(-d)
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

            for (; i < MFCCLength; ++i)
            {
                float invStd = math.rcp(standardDeviations[i] + EPS);
                float x = (mfcc[i] - means[i]) * invStd;
                float y = (phonemes[baseOffset + i] - means[i]) * invStd;
                float d = x - y;
                accum += d * d;
            }

            float distance = math.sqrt(accum * math.rcp(MFCCLength));
            return math.exp(-distance * LN10); // 10^(-d)
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

            for (; i < MFCCLength; ++i)
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

            // Clamp to [0,1] and make finite
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

            for (int i = 0; i < ScoresLength; ++i)
            {
                float s = scores[i];
                if (!IsFinite(s)) continue; // ignore bad values
                if (s > maxScore)
                {
                    maxScore = s;
                    index = i;
                }
            }

            // Fallback if everything is 0/NaN
            if (index < 0 || maxScore <= 0f)
            {
                int rest = SafeRestIndex(restPhonemeIndex, ScoresLength);
                OneHotRest(scores, rest);
                return rest;
            }

            return index;
        }

        // ---------- Small helpers ----------

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        // Treat frames with vanishing MFCC energy as silence too
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
            for (int i = 0; i < s.Length; ++i) s[i] = 0f;
            if (s.Length > 0) s[rest] = 1f;
        }
        public static float GetMaxValue(in NativeArray<float> array)
        {
            return GetMaxValue((float*)array.GetUnsafeReadOnlyPtr(), array.Length);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float GetMaxValue(float* array, int len)
        {
            float max = 0f;
            float* p = array;
            float* end = p + len;

            // Unroll by 4 for better throughput
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

            // Unroll by 4
            for (; p + 4 <= end; p += 4)
            {
                float x0 = p[0]; sum += x0 * x0;
                float x1 = p[1]; sum += x1 * x1;
                float x2 = p[2]; sum += x2 * x2;
                float x3 = p[3]; sum += x3 * x3;
            }
            for (; p < end; p++)
            {
                float x = *p; sum += x * x;
            }

            return math.sqrt(sum / len);
        }

        // ---------- Ring buffer copy ---------------------------------------------

        public static void CopyRingBuffer(in NativeArray<float> input, out NativeArray<float> output, int startSrcIndex)
        {
            int len = input.Length;
            output = new NativeArray<float>(len, Allocator.Temp);

            CopyRingBuffer(
                (float*)input.GetUnsafeReadOnlyPtr(),
                (float*)output.GetUnsafePtr(),
                len,
                startSrcIndex);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void CopyRingBuffer(float* input, float* output, int len, int startSrcIndex)
        {
            // Fast path: no wrap
            startSrcIndex %= len;
            if (startSrcIndex < 0) startSrcIndex += len;

            if (startSrcIndex == 0)
            {
                UnsafeUtility.MemCpy(output, input, (long)len * sizeof(float));
                return;
            }

            // Split copy: [start..end) then [0..start)
            int first = len - startSrcIndex;
            UnsafeUtility.MemCpy(output, input + startSrcIndex, (long)first * sizeof(float));
            UnsafeUtility.MemCpy(output + first, input, (long)(len - first) * sizeof(float));
        }

        // ---------- Normalize -----------------------------------------------------

        public static void Normalize(ref NativeArray<float> array, float value = 1f)
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

            // Unroll by 4
            for (; p + 4 <= end; p += 4)
            {
                p[0] *= r; p[1] *= r; p[2] *= r; p[3] *= r;
            }
            for (; p < end; p++) *p *= r;
        }

        // ---------- Low-pass FIR --------------------------------------------------

        public static void LowPassFilter(ref NativeArray<float> data, float sampleRate, float cutoff, float range)
        {
            // Keep original behavior and math; just reduce overhead in inner loops.
            cutoff = (cutoff - range) / sampleRate;
            range /= sampleRate;

            var tmp = new NativeArray<float>(data, Allocator.Temp);

            int n = (int)math.round(3.1f / range);
            if ((n + 1) % 2 == 0) n += 1; // keep original parity logic
            var b = new NativeArray<float>(n, Allocator.Temp);

            LowPassFilter(
                (float*)data.GetUnsafePtr(),
                data.Length,
                cutoff,
                (float*)tmp.GetUnsafeReadOnlyPtr(),
                (float*)b.GetUnsafePtr(),
                n);

            tmp.Dispose();
            b.Dispose();
        }

        [BurstCompile]
        static void LowPassFilter(float* data, int len, float cutoff, float* tmp, float* b, int bLen)
        {
            // Compute taps (same formula/order as original)
            float half = (bLen - 1) * 0.5f;
            for (int i = 0; i < bLen; ++i)
            {
                float x = i - half;
                float ang = 2f * math.PI * cutoff * x;
                // Original formula: b[i] = 2f * cutoff * sin(ang) / ang;
                // Keep behavior exactly the same (no special-case at ang==0).
                b[i] = 2f * cutoff * math.sin(ang) / ang;
            }

            // Convolution (causal, same indices and accumulation order)
            for (int i = 0; i < len; ++i)
            {
                float acc = data[i]; // preserve original accumulation into data[i]
                for (int j = 0; j < bLen; ++j)
                {
                    int k = i - j;
                    if (k >= 0)
                    {
                        acc += b[j] * tmp[k];
                    }
                    else break; // remaining j will only be more negative
                }
                data[i] = acc;
            }
        }

        // ---------- Downsample ----------------------------------------------------

        public static void DownSample(in NativeArray<float> input, out NativeArray<float> output, int sampleRate, int targetSampleRate)
        {
            if (sampleRate <= targetSampleRate)
            {
                output = new NativeArray<float>(input, Allocator.Temp);
            }
            else if (sampleRate % targetSampleRate == 0)
            {
                int skip = sampleRate / targetSampleRate;
                int outLen = input.Length / skip;
                output = new NativeArray<float>(outLen, Allocator.Temp);
                DownSample1(
                    (float*)input.GetUnsafeReadOnlyPtr(),
                    (float*)output.GetUnsafePtr(),
                    outLen,
                    skip);
            }
            else
            {
                float df = (float)sampleRate / targetSampleRate;
                int n = (int)math.round(input.Length / df);
                output = new NativeArray<float>(n, Allocator.Temp);
                DownSample2(
                    (float*)input.GetUnsafeReadOnlyPtr(),
                    input.Length,
                    (float*)output.GetUnsafePtr(),
                    n,
                    df);
            }
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DownSample1(float* input, float* output, int outputLen, int skip)
        {
            // Strided gather (unchanged behavior)
            for (int i = 0; i < outputLen; ++i)
            {
                output[i] = input[i * skip];
            }
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DownSample2(float* input, int inputLen, float* output, int outputLen, float df)
        {
            // Keep exact original math/indices
            for (int j = 0; j < outputLen; ++j)
            {
                float fIndex = df * j;
                int i0 = (int)math.floor(fIndex);
                int i1 = math.min(i0, inputLen - 1);
                float t = fIndex - i0;
                float x0 = input[i0];
                float x1 = input[i1];
                output[j] = math.lerp(x0, x1, t);
            }
        }

        // ---------- Pre-Emphasis --------------------------------------------------

        public static void PreEmphasis(ref NativeArray<float> data, float p)
        {
            var tmp = new NativeArray<float>(data, Allocator.Temp);
            PreEmphasis(
                (float*)data.GetUnsafePtr(),
                (float*)tmp.GetUnsafeReadOnlyPtr(),
                data.Length,
                p);
            tmp.Dispose();
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PreEmphasis(float* data, float* tmp, int len, float p)
        {
            for (int i = 1; i < len; ++i)
            {
                data[i] = tmp[i] - p * tmp[i - 1];
            }
        }

        // ---------- Hamming Window ------------------------------------------------

        public static void HammingWindow(ref NativeArray<float> array)
        {
            HammingWindow((float*)array.GetUnsafePtr(), array.Length);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void HammingWindow(float* array, int len)
        {
            float inv = 1f / (len - 1);
            for (int i = 0; i < len; ++i)
            {
                float x = i * inv;
                array[i] *= 0.54f - 0.46f * math.cos(2f * math.PI * x);
            }
        }

        // ---------- Zero Padding --------------------------------------------------

        public static void ZeroPadding(ref NativeArray<float> data, out NativeArray<float> dataWithPadding)
        {
            int N = data.Length;
            dataWithPadding = new NativeArray<float>(N * 2, Allocator.Temp);

            // Zero first quarter
            var slice1 = new NativeSlice<float>(dataWithPadding, 0, N / 2);
            UnsafeUtility.MemSet(slice1.GetUnsafePtr<float>(), 0, (long)sizeof(float) * slice1.Length);

            // Copy middle half
            var slice2 = new NativeSlice<float>(dataWithPadding, N / 2, N);
            slice2.CopyFrom(data);

            // Zero last quarter
            var slice3 = new NativeSlice<float>(dataWithPadding, N * 3 / 2, N / 2);
            UnsafeUtility.MemSet(slice3.GetUnsafePtr<float>(), 0, (long)sizeof(float) * slice3.Length);
        }

        // ---------- FFT & Spectrum -----------------------------------------------

        public static void FFT(in NativeArray<float> data, out NativeArray<float> spectrum)
        {
            int N = data.Length;
            spectrum = new NativeArray<float>(N, Allocator.Temp);
            FFT((float*)data.GetUnsafePtr(), (float*)spectrum.GetUnsafePtr(), N);
        }

        [BurstCompile]
        static void FFT(float* data, float* spectrum, int N)
        {
            var spectrumRe = new NativeArray<float>(N, Allocator.Temp);
            var spectrumIm = new NativeArray<float>(N, Allocator.Temp);

            // Faster block copy
            UnsafeUtility.MemCpy((float*)spectrumRe.GetUnsafePtr(), data, (long)N * sizeof(float));
            _FFT((float*)spectrumRe.GetUnsafePtr(), (float*)spectrumIm.GetUnsafePtr(), N);

            // Magnitude
            float* re = (float*)spectrumRe.GetUnsafePtr();
            float* im = (float*)spectrumIm.GetUnsafePtr();
            float* outp = spectrum;
            float* end = outp + N;

            for (int i = 0; i < N; ++i)
            {
                float rr = re[i];
                float ii = im[i];
                outp[i] = math.length(new float2(rr, ii));
            }

            spectrumRe.Dispose();
            spectrumIm.Dispose();
        }

        [BurstCompile]
        static void _FFT(float* spectrumRe, float* spectrumIm, int N)
        {
            // Keep original recursive algorithm and math to preserve exact results.
            if (N < 2) return;

            int half = N >> 1;

            var evenRe = new NativeArray<float>(half, Allocator.Temp);
            var evenIm = new NativeArray<float>(half, Allocator.Temp);
            var oddRe = new NativeArray<float>(half, Allocator.Temp);
            var oddIm = new NativeArray<float>(half, Allocator.Temp);

            float* eRe = (float*)evenRe.GetUnsafePtr();
            float* eIm = (float*)evenIm.GetUnsafePtr();
            float* oRe = (float*)oddRe.GetUnsafePtr();
            float* oIm = (float*)oddIm.GetUnsafePtr();

            // Deinterleave
            for (int i = 0, j = 0; i < half; ++i, j += 2)
            {
                eRe[i] = spectrumRe[j];
                eIm[i] = spectrumIm[j];
                oRe[i] = spectrumRe[j + 1];
                oIm[i] = spectrumIm[j + 1];
            }

            _FFT(eRe, eIm, half);
            _FFT(oRe, oIm, half);

            // Combine
            for (int i = 0; i < half; ++i)
            {
                float er = eRe[i];
                float ei = eIm[i];
                float orr = oRe[i];
                float oi = oIm[i];
                float theta = -2f * math.PI * i / N;

                float c = math.cos(theta);
                float s = math.sin(theta);

                float tr = c * orr - s * oi;
                float ti = c * oi + s * orr;

                spectrumRe[i] = er + tr;
                spectrumIm[i] = ei + ti;
                spectrumRe[half + i] = er - tr;
                spectrumIm[half + i] = ei - ti;
            }

            evenRe.Dispose();
            evenIm.Dispose();
            oddRe.Dispose();
            oddIm.Dispose();
        }

        // ---------- Mel Filter Bank ----------------------------------------------

        public static void MelFilterBank(
            in NativeArray<float> spectrum,
            out NativeArray<float> melSpectrum,
            float sampleRate,
            int melDiv)
        {
            melSpectrum = new NativeArray<float>(melDiv, Allocator.Temp);
            MelFilterBank(
                (float*)spectrum.GetUnsafeReadOnlyPtr(),
                (float*)melSpectrum.GetUnsafePtr(),
                spectrum.Length,
                sampleRate,
                melDiv);
        }

        [BurstCompile]
        static void MelFilterBank(
            float* spectrum,
            float* melSpectrum,
            int len,
            float sampleRate,
            int melDiv)
        {
            float fMax = sampleRate * 0.5f;
            float melMax = ToMel(fMax);
            int nMax = len / 2;
            float df = fMax / nMax;
            float dMel = melMax / (melDiv + 1);

            for (int n = 0; n < melDiv; ++n)
            {
                float melBegin = dMel * n;
                float melCenter = dMel * (n + 1);
                float melEnd = dMel * (n + 2);

                float fBegin = ToHz(melBegin);
                float fCenter = ToHz(melCenter);
                float fEnd = ToHz(melEnd);

                int iBegin = (int)math.ceil(fBegin / df);
                int iCenter = (int)math.round(fCenter / df);
                int iEnd = (int)math.floor(fEnd / df);

                float denomL = (fCenter - fBegin);
                float denomR = (fEnd - fCenter);
                float norm = 0.5f / (fEnd - fBegin); // same as original (a /= (fEnd - fBegin) * 0.5f)

                float sum = 0f;
                for (int i = iBegin + 1; i <= iEnd; ++i)
                {
                    float f = df * i;
                    float a = (i < iCenter) ? ((f - fBegin) / denomL) : ((fEnd - f) / denomR);
                    a *= norm;
                    sum += a * spectrum[i];
                }
                melSpectrum[n] = sum;
            }
        }

        // ---------- Power -> dB ---------------------------------------------------

        public static void PowerToDb(ref NativeArray<float> array)
        {
            PowerToDb((float*)array.GetUnsafePtr(), array.Length);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PowerToDb(float* array, int len)
        {
            float* p = array;
            float* end = p + len;

            // Unroll by 4
            for (; p + 4 <= end; p += 4)
            {
                p[0] = 10f * math.log10(p[0]);
                p[1] = 10f * math.log10(p[1]);
                p[2] = 10f * math.log10(p[2]);
                p[3] = 10f * math.log10(p[3]);
            }
            for (; p < end; p++) *p = 10f * math.log10(*p);
        }

        // ---------- Mel / Hz ------------------------------------------------------

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float ToMel(float hz, bool slaney = false)
        {
            float a = slaney ? 2595f : 1127f;
            return a * math.log(hz / 700f + 1f);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float ToHz(float mel, bool slaney = false)
        {
            float a = slaney ? 2595f : 1127f;
            return 700f * (math.exp(mel / a) - 1f);
        }

        // ---------- DCT -----------------------------------------------------------

        public static void DCT(in NativeArray<float> spectrum, out NativeArray<float> cepstrum)
        {
            cepstrum = new NativeArray<float>(spectrum.Length, Allocator.Temp);
            DCT(
                (float*)spectrum.GetUnsafeReadOnlyPtr(),
                (float*)cepstrum.GetUnsafePtr(),
                spectrum.Length);
        }

        [BurstCompile]
        static void DCT(float* spectrum, float* cepstrum, int len)
        {
            // Keep exact math; reduce repeated ops
            float a = math.PI / len;

            for (int i = 0; i < len; ++i)
            {
                float sum = 0f;
                // Inner loop will be auto-vectorized by Burst when possible
                for (int j = 0; j < len; ++j)
                {
                    float ang = (j + 0.5f) * i * a;
                    sum += spectrum[j] * math.cos(ang);
                }
                cepstrum[i] = sum;
            }
        }

        // ---------- Norm ----------------------------------------------------------
        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static float Norm(float* array, int len)
        {
            float sum = 0f;
            float* p = array;
            float* end = p + len;

            // Unroll by 4
            for (; p + 4 <= end; p += 4)
            {
                float x0 = p[0]; sum += x0 * x0;
                float x1 = p[1]; sum += x1 * x1;
                float x2 = p[2]; sum += x2 * x2;
                float x3 = p[3]; sum += x3 * x3;
            }
            for (; p < end; p++)
            {
                float x = *p; sum += x * x;
            }

            return math.sqrt(sum);
        }
    }
}
