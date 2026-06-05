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
        public bool InvertRoll = false;
        public float PositionGain = 1f;
        public float RollGain = 1f;
        public float HeightOffset = 0f;
        public float Smoothing = 0.5f;

        private Quaternion _neutralInverse = Quaternion.identity;
        private Vector3 _neutralPosition;
        private Vector3 _smoothedPosition;
        private bool _calibrated;
        private float _yaw;
        private float _pitch;
        private float _roll;

        public void Calibrate(in BasisMediaPipeResult result)
        {
            if (!result.HasFace) return;
            _neutralInverse = Quaternion.Inverse(result.FaceTransform.rotation);
            _neutralPosition = result.FaceTransform.GetColumn(3);
            _calibrated = true;
        }

        public bool TryGetHeadOffset(in BasisMediaPipeResult result, out Quaternion rotation, out Vector3 positionOffset)
        {
            rotation = Quaternion.identity;
            positionOffset = Vector3.zero;
            if (!result.HasFace)
            {
                return false;
            }

            Quaternion headRot = result.FaceTransform.rotation;
            Quaternion rel = _calibrated ? _neutralInverse * headRot : headRot;
            Vector3 euler = rel.eulerAngles;

            float pitch = NormalizeAngle(euler.x) * (InvertPitch ? -1f : 1f) * PitchGain;
            float yaw = NormalizeAngle(euler.y) * (InvertYaw ? -1f : 1f) * YawGain;
            float roll = NormalizeAngle(euler.z) * (InvertRoll ? -1f : 1f) * RollGain;

            float t = 1f - Mathf.Clamp01(Smoothing);
            _pitch = Mathf.Lerp(_pitch, pitch, t);
            _yaw = Mathf.Lerp(_yaw, yaw, t);
            _roll = Mathf.Lerp(_roll, roll, t);
            rotation = Quaternion.Euler(_pitch, _yaw, _roll);

            Vector3 translation = result.FaceTransform.GetColumn(3);
            Vector3 delta = _calibrated ? translation - _neutralPosition : Vector3.zero;
            // MediaPipe head transform translation is camera-space (cm); map to player-local
            // (mirror x, z forward) and scale cm->m. Vertical bob is excluded (it fights eye
            // height); HeightOffset is a static trim instead.
            Vector3 targetPos = new Vector3(-delta.x * PositionGain * 0.01f, HeightOffset, -delta.z * PositionGain * 0.01f);
            _smoothedPosition = Vector3.Lerp(_smoothedPosition, targetPos, t);
            positionOffset = _smoothedPosition;
            return true;
        }

        private static float NormalizeAngle(float angle) => angle > 180f ? angle - 360f : angle;
    }
}
