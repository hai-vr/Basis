using Basis.Scripts.Drivers;
using UnityEngine;

namespace Basis.MediaPipe
{
    public sealed class MediaPipeBodyConverter
    {
        public float Strength = 0.6f;

        public float MaxAngle = 35f;

        public float Smoothing = 0.7f;
        public bool InvertTwist = false;
        public bool InvertLean = false;
        public bool InvertRoll = false;

        private const float CutoffResponsive = 6f;
        private const float CutoffSmooth = 0.6f;
        private const float Beta = 1.5f;
        private const float DerivativeCutoff = 1f;



        private Quaternion _neutralInverse = Quaternion.identity;
        private bool _calibrated;
        private BasisEuroQuatState _euro;
        private Quaternion _sampled = Quaternion.identity;
        private Quaternion _carried = Quaternion.identity;
        private bool _hasSample;


        private float Cutoff => Mathf.Lerp(CutoffResponsive, CutoffSmooth, Mathf.Clamp01(Smoothing));

        public void Calibrate(in BasisMediaPipeResult result)
        {
            if (TryTorsoRotation(result, out Quaternion rot))
            {
                _neutralInverse = Quaternion.Inverse(rot);
                _calibrated = true;
            }
        }

        public void Reset()
        {
            _calibrated = false;
            _euro = default;
            _hasSample = false;
            _sampled = Quaternion.identity;
            _carried = Quaternion.identity;

        }


        public bool TryGetTorsoOffset(in BasisMediaPipeResult result, in MediaPipeTiming timing, out Quaternion offset)
        {
            offset = Quaternion.identity;
            if (!TryTorsoRotation(result, out Quaternion rot)) return false;

            if (!_calibrated)
            {
                _neutralInverse = Quaternion.Inverse(rot);
                _calibrated = true;
            }

            Vector3 euler = (_neutralInverse * rot).eulerAngles;
            float lean = Axis(euler.x, InvertLean);
            float twist = Axis(euler.y, InvertTwist);
            // Roll is the side-lean, and it used to be dropped on the floor. For someone sitting at a webcam it is
            // the most visible thing their torso does — you sway sideways far more than you twist.
            float roll = Axis(euler.z, InvertRoll);

            Quaternion target = Quaternion.Euler(lean, twist, roll);

            // Same two-clock split the arms use: one-euro on the camera's delta when a fresh sample lands, then a
            // carry slerp every rendered frame. A filter run at render rate over a held sample snaps and holds.
            if (timing.IsNewSample || !_hasSample)
            {
                _sampled = BasisFilterMath.EuroQuat(ref _euro, target, timing.SampleDelta,
                    Cutoff, Beta, DerivativeCutoff);
                if (!_hasSample)
                {
                    _carried = _sampled;
                    _hasSample = true;
                    offset = _carried;
                    return true;
                }
            }

            _carried = Quaternion.Slerp(_carried, _sampled,
                BasisFilterMath.Alpha(timing.CarryCutoff, timing.RenderDelta));
            offset = _carried;
            return true;
        }

        private float Axis(float raw, bool invert)
        {
            float angle = raw > 180f ? raw - 360f : raw;
            angle = Mathf.Clamp(angle, -MaxAngle, MaxAngle);
            return angle * (invert ? -1f : 1f) * Strength;
        }

        private static bool TryTorsoRotation(in BasisMediaPipeResult result, out Quaternion rot)
        {
            rot = Quaternion.identity;
            if (!result.HasPose) return false;
            return MediaPipeSpace.TryBodyFrame(result.PoseWorldLandmarks, out _, out rot);
        }
    }
}
