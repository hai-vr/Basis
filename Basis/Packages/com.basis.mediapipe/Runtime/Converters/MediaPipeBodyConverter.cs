using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// EXPERIMENTAL torso lean/twist from PoseLandmarker world landmarks. Rotation only (no
    /// noisy monocular translation): builds a chest orientation from the shoulder line + spine
    /// direction, relative to a calibrated neutral. Expect to flip Invert* and tune gains on a
    /// real body.
    /// </summary>
    public sealed class MediaPipeBodyConverter
    {
        public float TwistGain = 1f;
        public float LeanGain = 1f;
        public bool InvertTwist = false;
        public bool InvertLean = false;
        public float Smoothing = 0.6f;

        private const int LeftShoulder = 11;
        private const int RightShoulder = 12;
        private const int LeftHip = 23;
        private const int RightHip = 24;

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

        public bool TryGetChestRotation(in BasisMediaPipeResult result, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
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
            rotation = _smoothed;
            return true;
        }

        private static bool TryTorsoRotation(in BasisMediaPipeResult result, out Quaternion rot)
        {
            rot = Quaternion.identity;
            Vector3[] lm = result.PoseWorldLandmarks;
            if (!result.HasPose || lm == null || lm.Length <= RightHip) return false;

            Vector3 ls = Flip(lm[LeftShoulder]);
            Vector3 rs = Flip(lm[RightShoulder]);
            Vector3 lh = Flip(lm[LeftHip]);
            Vector3 rh = Flip(lm[RightHip]);

            Vector3 up = ((ls + rs) * 0.5f - (lh + rh) * 0.5f).normalized;
            Vector3 right = (rs - ls).normalized;
            Vector3 forward = Vector3.Cross(right, up);
            if (forward.sqrMagnitude < 1e-4f || up.sqrMagnitude < 1e-4f) return false;

            rot = Quaternion.LookRotation(forward.normalized, up);
            return true;
        }

        // MediaPipe world landmarks are y-down; flip to Unity y-up.
        private static Vector3 Flip(Vector3 v) => new Vector3(v.x, -v.y, v.z);

        private static float NormalizeAngle(float angle) => angle > 180f ? angle - 360f : angle;
    }
}
