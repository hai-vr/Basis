using Basis.Scripts.BasisSdk.Players;
using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// MediaPipe 21-point hand landmarks → finger curl/splay (BasisLocalHandDriver) plus a
    /// wrist pose OFFSET for the hand trackers. Like the head, the offset is applied on top of a
    /// base tracker space (the manager adds it to the head/eye position and the hand bone's rest
    /// rotation), and rotation is relative to a calibrated neutral.
    /// </summary>
    public sealed class MediaPipeHandConverter
    {
        public float CurlGain = 1f;
        public float ThumbMaxAngle = 100f;
        public float FingerMaxAngle = 160f;
        public float MaxSplayDegrees = 20f;
        public float SplayGain = 1f;

        // Wrist-position offset from the head, derived from the wrist's position in the frame.
        public float PlaneWidth = 0.7f;
        public float PlaneHeight = 0.7f;
        public float ForwardDepth = 0.35f;
        public float HandDrop = 0f;
        public float FingerSmoothing = 0.5f;
        public float PoseSmoothing = 0.5f;
        public bool UseRotation = true;

        private Vector3 _leftPos;
        private Vector3 _rightPos;
        private Quaternion _leftRot = Quaternion.identity;
        private Quaternion _rightRot = Quaternion.identity;
        private bool _leftInit;
        private bool _rightInit;
        private Quaternion _leftNeutralInverse = Quaternion.identity;
        private Quaternion _rightNeutralInverse = Quaternion.identity;
        private bool _leftCalibrated;
        private bool _rightCalibrated;

        public void Calibrate(in BasisMediaPipeResult result)
        {
            if (result.HasLeftHand && TryRawRotation(result.LeftHandLandmarks, out Quaternion lr))
            {
                _leftNeutralInverse = Quaternion.Inverse(lr);
                _leftCalibrated = true;
            }
            if (result.HasRightHand && TryRawRotation(result.RightHandLandmarks, out Quaternion rr))
            {
                _rightNeutralInverse = Quaternion.Inverse(rr);
                _rightCalibrated = true;
            }
        }

        /// <summary>Wrist pose as an offset: position relative to the head, rotation relative to the calibrated neutral.</summary>
        public bool TryGetHandTarget(Vector3[] lm, bool left, out Vector3 positionOffset, out Quaternion rotationOffset)
        {
            positionOffset = Vector3.zero;
            rotationOffset = Quaternion.identity;
            if (lm == null || lm.Length < 21)
            {
                return false;
            }

            Vector3 wrist = lm[0];
            Vector3 rawPos = new Vector3(
                (0.5f - wrist.x) * PlaneWidth,
                (0.5f - wrist.y) * PlaneHeight - HandDrop,
                ForwardDepth);

            Quaternion rawRot = Quaternion.identity;
            if (UseRotation && TryRawRotation(lm, out Quaternion r))
            {
                if (left)
                {
                    if (!_leftCalibrated) { _leftNeutralInverse = Quaternion.Inverse(r); _leftCalibrated = true; }
                    rawRot = _leftNeutralInverse * r;
                }
                else
                {
                    if (!_rightCalibrated) { _rightNeutralInverse = Quaternion.Inverse(r); _rightCalibrated = true; }
                    rawRot = _rightNeutralInverse * r;
                }
            }

            float t = 1f - Mathf.Clamp01(PoseSmoothing);
            if (left)
            {
                _leftPos = _leftInit ? Vector3.Lerp(_leftPos, rawPos, t) : rawPos;
                _leftRot = _leftInit ? Quaternion.Slerp(_leftRot, rawRot, t) : rawRot;
                _leftInit = true;
                positionOffset = _leftPos;
                rotationOffset = _leftRot;
            }
            else
            {
                _rightPos = _rightInit ? Vector3.Lerp(_rightPos, rawPos, t) : rawPos;
                _rightRot = _rightInit ? Quaternion.Slerp(_rightRot, rawRot, t) : rawRot;
                _rightInit = true;
                positionOffset = _rightPos;
                rotationOffset = _rightRot;
            }
            return true;
        }

        private static bool TryRawRotation(Vector3[] lm, out Quaternion rot)
        {
            rot = Quaternion.identity;
            if (lm == null || lm.Length < 21) return false;

            Vector3 forward = lm[9] - lm[0];
            Vector3 normal = Vector3.Cross(lm[5] - lm[0], lm[17] - lm[0]);
            if (forward.sqrMagnitude < 1e-6f || normal.sqrMagnitude < 1e-6f) return false;

            rot = Quaternion.LookRotation(
                new Vector3(-forward.x, -forward.y, -forward.z),
                new Vector3(-normal.x, -normal.y, -normal.z)) * Quaternion.Euler(0f, 180f, 0f);
            return true;
        }

        public void Apply(in BasisMediaPipeResult result)
        {
            BasisLocalHandDriver driver = BasisLocalPlayer.Instance.LocalHandDriver;
            if (result.HasLeftHand && result.LeftHandLandmarks != null && result.LeftHandLandmarks.Length >= 21)
            {
                ApplyHand(result.LeftHandLandmarks, driver.LeftHand, true);
            }
            if (result.HasRightHand && result.RightHandLandmarks != null && result.RightHandLandmarks.Length >= 21)
            {
                ApplyHand(result.RightHandLandmarks, driver.RightHand, false);
            }
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
            Vector3 palmNormal = Vector3.Cross(lm[5] - lm[0], lm[17] - lm[0]);
            float signed = Vector3.SignedAngle(refDir, dir, palmNormal);
            float splay = Mathf.Clamp(signed / MaxSplayDegrees * SplayGain, -1f, 1f);
            return isLeft ? splay : -splay;
        }
    }
}
