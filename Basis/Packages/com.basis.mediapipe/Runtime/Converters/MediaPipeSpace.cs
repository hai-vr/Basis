using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// MediaPipe landmarks arrive in a y-down frame, and the backend mirrors the camera image before
    /// inference (selfie view), so the reported geometry is a mirror of the real body. Everything is
    /// converted into one Unity-space, un-mirrored, anatomically-labelled frame here so the converters
    /// never deal with raw MediaPipe conventions again.
    ///
    /// The hand landmarker already compensates for a mirrored image in its handedness LABEL (it assumes
    /// selfie input), but not in the landmark COORDINATES. The pose landmarker compensates for neither,
    /// so its left/right indices are swapped as well.
    /// </summary>
    public static class MediaPipeSpace
    {
        public const int Nose = 0;
        public const int LeftEar = 7, RightEar = 8;
        public const int LeftShoulder = 11, RightShoulder = 12;
        public const int LeftElbow = 13, RightElbow = 14;
        public const int LeftWrist = 15, RightWrist = 16;
        public const int LeftHip = 23, RightHip = 24;
        public const int PoseCount = 33;

        public const int HandWrist = 0;
        public const int HandIndexMcp = 5;
        public const int HandMiddleMcp = 9;
        public const int HandPinkyMcp = 17;
        public const int HandCount = 21;

        private static readonly int[] LeftRightPairs =
        {
            1, 4, 2, 5, 3, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16,
            17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32,
        };

        /// <summary>Metric world landmark (metres) into Unity space, undoing the selfie mirror.</summary>
        public static Vector3 World(Vector3 v, bool mirrored) =>
            new Vector3(mirrored ? -v.x : v.x, -v.y, v.z);

        /// <summary>
        /// Normalized image landmark into a y-up [0,1] frame, undoing the selfie mirror. A horizontal mirror
        /// does not touch depth, so z passes through.
        /// </summary>
        public static Vector3 Image(Vector3 v, bool mirrored) =>
            new Vector3(mirrored ? 1f - v.x : v.x, 1f - v.y, v.z);

        /// <summary>
        /// Restores anatomical left/right on pose landmarks detected from a mirrored frame. The pose
        /// model names landmarks from appearance, so a mirrored body reports its sides the wrong way round.
        /// </summary>
        public static void SwapPoseSidesInPlace(Vector3[] landmarks)
        {
            if (landmarks == null || landmarks.Length < PoseCount) return;
            for (int i = 0; i < LeftRightPairs.Length; i += 2)
            {
                int a = LeftRightPairs[i];
                int b = LeftRightPairs[i + 1];
                (landmarks[a], landmarks[b]) = (landmarks[b], landmarks[a]);
            }
        }

        /// <summary>
        /// Which way MediaPipe's depth axis runs, resolved from anatomy rather than assumed: the nose is
        /// always in front of the ears. If the cross product of the shoulder line and the head direction
        /// disagrees with that, the landmark cloud is depth-reflected and z has to be negated to bring it
        /// back into a Unity-handed frame. Returns 0 when the head landmarks are too degenerate to call it,
        /// in which case the caller should keep the last known sign.
        /// </summary>
        public static float DepthSign(Vector3[] pose)
        {
            if (pose == null || pose.Length < PoseCount) return 0f;

            Vector3 shoulderCenter = (pose[LeftShoulder] + pose[RightShoulder]) * 0.5f;
            Vector3 earCenter = (pose[LeftEar] + pose[RightEar]) * 0.5f;

            Vector3 right = pose[RightShoulder] - pose[LeftShoulder];
            Vector3 up = earCenter - shoulderCenter;
            if (right.sqrMagnitude < 1e-8f || up.sqrMagnitude < 1e-8f) return 0f;

            Vector3 forward = Vector3.Cross(right.normalized, up.normalized);
            Vector3 faceDir = pose[Nose] - earCenter;
            if (forward.sqrMagnitude < 1e-8f || faceDir.sqrMagnitude < 1e-8f) return 0f;

            float alignment = Vector3.Dot(faceDir.normalized, forward.normalized);
            if (Mathf.Abs(alignment) < 0.1f) return 0f;
            return alignment > 0f ? 1f : -1f;
        }

        public static void ApplyDepthSign(Vector3[] landmarks, float sign)
        {
            if (landmarks == null || sign >= 0f) return;
            for (int i = 0; i < landmarks.Length; i++)
            {
                Vector3 v = landmarks[i];
                landmarks[i] = new Vector3(v.x, v.y, -v.z);
            }
        }

        /// <summary>
        /// Orthonormal body frame from the shoulder line and torso, built with the same rule the avatar rig
        /// uses so mapping between them is a plain change of basis. Up falls back to the head when the hips
        /// are off-frame (a seated webcam user), where the pose model extrapolates them badly.
        /// </summary>
        public static bool TryBodyFrame(Vector3[] pose, out Vector3 shoulderCenter, out Quaternion frame)
        {
            shoulderCenter = Vector3.zero;
            frame = Quaternion.identity;
            if (pose == null || pose.Length < PoseCount) return false;

            shoulderCenter = (pose[LeftShoulder] + pose[RightShoulder]) * 0.5f;

            Vector3 right = pose[RightShoulder] - pose[LeftShoulder];
            if (right.sqrMagnitude < 1e-8f) return false;
            right.Normalize();

            Vector3 hipCenter = (pose[LeftHip] + pose[RightHip]) * 0.5f;
            Vector3 up = shoulderCenter - hipCenter;
            if (up.sqrMagnitude < 1e-6f)
            {
                up = (pose[LeftEar] + pose[RightEar]) * 0.5f - shoulderCenter;
            }
            up = Vector3.ProjectOnPlane(up, right);
            if (up.sqrMagnitude < 1e-8f) return false;
            up.Normalize();

            Vector3 forward = Vector3.Cross(right, up);
            if (forward.sqrMagnitude < 1e-8f) return false;

            frame = Quaternion.LookRotation(forward.normalized, up);
            return true;
        }

        /// <summary>
        /// Palm frame: forward runs wrist to middle knuckle, up is the palm normal. The index/pinky
        /// ordering mirrors between hands, so the normal is negated on the left to give both hands the same
        /// "out of the back of the hand" convention.
        ///
        /// Called with MediaPipe landmarks for the user and with humanoid bone positions for the avatar, so
        /// both palm frames are defined by the same rule and mapping between them is a change of basis.
        /// </summary>
        public static bool TryPalmFrame(Vector3 wrist, Vector3 indexMcp, Vector3 middleMcp, Vector3 pinkyMcp,
            bool left, out Quaternion frame)
        {
            frame = Quaternion.identity;

            Vector3 forward = middleMcp - wrist;
            Vector3 across = indexMcp - pinkyMcp;
            if (forward.sqrMagnitude < 1e-8f || across.sqrMagnitude < 1e-8f) return false;

            Vector3 normal = Vector3.Cross(forward, across);
            if (normal.sqrMagnitude < 1e-8f) return false;
            if (left) normal = -normal;
            normal.Normalize();

            forward = Vector3.ProjectOnPlane(forward, normal);
            if (forward.sqrMagnitude < 1e-8f) return false;

            frame = Quaternion.LookRotation(forward.normalized, normal);
            return true;
        }

        public static bool TryHandFrame(Vector3[] hand, bool left, out Quaternion frame)
        {
            frame = Quaternion.identity;
            if (hand == null || hand.Length < HandCount) return false;
            return TryPalmFrame(hand[HandWrist], hand[HandIndexMcp], hand[HandMiddleMcp], hand[HandPinkyMcp],
                left, out frame);
        }
    }
}
