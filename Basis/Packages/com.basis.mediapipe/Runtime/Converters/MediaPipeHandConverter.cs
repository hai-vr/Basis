using Basis.Scripts.BasisSdk.Players;
using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// MediaPipe 21-point hand landmarks → finger curl/splay (BasisLocalHandDriver) plus the wrist
    /// rotation for the hand trackers. Both run off the metric WORLD landmarks, so curl no longer
    /// collapses when the hand points at the camera and the palm frame is real 3D rather than a
    /// projection.
    ///
    /// Rotation is a retarget, not a calibration: the palm frame is measured relative to the user's torso
    /// and re-expressed relative to the avatar's, then corrected by the constant that maps a palm frame
    /// onto the avatar's hand bone. Holding your hand however you like reproduces it on the avatar with
    /// nothing to calibrate.
    /// </summary>
    public sealed class MediaPipeHandConverter
    {
        public float CurlGain = 1f;
        public float ThumbMaxAngle = 100f;
        public float FingerMaxAngle = 160f;
        public float MaxSplayDegrees = 20f;
        public float SplayGain = 1f;
        public float FingerSmoothing = 0.5f;
        public float PoseSmoothing = 0.5f;
        public bool UseRotation = true;

        private Quaternion _leftRot = Quaternion.identity;
        private Quaternion _rightRot = Quaternion.identity;
        private bool _leftRotInit;
        private bool _rightRotInit;

        /// <summary>
        /// Avatar hand geometry in player-root-local space. Correction maps a MediaPipe palm frame onto the
        /// hand bone's rotation; it is built from the knuckle positions, which do not move relative to the
        /// hand when the fingers curl, so it holds for any pose and needs no reference pose to capture.
        /// </summary>
        public struct AvatarHandRig
        {
            public Quaternion Body;
            public Quaternion LeftCorrection;
            public Quaternion RightCorrection;
            public bool Valid;
        }

        public void Reset()
        {
            _leftRotInit = _rightRotInit = false;
        }

        public bool TryGetHandRotation(in BasisMediaPipeResult result, in AvatarHandRig rig, bool left,
            out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (!UseRotation || !rig.Valid) return false;

            Vector3[] hand = left ? result.LeftHandWorldLandmarks : result.RightHandWorldLandmarks;
            if (!MediaPipeSpace.TryHandFrame(hand, left, out Quaternion handFrame)) return false;
            if (!MediaPipeSpace.TryBodyFrame(result.PoseWorldLandmarks, out _, out Quaternion bodyFrame)) return false;

            Quaternion handInBody = Quaternion.Inverse(bodyFrame) * handFrame;
            Quaternion correction = left ? rig.LeftCorrection : rig.RightCorrection;
            Quaternion target = rig.Body * handInBody * correction;

            float t = 1f - Mathf.Clamp01(PoseSmoothing);
            if (left)
            {
                _leftRot = _leftRotInit ? Quaternion.Slerp(_leftRot, target, t) : target;
                _leftRotInit = true;
                rotation = _leftRot;
            }
            else
            {
                _rightRot = _rightRotInit ? Quaternion.Slerp(_rightRot, target, t) : target;
                _rightRotInit = true;
                rotation = _rightRot;
            }
            return true;
        }

        public void Apply(in BasisMediaPipeResult result)
        {
            BasisLocalHandDriver driver = BasisLocalPlayer.Instance.LocalHandDriver;

            Vector3[] left = Fingers(result.LeftHandWorldLandmarks, result.LeftHandLandmarks);
            if (result.HasLeftHand && left != null)
            {
                ApplyHand(left, driver.LeftHand, true);
            }

            Vector3[] right = Fingers(result.RightHandWorldLandmarks, result.RightHandLandmarks);
            if (result.HasRightHand && right != null)
            {
                ApplyHand(right, driver.RightHand, false);
            }
        }

        private static Vector3[] Fingers(Vector3[] world, Vector3[] image)
        {
            if (world != null && world.Length >= MediaPipeSpace.HandCount) return world;
            return image != null && image.Length >= MediaPipeSpace.HandCount ? image : null;
        }

        private void ApplyHand(Vector3[] lm, BasisFingerPose pose, bool isLeft)
        {
            float t = 1f - Mathf.Clamp01(FingerSmoothing);
            pose.ThumbPercentage = Vector2.Lerp(pose.ThumbPercentage, new Vector2(Curl(lm, 1, 2, 3, 4, ThumbMaxAngle), Splay(lm, 2, 3, 5, 6, isLeft)), t);
            pose.IndexPercentage = Vector2.Lerp(pose.IndexPercentage, new Vector2(Curl(lm, 5, 6, 7, 8, FingerMaxAngle), Splay(lm, 5, 6, 9, 10, isLeft)), t);
            pose.MiddlePercentage = Vector2.Lerp(pose.MiddlePercentage, new Vector2(Curl(lm, 9, 10, 11, 12, FingerMaxAngle), 0f), t);
            pose.RingPercentage = Vector2.Lerp(pose.RingPercentage, new Vector2(Curl(lm, 13, 14, 15, 16, FingerMaxAngle), Splay(lm, 13, 14, 9, 10, isLeft)), t);
            pose.LittlePercentage = Vector2.Lerp(pose.LittlePercentage, new Vector2(Curl(lm, 17, 18, 19, 20, FingerMaxAngle), Splay(lm, 17, 18, 13, 14, isLeft)), t);
        }

        private float Curl(Vector3[] lm, int a, int b, int c, int d, float maxAngle)
        {
            Vector3 s1 = lm[b] - lm[a];
            Vector3 s2 = lm[c] - lm[b];
            Vector3 s3 = lm[d] - lm[c];
            float angle = Vector3.Angle(s1, s2) + Vector3.Angle(s2, s3);
            float curl01 = Mathf.Clamp01(angle / maxAngle * CurlGain);
            return 1f - curl01 * 2f;
        }

        private float Splay(Vector3[] lm, int baseMcp, int basePip, int refMcp, int refPip, bool isLeft)
        {
            Vector3 dir = lm[basePip] - lm[baseMcp];
            Vector3 refDir = lm[refPip] - lm[refMcp];
            Vector3 palmNormal = Vector3.Cross(
                lm[MediaPipeSpace.HandIndexMcp] - lm[MediaPipeSpace.HandWrist],
                lm[MediaPipeSpace.HandPinkyMcp] - lm[MediaPipeSpace.HandWrist]);
            float signed = Vector3.SignedAngle(refDir, dir, palmNormal);
            float splay = Mathf.Clamp(signed / MaxSplayDegrees * SplayGain, -1f, 1f);
            return isLeft ? splay : -splay;
        }
    }
}
