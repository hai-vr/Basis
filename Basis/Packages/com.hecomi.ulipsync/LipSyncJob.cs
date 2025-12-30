using Unity.Jobs;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Burst;

namespace uLipSync
{

    [BurstCompile]
    public struct LipSyncJob : IJob
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

            Algorithm.CopyRingBuffer(input, out var buffer, startIndex);
            Algorithm.LowPassFilter(ref buffer, outputSampleRate, targetSampleRate / 2, range);
            Algorithm.DownSample(buffer, out var data, outputSampleRate, targetSampleRate);
            Algorithm.PreEmphasis(ref data, 0.97f);
            Algorithm.HammingWindow(ref data);
            Algorithm.Normalize(ref data, 1f);
            Algorithm.FFT(data, out var spectrum);
            Algorithm.MelFilterBank(spectrum, out var melSpectrum, targetSampleRate, melFilterBankChannels);

            int length = melSpectrum.Length;
            // Floor powers before dB to avoid -inf and NaNs on silence/near-silence
            for (int k = 0; k < length; ++k)
            {
                melSpectrum[k] = math.max(melSpectrum[k], EPS);
            }

            Algorithm.PowerToDb(ref melSpectrum);
            Algorithm.DCT(melSpectrum, out var melCepstrum);

            // Copy MFCCs (skip c0) and sanitize to finite values

            for (int i = 0; i < MFCCLength; ++i)
            {
                float v = melCepstrum[i + 1];
                mfcc[i] = IsFinite(v) ? v : 0f;
            }

            // If MFCC energy is basically gone (e.g., after strong normalization), fall back to rest
            if (IsLowEnergy())
            {
                OneHotRest(scores, restPhonemeIndex);
                info[0] = new Info { volume = Algorithm.GetRMSVolume(input), mainPhonemeIndex = SafeRestIndex(restPhonemeIndex, ScoresLength) };
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
                volume = Algorithm.GetRMSVolume(input),
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
    }
}
