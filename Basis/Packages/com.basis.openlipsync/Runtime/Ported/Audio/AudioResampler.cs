using System;
using System.Collections.Concurrent;
using System.Numerics;

namespace OpenLipSync.Inference.Audio
{
    public sealed class AudioResampler : IDisposable
    {
        private readonly int _inputSampleRate;
        private readonly int _outputSampleRate;
        private readonly double _inputPerOutput;
        private readonly double _ratio;

        private readonly int _filterTaps;
        private readonly int _halfTaps;
        private readonly int _numPhases;
        private readonly float[] _coeffTable;

        // Manually-managed delay-line. Replaces a List<float>: the convolution inner loop
        // indexed List.this[] 48× per output sample, which Mono's JIT does not inline well.
        // Layout: valid samples occupy _buf[_bufHead .. _bufHead + _bufCount - 1].
        // Drop-front is O(1) (advance head); we compact when head wanders past mid-array.
        private float[] _buf = new float[256];
        private int _bufHead;
        private int _bufCount;

        private double _time;
        private bool _primed;
        private bool _disposed;

        // Output staging — grows as needed; trimmed to exact length only when the
        // emitted-sample count changes (preserves the previous Length-equals-count contract).
        private float[] _outputArray = Array.Empty<float>();

        // ── Coefficient table cache ──
        // The table depends only on (inputRate, outputRate, taps, phases, cutoffScale).
        // For OpenLipSync all contexts use the same rates, so this avoids rebuilding
        // the 49,152-float table (~196 KB) for every new context.
        private static readonly ConcurrentDictionary<long, float[]> _coeffCache = new ConcurrentDictionary<long, float[]>();

        private static long MakeCacheKey(int inputRate, int outputRate, int taps, int phases)
        {
            // Pack 4 ints into a single long key
            return ((long)inputRate << 48) | ((long)(outputRate & 0xFFFF) << 32) | ((long)(taps & 0xFFFF) << 16) | (long)(phases & 0xFFFF);
        }

        public AudioResampler(int inputSampleRate, int outputSampleRate, int filterTaps = 48, int numPhases = 1024, double cutoffScale = 0.9)
        {
            if (inputSampleRate <= 0) throw new ArgumentException("Input sample rate must be positive", nameof(inputSampleRate));
            if (outputSampleRate <= 0) throw new ArgumentException("Output sample rate must be positive", nameof(outputSampleRate));
            if (filterTaps < 8 || filterTaps % 2 != 0) throw new ArgumentException("Filter taps must be even and >= 8", nameof(filterTaps));
            if (numPhases < 8) throw new ArgumentException("Number of phases must be >= 8", nameof(numPhases));
            if (cutoffScale <= 0 || cutoffScale >= 1) throw new ArgumentException("cutoffScale must be in (0,1)", nameof(cutoffScale));

            _inputSampleRate = inputSampleRate;
            _outputSampleRate = outputSampleRate;
            _inputPerOutput = (double)inputSampleRate / outputSampleRate;
            _ratio = _inputPerOutput;

            _filterTaps = filterTaps;
            _halfTaps = filterTaps / 2;
            _numPhases = numPhases;

            // Check cache before computing the expensive coefficient table
            long key = MakeCacheKey(inputSampleRate, outputSampleRate, filterTaps, numPhases);
            if (_coeffCache.TryGetValue(key, out float[] cached))
            {
                _coeffTable = cached;
            }
            else
            {
                _coeffTable = new float[_numPhases * _filterTaps];
                double fc = 0.5 * Math.Min(1.0, (double)_outputSampleRate / _inputSampleRate) * cutoffScale;
                BuildCoefficientTable(fc);
                _coeffCache.TryAdd(key, _coeffTable);
            }

            _time = 0.0;
            _primed = false;
            _disposed = false;
        }

        public int InputSampleRate => _inputSampleRate;
        public int OutputSampleRate => _outputSampleRate;
        public double ResampleRatio => _ratio;

        public float[] Resample(ReadOnlySpan<float> input)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(AudioResampler));

            int taps = _filterTaps;
            int halfMinus1 = _halfTaps - 1;

            if (input.Length > 0)
            {
                if (!_primed)
                {
                    EnsureBufCapacity(_halfTaps);
                    Array.Clear(_buf, 0, _halfTaps);
                    _bufHead = 0;
                    _bufCount = _halfTaps;
                    _time = halfMinus1;
                    _primed = true;
                }

                int writePos = _bufHead + _bufCount;
                EnsureBufCapacity(writePos + input.Length);
                input.CopyTo(_buf.AsSpan(writePos));
                _bufCount += input.Length;
            }

            if (_bufCount < taps) return Array.Empty<float>();

            float[] coeffs = _coeffTable;
            float[] buf = _buf;
            int bufHead = _bufHead;
            int phases = _numPhases;
            double inputPerOutput = _inputPerOutput;
            int outCount = 0;

            while (true)
            {
                int center = (int)Math.Floor(_time);
                int leftIndex = center - halfMinus1;
                int rightIndex = center + _halfTaps;

                if (leftIndex < 0 || rightIndex >= _bufCount)
                {
                    break;
                }

                double frac = _time - center;
                int phaseIndex = (int)Math.Round(frac * phases);
                if (phaseIndex == phases) phaseIndex = 0;
                int coeffBase = phaseIndex * taps;
                int bufBase = bufHead + leftIndex;

                float sum = ConvolveTaps(buf, bufBase, coeffs, coeffBase, taps);

                if (outCount >= _outputArray.Length)
                {
                    int newCap = _outputArray.Length == 0 ? 64 : _outputArray.Length * 2;
                    Array.Resize(ref _outputArray, newCap);
                }
                _outputArray[outCount++] = sum;

                _time += inputPerOutput;
            }

            int safeToRemove = (int)Math.Floor(_time) - halfMinus1;
            if (safeToRemove > 0)
            {
                if (safeToRemove >= _bufCount)
                {
                    _bufHead = 0;
                    _bufCount = 0;
                }
                else
                {
                    _bufHead += safeToRemove;
                    _bufCount -= safeToRemove;
                }
                _time -= safeToRemove;
                if (_time < 0) _time = 0;

                // Compact when the head has wandered past the midpoint, otherwise
                // appends keep growing the backing array unboundedly.
                if (_bufHead > (_buf.Length >> 1) && _bufCount > 0)
                {
                    Array.Copy(_buf, _bufHead, _buf, 0, _bufCount);
                    _bufHead = 0;
                }
            }

            if (outCount == 0) return Array.Empty<float>();

            // Trim to exact length only when the emitted count changed — preserves the
            // old contract that returned-array Length == sample count.
            if (_outputArray.Length != outCount)
                Array.Resize(ref _outputArray, outCount);
            return _outputArray;
        }

        // SIMD dot-product over `taps` floats. Falls back to scalar if Vector<float>
        // doesn't fit. Note: accumulates in float (the previous code accumulated in
        // double). For 48-tap windowed-sinc audio this is well within precision.
        private static float ConvolveTaps(float[] buf, int bufBase, float[] coeffs, int coeffBase, int taps)
        {
            int simd = Vector<float>.Count;
            int t = 0;
            float sum = 0f;

            if (simd > 1 && taps >= simd)
            {
                Vector<float> vsum = Vector<float>.Zero;
                int vEnd = taps - simd;
                for (; t <= vEnd; t += simd)
                {
                    var vb = new Vector<float>(buf, bufBase + t);
                    var vc = new Vector<float>(coeffs, coeffBase + t);
                    vsum += vb * vc;
                }
                sum = Vector.Dot(vsum, Vector<float>.One);
            }

            for (; t < taps; t++)
            {
                sum += buf[bufBase + t] * coeffs[coeffBase + t];
            }
            return sum;
        }

        private void EnsureBufCapacity(int needed)
        {
            if (_buf.Length >= needed) return;

            int newCap = _buf.Length;
            while (newCap < needed) newCap <<= 1;
            Array.Resize(ref _buf, newCap);
        }

        private void BuildCoefficientTable(double fc)
        {
            for (int p = 0; p < _numPhases; p++)
            {
                double frac = (double)p / _numPhases;
                double sum = 0.0;

                for (int n = 0; n < _filterTaps; n++)
                {
                    double t = n - (_halfTaps - 1) - frac;
                    double sincArg = 2.0 * fc * t;
                    double sinc = Sinc(sincArg);

                    double w = 0.42
                             - 0.5 * Math.Cos((2.0 * Math.PI * n) / (_filterTaps - 1))
                             + 0.08 * Math.Cos((4.0 * Math.PI * n) / (_filterTaps - 1));

                    double h = 2.0 * fc * sinc * w;
                    _coeffTable[p * _filterTaps + n] = (float)h;
                    sum += h;
                }

                if (sum != 0)
                {
                    float norm = (float)(1.0 / sum);
                    for (int n = 0; n < _filterTaps; n++)
                    {
                        _coeffTable[p * _filterTaps + n] *= norm;
                    }
                }
            }
        }

        private static double Sinc(double x)
        {
            if (x == 0.0) return 1.0;
            double pix = Math.PI * x;
            return Math.Sin(pix) / pix;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
