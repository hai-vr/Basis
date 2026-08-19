using Unity.Mathematics;

namespace Basis.IK
{
    /// <summary>
    /// Feeds HMD poses to <see cref="BasisNodPivotEstimatorCore"/> on a cadence and slews the accepted
    /// result. The arm it produces is a body dimension, not a per-frame signal: a bad window must never
    /// be able to jerk the pelvis, so an accepted fit is blended in over roughly a second and an
    /// unaccepted one simply holds.
    /// <para>
    /// Samples are held in a ring, and the solve is order independent (it only forms means and sums), so
    /// the buffer is passed through as-is rather than being unrolled into chronological order.
    /// </para>
    /// </summary>
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

        /// <summary>Sample cadence. The window length is this times the buffer capacity.</summary>
        public float SampleIntervalSeconds = 1f / 30f;

        /// <summary>How often the least squares runs. Cheap, but there is no point solving per frame.</summary>
        public float SolveIntervalSeconds = 0.25f;

        /// <summary>
        /// How far toward an accepted fit the arm moves, per ACCEPTED SOLVE rather than per second. Tied
        /// to the solve and not to the clock, a single window that slips past the gates can only ever move
        /// the arm by this much, while a run of agreeing windows still converges quickly.
        /// </summary>
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

        /// <summary>
        /// Returns the eye-from-pivot arm to drive the gaze-swing removal with: <paramref name="priorArm"/>
        /// until a window is both well excited and well explained, then a slewed blend toward the fit.
        /// </summary>
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
