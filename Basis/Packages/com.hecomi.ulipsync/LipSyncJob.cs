using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

namespace uLipSync
{
    /// <summary>
    /// Allocate once (Allocator.Persistent or TempJob) and reuse every frame.
    /// Caller owns lifetime (Dispose when done).
    /// </summary>
    public struct LipSyncWorkspace : IDisposable
    {
        // ring buffer copy
        public NativeArray<float> buffer;

        // post-downsample time-domain frame
        public NativeArray<float> data;

        // magnitude spectrum (same length as data)
        public NativeArray<float> spectrum;

        // mel spectrum + cepstrum (melDiv length)
        public NativeArray<float> melSpectrum;
        public NativeArray<float> melCepstrum;

        // filter scratch
        public NativeArray<float> tmp;
        public NativeArray<float> firTaps;

        // FFT scratch (same length as data)
        public NativeArray<float> fftRe;
        public NativeArray<float> fftIm;

        public bool IsCreated =>
            buffer.IsCreated && data.IsCreated && spectrum.IsCreated &&
            melSpectrum.IsCreated && melCepstrum.IsCreated &&
            tmp.IsCreated && firTaps.IsCreated &&
            fftRe.IsCreated && fftIm.IsCreated;

        public void Dispose()
        {
            if (buffer.IsCreated) buffer.Dispose();
            if (data.IsCreated) data.Dispose();
            if (spectrum.IsCreated) spectrum.Dispose();
            if (melSpectrum.IsCreated) melSpectrum.Dispose();
            if (melCepstrum.IsCreated) melCepstrum.Dispose();
            if (tmp.IsCreated) tmp.Dispose();
            if (firTaps.IsCreated) firTaps.Dispose();
            if (fftRe.IsCreated) fftRe.Dispose();
            if (fftIm.IsCreated) fftIm.Dispose();
        }

        /// <summary>
        /// Helper for caller: computes output length of DownSample() given input length and rates.
        /// This matches the job's logic (integer decimate if divisible; otherwise linear resample).
        /// </summary>
        public static int ComputeDownsampleLength(int inputLen, int sampleRate, int targetSampleRate)
        {
            if (sampleRate <= targetSampleRate) return inputLen;

            if (sampleRate % targetSampleRate == 0)
            {
                int skip = sampleRate / targetSampleRate;
                return inputLen / skip;
            }

            float df = (float)sampleRate / targetSampleRate;
            return (int)math.round(inputLen / df);
        }

        /// <summary>
        /// Helper for caller: computes FIR length used by LowPassFilter().
        /// Note: matches original math/parity logic.
        /// </summary>
        public static int ComputeLowPassFirLength(float sampleRate, float cutoffHz, float rangeHz)
        {
            float cutoff = (cutoffHz - rangeHz) / sampleRate;
            float range = rangeHz / sampleRate;

            int n = (int)math.round(3.1f / range);
            if (((n + 1) & 1) == 0) n += 1; // keep original parity logic
            return n;
        }

        /// <summary>
        /// Allocate workspace with correct sizes. Caller can store/reuse per stream.
        /// </summary>
        public static LipSyncWorkspace Create(
            int inputLen,
            int sampleRate,
            int targetSampleRate,
            int melDiv,
            float firRangeHz,
            Allocator allocator)
        {
            int dataLen = ComputeDownsampleLength(inputLen, sampleRate, targetSampleRate);

            float cutoffHz = targetSampleRate * 0.5f;
            int firLen = ComputeLowPassFirLength(sampleRate, cutoffHz, firRangeHz);

            return new LipSyncWorkspace
            {
                buffer = new NativeArray<float>(inputLen, allocator),
                data = new NativeArray<float>(dataLen, allocator),
                spectrum = new NativeArray<float>(dataLen, allocator),
                melSpectrum = new NativeArray<float>(melDiv, allocator),
                melCepstrum = new NativeArray<float>(melDiv, allocator),

                tmp = new NativeArray<float>(math.max(inputLen, dataLen), allocator), // big enough for copy/filter ops
                firTaps = new NativeArray<float>(firLen, allocator),

                fftRe = new NativeArray<float>(dataLen, allocator),
                fftIm = new NativeArray<float>(dataLen, allocator),
            };
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
        [ReadOnly] public int melFilterBankChannels;
        [ReadOnly] public CompareMethod compareMethod;
        [ReadOnly] public NativeArray<float> means;
        [ReadOnly] public NativeArray<float> standardDeviations;
        [ReadOnly] public NativeArray<float> phonemes;

        [ReadOnly] public int restPhonemeIndex;

        public NativeArray<float> mfcc;
        public NativeArray<float> scores;
        public NativeArray<Info> info;

        // Workspace allocated by caller (no allocations inside Execute)
        public LipSyncWorkspace ws;

        private const float FIR_RANGE_HZ = 500f;

        const float EPS = 1e-12f;
        const float LN10 = 2.302585092994046f;

        public int ScoresLength;
        public int MFCCLength;

        public void Execute()
        {
            ScoresLength = scores.Length;
            MFCCLength = mfcc.Length;

            // ---- 1) Copy ring buffer into ws.buffer
            CopyRingBuffer(input, ws.buffer, startIndex);

            // ---- 2) Low pass filter in-place on ws.buffer (uses ws.tmp + ws.firTaps)
            LowPassFilterInPlace(ws.buffer, ws.tmp, ws.firTaps, outputSampleRate, targetSampleRate * 0.5f, FIR_RANGE_HZ);

            // ---- 3) Downsample from ws.buffer -> ws.data
            DownSample(ws.buffer, ws.data, outputSampleRate, targetSampleRate);

            // ---- 4) Pre-emphasis (uses ws.tmp as source copy)
            PreEmphasisInPlace(ws.data, ws.tmp, 0.97f);

            // ---- 5) Window, normalize
            HammingWindowInPlace(ws.data);
            NormalizeInPlace(ws.data, 1f);

            // ---- 6) FFT magnitude -> ws.spectrum (uses ws.fftRe/ws.fftIm)
            FFTMagnitude(ws.data, ws.spectrum, ws.fftRe, ws.fftIm);

            // ---- 7) Mel filter bank -> ws.melSpectrum
            MelFilterBank(ws.spectrum, ws.melSpectrum, targetSampleRate, melFilterBankChannels);

            // floor powers to avoid -inf / NaNs on silence
            for (int k = 0; k < ws.melSpectrum.Length; ++k)
                ws.melSpectrum[k] = math.max(ws.melSpectrum[k], EPS);

            // ---- 8) Power to dB, DCT -> ws.melCepstrum
            PowerToDbInPlace(ws.melSpectrum);
            DCT(ws.melSpectrum, ws.melCepstrum);

            // ---- 9) Copy MFCCs (skip c0) and sanitize
            for (int i = 0; i < MFCCLength; ++i)
            {
                float v = ws.melCepstrum[i + 1];
                mfcc[i] = IsFinite(v) ? v : 0f;
            }

            // ---- 10) Silence fallback
            if (IsLowEnergy())
            {
                OneHotRest(scores, restPhonemeIndex);
                info[0] = new Info
                {
                    volume = GetRMSVolume(input),
                    mainPhonemeIndex = SafeRestIndex(restPhonemeIndex, ScoresLength)
                };
                return;
            }

            // ---- 11) Scores
            CalcScoresSIMD();

            int winner = GetVowelOrRest();

            info[0] = new Info
            {
                volume = GetRMSVolume(input),
                mainPhonemeIndex = winner
            };
        }

        // ======================================================================
        // Scoring (unchanged behavior; no allocations)
        // ======================================================================

        [BurstCompile]
        void CalcScoresSIMD()
        {
            float sum = 0f;

            for (int i = 0; i < ScoresLength; ++i)
            {
                float s = CalcScoreSIMD(i);
                if (!IsFinite(s) || s < 0f) s = 0f;
                scores[i] = s;
                sum += s;
            }

            if (sum > 0f && IsFinite(sum))
            {
                float inv = math.rcp(sum);
                for (int i = 0; i < ScoresLength; ++i)
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

            for (; i < MFCCLength; ++i)
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

            for (; i < MFCCLength; ++i)
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

        // ======================================================================
        // DSP (NO allocations; all scratch uses workspace)
        // ======================================================================

        public static void CopyRingBuffer(in NativeArray<float> input, NativeArray<float> output, int startSrcIndex)
        {
            CopyRingBuffer(
                (float*)input.GetUnsafeReadOnlyPtr(),
                (float*)output.GetUnsafePtr(),
                input.Length,
                startSrcIndex);
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

        static void LowPassFilterInPlace(NativeArray<float> data, NativeArray<float> tmp, NativeArray<float> taps, float sampleRate, float cutoffHz, float rangeHz)
        {
            // same parameter transforms as original
            float cutoff = (cutoffHz - rangeHz) / sampleRate;
            float range = rangeHz / sampleRate;

            // copy data -> tmp (only used prefix len)
            UnsafeUtility.MemCpy(tmp.GetUnsafePtr(), data.GetUnsafeReadOnlyPtr(), (long)data.Length * sizeof(float));

            int n = (int)math.round(3.1f / range);
            if (((n + 1) & 1) == 0) n += 1;
            if (taps.Length < n) return; // safety (should not happen if workspace created correctly)

            LowPassFilter(
                (float*)data.GetUnsafePtr(),
                data.Length,
                cutoff,
                (float*)tmp.GetUnsafeReadOnlyPtr(),
                (float*)taps.GetUnsafePtr(),
                n);
        }

        [BurstCompile]
        static void LowPassFilter(float* data, int len, float cutoff, float* tmp, float* b, int bLen)
        {
            float half = (bLen - 1) * 0.5f;

            // taps (FIXED: handle ang == 0 to avoid NaN)
            for (int i = 0; i < bLen; ++i)
            {
                float x = i - half;
                float ang = 2f * math.PI * cutoff * x;
                if (math.abs(ang) < 1e-12f) b[i] = 2f * cutoff;
                else b[i] = 2f * cutoff * math.sin(ang) / ang;
            }

            // convolution (keeps original accumulation style)
            for (int i = 0; i < len; ++i)
            {
                float acc = data[i];
                for (int j = 0; j < bLen; ++j)
                {
                    int k = i - j;
                    if (k >= 0) acc += b[j] * tmp[k];
                    else break;
                }
                data[i] = acc;
            }
        }

        public static void DownSample(in NativeArray<float> input, NativeArray<float> output, int sampleRate, int targetSampleRate)
        {
            if (sampleRate <= targetSampleRate)
            {
                // output length should equal input length
                UnsafeUtility.MemCpy(output.GetUnsafePtr(), input.GetUnsafeReadOnlyPtr(), (long)input.Length * sizeof(float));
                return;
            }

            if (sampleRate % targetSampleRate == 0)
            {
                int skip = sampleRate / targetSampleRate;
                DownSample1(
                    (float*)input.GetUnsafeReadOnlyPtr(),
                    (float*)output.GetUnsafePtr(),
                    output.Length,
                    skip);
            }
            else
            {
                float df = (float)sampleRate / targetSampleRate;
                DownSample2(
                    (float*)input.GetUnsafeReadOnlyPtr(),
                    input.Length,
                    (float*)output.GetUnsafePtr(),
                    output.Length,
                    df);
            }
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DownSample1(float* input, float* output, int outputLen, int skip)
        {
            for (int i = 0; i < outputLen; ++i)
                output[i] = input[i * skip];
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void DownSample2(float* input, int inputLen, float* output, int outputLen, float df)
        {
            // FIXED: i1 must be i0+1 (clamped), not i0
            for (int j = 0; j < outputLen; ++j)
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
            // copy data -> tmp
            UnsafeUtility.MemCpy(tmp.GetUnsafePtr(), data.GetUnsafeReadOnlyPtr(), (long)data.Length * sizeof(float));
            PreEmphasis((float*)data.GetUnsafePtr(), (float*)tmp.GetUnsafeReadOnlyPtr(), data.Length, p);
        }

        [BurstCompile]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static void PreEmphasis(float* data, float* src, int len, float p)
        {
            for (int i = 1; i < len; ++i)
                data[i] = src[i] - p * src[i - 1];
        }

        static void HammingWindowInPlace(NativeArray<float> array)
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
            for (; p < end; p++) *p *= r;
        }

        // ---- FFT magnitude (iterative, no allocations)
        static void FFTMagnitude(NativeArray<float> time, NativeArray<float> magOut, NativeArray<float> re, NativeArray<float> im)
        {
            FFTIterative(
                (float*)time.GetUnsafeReadOnlyPtr(),
                (float*)magOut.GetUnsafePtr(),
                (float*)re.GetUnsafePtr(),
                (float*)im.GetUnsafePtr(),
                time.Length);
        }

        [BurstCompile]
        static void FFTIterative(float* input, float* magOut, float* re, float* im, int N)
        {
            // copy input
            for (int i = 0; i < N; i++) { re[i] = input[i]; im[i] = 0f; }

            // bit-reversal permutation (in-place)
            for (int i = 1, j = 0; i < N; i++)
            {
                int bit = N >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;

                if (i < j)
                {
                    float tr = re[i]; re[i] = re[j]; re[j] = tr;
                    float ti = im[i]; im[i] = im[j]; im[j] = ti;
                }
            }

            // iterative stages
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

            for (int i = 0; i < N; i++)
                magOut[i] = math.sqrt(re[i] * re[i] + im[i] * im[i]);
        }

        // ---- Mel filter bank (no allocations)
        public static void MelFilterBank(in NativeArray<float> spectrum, NativeArray<float> melSpectrum, float sampleRate, int melDiv)
        {
            MelFilterBank(
                (float*)spectrum.GetUnsafeReadOnlyPtr(),
                (float*)melSpectrum.GetUnsafePtr(),
                spectrum.Length,
                sampleRate,
                melDiv);
        }

        [BurstCompile]
        static void MelFilterBank(float* spectrum, float* melSpectrum, int len, float sampleRate, int melDiv)
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
                float norm = 0.5f / (fEnd - fBegin);

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
            for (; p < end; p++) *p = 10f * math.log10(*p);
        }

        public static void DCT(in NativeArray<float> spectrum, NativeArray<float> cepstrum)
        {
            DCT(
                (float*)spectrum.GetUnsafeReadOnlyPtr(),
                (float*)cepstrum.GetUnsafePtr(),
                spectrum.Length);
        }

        [BurstCompile]
        static void DCT(float* spectrum, float* cepstrum, int len)
        {
            float a = math.PI / len;

            for (int i = 0; i < len; ++i)
            {
                float sum = 0f;
                for (int j = 0; j < len; ++j)
                {
                    float ang = (j + 0.5f) * i * a;
                    sum += spectrum[j] * math.cos(ang);
                }
                cepstrum[i] = sum;
            }
        }

        // ======================================================================
        // Helpers
        // ======================================================================

        static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        bool IsLowEnergy()
        {
            float acc = 0f;
            for (int i = 0; i < MFCCLength; i++)
                acc += mfcc[i] * mfcc[i];
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
                max = math.max(max, math.abs(*p));

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

            return math.sqrt(sum / len);
        }

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
    }
}
