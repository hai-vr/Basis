using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace uLipSync
{
    // ============================
    // Mel + DCT plans unchanged
    // (your existing MelFilterPlan/DctPlan are good)
    // ============================

    public struct BasisLipSyncWorkspace : IDisposable
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
        public BasisFftPlan fftPlan;

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

        public static BasisLipSyncWorkspace Create(
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

            var ws = new BasisLipSyncWorkspace
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

                fftPlan = BasisFftPlan.Build(fftN, allocator),
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
}
