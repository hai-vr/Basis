using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// Head rotation from the FaceLandmarker head transform, relative to a calibrated neutral,
    /// produced as a player-local rotation for a Head IK tracker. The camera stays mouse-driven;
    /// only the avatar's head bone follows the webcam.
    /// </summary>
    public sealed class MediaPipeHeadConverter
    {
        public float YawGain = 1f;
        public float PitchGain = 1f;
        public bool InvertYaw = false;
        public bool InvertPitch = true;
        public float Smoothing = 0.5f;

        private Quaternion _neutralInverse = Quaternion.identity;
        private bool _calibrated;
        private float _yaw;
        private float _pitch;

        public void Calibrate(in BasisMediaPipeResult result)
        {
            if (!result.HasFace) return;
            _neutralInverse = Quaternion.Inverse(result.FaceTransform.rotation);
            _calibrated = true;
        }

        public bool TryGetHeadLocalRotation(in BasisMediaPipeResult result, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (!result.HasFace) return false;

            Quaternion headRot = result.FaceTransform.rotation;
            Quaternion rel = _calibrated ? _neutralInverse * headRot : headRot;
            Vector3 euler = rel.eulerAngles;

            float pitch = NormalizeAngle(euler.x) * (InvertPitch ? -1f : 1f) * PitchGain;
            float yaw = NormalizeAngle(euler.y) * (InvertYaw ? -1f : 1f) * YawGain;

            float t = 1f - Mathf.Clamp01(Smoothing);
            _pitch = Mathf.Lerp(_pitch, pitch, t);
            _yaw = Mathf.Lerp(_yaw, yaw, t);

            rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            return true;
        }

        private static float NormalizeAngle(float angle) => angle > 180f ? angle - 360f : angle;
    }
}
