using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// EXPERIMENTAL torso lean/twist from PoseLandmarker world landmarks. Rotation only (no noisy monocular
    /// translation): reads the same body frame the arms retarget against, and reports how far it has turned
    /// from a calibrated neutral.
    ///
    /// What comes out is an OFFSET, not a chest orientation — the caller composes it onto the avatar's own body
    /// so the chest keeps following the body and only picks up your lean and twist on top.
    /// </summary>
    public sealed class MediaPipeBodyConverter
    {
        public float TwistGain = 1f;
        public float LeanGain = 1f;
        public bool InvertTwist = false;
        public bool InvertLean = false;
        public float Smoothing = 0.6f;

        private Quaternion _neutralInverse = Quaternion.identity;
        private bool _calibrated;
        private Quaternion _smoothed = Quaternion.identity;

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
            _smoothed = Quaternion.identity;
        }

        /// <summary>Torso lean/twist relative to the calibrated neutral, in the avatar's body axes.</summary>
        public bool TryGetTorsoOffset(in BasisMediaPipeResult result, out Quaternion offset)
        {
            offset = Quaternion.identity;
            if (!TryTorsoRotation(result, out Quaternion rot)) return false;

            if (!_calibrated)
            {
                _neutralInverse = Quaternion.Inverse(rot);
                _calibrated = true;
            }

            Quaternion rel = _neutralInverse * rot;
            Vector3 euler = rel.eulerAngles;
            float twist = NormalizeAngle(euler.y) * (InvertTwist ? -1f : 1f) * TwistGain;
            float lean = NormalizeAngle(euler.x) * (InvertLean ? -1f : 1f) * LeanGain;
            Quaternion target = Quaternion.Euler(lean, twist, 0f);

            float t = 1f - Mathf.Clamp01(Smoothing);
            _smoothed = Quaternion.Slerp(_smoothed, target, t);
            offset = _smoothed;
            return true;
        }

        private static bool TryTorsoRotation(in BasisMediaPipeResult result, out Quaternion rot)
        {
            rot = Quaternion.identity;
            if (!result.HasPose) return false;
            return MediaPipeSpace.TryBodyFrame(result.PoseWorldLandmarks, out _, out rot);
        }

        private static float NormalizeAngle(float angle) => angle > 180f ? angle - 360f : angle;
    }
}
