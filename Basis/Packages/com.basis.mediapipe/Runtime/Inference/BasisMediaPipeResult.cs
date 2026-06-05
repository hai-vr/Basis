using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>One inferred frame of webcam tracking data in source/normalized space.</summary>
    public struct BasisMediaPipeResult
    {
        public bool HasFace;
        public bool HasLeftHand;
        public bool HasRightHand;
        public bool HasPose;

        public Matrix4x4 FaceTransform;
        public float[] FaceBlendshapes;
        public float TongueOut;

        public Vector2 HeadImagePosition;
        public float FaceImageSize;

        public Vector2 LeftEyeGaze;
        public Vector2 RightEyeGaze;

        public Vector3[] LeftHandLandmarks;
        public Vector3[] RightHandLandmarks;
        public Vector3[] PoseLandmarks;
        public Vector3[] PoseWorldLandmarks;

        public double TimestampMs;
    }
}
