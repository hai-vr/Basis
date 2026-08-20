using Unity.Mathematics;

namespace Basis.IK
{
    public sealed class BasisNodPivotSampler
    {
        private readonly float3[] _positions;
        private readonly quaternion[] _rotations;
        private int _filled;
        private int _write;

        private float _sampleAccum;
        private float _solveAccum;

        private float3 _arm;
        private bool _hasArm;

        public float SampleIntervalSeconds = 1f / 30f;

        public float SolveIntervalSeconds = 0.25f;

        public float BlendPerAcceptance = 0.15f;

        public BasisNodPivotResult LastResult;

        public bool HasEstimate => _hasArm;

        public BasisNodPivotSampler(int capacity = 30)
        {
            if (capacity < 4) capacity = 4;
            _positions = new float3[capacity];
            _rotations = new quaternion[capacity];
        }

        public void Reset()
        {
            _filled = 0;
            _write = 0;
            _sampleAccum = 0f;
            _solveAccum = 0f;
            _hasArm = false;
            _arm = default;
            LastResult = default;
        }

        public float3 Update(float3 eyePos, quaternion eyeRot, float dt, float3 priorArm, in BasisNodPivotSettings settings)
        {
            if (!(dt > 0f) || !math.all(math.isfinite(eyePos)))
            {
                return _hasArm ? _arm : priorArm;
            }

            _sampleAccum += dt;
            if (_sampleAccum >= SampleIntervalSeconds)
            {
                _sampleAccum = 0f;
                _positions[_write] = eyePos;
                _rotations[_write] = eyeRot;
                _write = (_write + 1) % _positions.Length;
                if (_filled < _positions.Length) _filled++;
            }

            _solveAccum += dt;
            if (_solveAccum >= SolveIntervalSeconds && _filled >= 4)
            {
                _solveAccum = 0f;
                BasisNodPivotEstimatorCore.Solve(_positions, _rotations, _filled,
                    _hasArm ? _arm : priorArm, in settings, out LastResult);

                // Blended here, inside the solve, so one acceptance moves the arm exactly once. Blending
                // per frame instead would let a single window that slipped past the gates keep pulling for
                // the whole solve interval.
                if (LastResult.Accepted)
                {
                    if (!_hasArm)
                    {
                        _arm = priorArm;
                        _hasArm = true;
                    }
                    _arm = math.lerp(_arm, LastResult.Arm, math.saturate(BlendPerAcceptance));
                }
            }

            return _hasArm ? _arm : priorArm;
        }
    }
}
