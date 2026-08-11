using System;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.Scripts.Drivers
{
    [Serializable]
    public class BasisOneEuroFilter
    {
        [Range(0.01f, 10f)] public float minCutoff = 1.0f;
        [Range(0f, 10f)] public float beta = 0.0f;
        [Range(0.01f, 10f)] public float dCutoff = 1.0f;

        private bool _initialized;
        private double _lastTimestamp;

        protected LowPassFilter _x;
        protected LowPassFilter _dx;

        public BasisOneEuroFilter() { }
        public BasisOneEuroFilter(float minCutoff, float beta, float dCutoff)
        {
            this.minCutoff = minCutoff;
            this.beta = beta;
            this.dCutoff = dCutoff;
        }

        protected float Alpha(float cutoff, float dt)
        {
            float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
            return 1.0f / (1.0f + tau / Mathf.Max(dt, 1e-6f));
        }

        protected float ComputeDt(double timestamp)
        {
            if (!_initialized)
            {
                _initialized = true;
                _lastTimestamp = timestamp;
                return 1f / 90f;
            }
            float dt = (float)math.max(timestamp - _lastTimestamp, 1e-6f);
            _lastTimestamp = timestamp;
            return dt;
        }

        protected class LowPassFilter
        {
            private bool _initialized;
            private float _hatX;
            public float Filter(float x, float alpha)
            {
                if (!_initialized)
                {
                    _initialized = true;
                    _hatX = x;
                }
                else
                {
                    _hatX = alpha * x + (1 - alpha) * _hatX;
                }
                return _hatX;
            }
            public float Last() => _hatX;
        }
    }

    [Serializable]
    public class BasisOneEuroFilterVector3 : BasisOneEuroFilter
    {
        private LowPassFilter _xX = new LowPassFilter();
        private LowPassFilter _xY = new LowPassFilter();
        private LowPassFilter _xZ = new LowPassFilter();

        private LowPassFilter _dxX = new LowPassFilter();
        private LowPassFilter _dxY = new LowPassFilter();
        private LowPassFilter _dxZ = new LowPassFilter();

        public BasisOneEuroFilterVector3() { }
        public BasisOneEuroFilterVector3(float minCutoff, float beta, float dCutoff) : base(minCutoff, beta, dCutoff) { }

        public Vector3 Filter(Vector3 x, double timestamp)
        {
            float dt = ComputeDt(timestamp);

            Vector3 dx = new Vector3(
                (_xX.Last() - x.x) / dt,
                (_xY.Last() - x.y) / dt,
                (_xZ.Last() - x.z) / dt
            );

            float ad = Alpha(dCutoff, dt);
            dx = new Vector3(
                _dxX.Filter(dx.x, ad),
                _dxY.Filter(dx.y, ad),
                _dxZ.Filter(dx.z, ad)
            );

            float cutoff = minCutoff + beta * dx.magnitude;

            float a = Alpha(cutoff, dt);
            return new Vector3(
                _xX.Filter(x.x, a),
                _xY.Filter(x.y, a),
                _xZ.Filter(x.z, a)
            );
        }
    }

    [Serializable]
    public class BasisOneEuroFilterQuaternion : BasisOneEuroFilter
    {
        private LowPassFilter _xW = new LowPassFilter();
        private LowPassFilter _xX = new LowPassFilter();
        private LowPassFilter _xY = new LowPassFilter();
        private LowPassFilter _xZ = new LowPassFilter();

        private LowPassFilter _dxW = new LowPassFilter();
        private LowPassFilter _dxX = new LowPassFilter();
        private LowPassFilter _dxY = new LowPassFilter();
        private LowPassFilter _dxZ = new LowPassFilter();

        public BasisOneEuroFilterQuaternion() { }
        public BasisOneEuroFilterQuaternion(float minCutoff, float beta, float dCutoff) : base(minCutoff, beta, dCutoff) { }

        public Quaternion Filter(Quaternion q, float timestamp)
        {
            if (q == Quaternion.identity == false)
                q.Normalize();

            float dt = ComputeDt(timestamp);

            Vector4 cur = new Vector4(q.x, q.y, q.z, q.w);
            Vector4 last = new Vector4(_xX.Last(), _xY.Last(), _xZ.Last(), _xW.Last());
            Vector4 d = (last - cur) / dt;

            float ad = Alpha(dCutoff, dt);
            Vector4 dFiltered = new Vector4(
                _dxX.Filter(d.x, ad),
                _dxY.Filter(d.y, ad),
                _dxZ.Filter(d.z, ad),
                _dxW.Filter(d.w, ad)
            );

            float cutoff = minCutoff + beta * dFiltered.magnitude;

            float a = Alpha(cutoff, dt);
            Vector4 qFiltered = new Vector4(
                _xX.Filter(cur.x, a),
                _xY.Filter(cur.y, a),
                _xZ.Filter(cur.z, a),
                _xW.Filter(cur.w, a)
            );

            Quaternion result = new Quaternion(qFiltered.x, qFiltered.y, qFiltered.z, qFiltered.w);
            result.Normalize();
            return result;
        }
    }
}
