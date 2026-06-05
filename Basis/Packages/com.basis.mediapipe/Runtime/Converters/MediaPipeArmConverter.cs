using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// Retargets PoseLandmarker landmarks (shoulder/elbow/wrist) onto the avatar's arms: directions
    /// come from the reliable 2D image positions (with damped depth), lengths from the avatar,
    /// anchored at the avatar's shoulder and mapped onto its frontal basis. When no body pose is
    /// available it falls back to the hand landmarker's wrist, mapped across the avatar's full reach.
    /// SwapArms/InvertForward cover the monocular mirror ambiguity.
    /// </summary>
    public sealed class MediaPipeArmConverter
    {
        public float Smoothing = 0.5f;
        public float DepthGain = 0.5f;
        public float HandReachGain = 1.1f;
        public float HeadReachGain = 1f;
        public bool SwapArms = true;
        public bool InvertForward = false;

        private const int LeftShoulder = 11, RightShoulder = 12;
        private const int LeftElbow = 13, RightElbow = 14;
        private const int LeftWrist = 15, RightWrist = 16;

        private Vector3 _leftElbow, _leftWrist, _rightElbow, _rightWrist;
        private bool _leftWristInit, _leftElbowInit, _rightWristInit, _rightElbowInit;

        /// <summary>Avatar arm geometry in player-root-local space, rebuilt per frame by the manager.</summary>
        public struct AvatarArmRig
        {
            public Vector3 LeftAnchor;
            public Vector3 RightAnchor;
            public float LeftUpperLen, LeftForeLen;
            public float RightUpperLen, RightForeLen;
            public Vector3 Right, Up, Forward;
            public Vector3 HeadLocal;
            public float HeadMetric;
            public bool Valid;
        }

        /// <summary>Full arm reconstruction from the body pose (shoulder/elbow/wrist), with elbow pole.</summary>
        public bool TryGetArm(Vector3[] image, float aspect, in AvatarArmRig rig, bool avatarLeft,
            out Vector3 wristLocal, out Vector3 elbowLocal, out Quaternion wristRotation)
        {
            wristLocal = Vector3.zero;
            elbowLocal = Vector3.zero;
            wristRotation = Quaternion.identity;
            if (!rig.Valid || image == null || image.Length <= RightWrist) return false;

            bool srcLeft = avatarLeft ^ SwapArms;
            Vector3 s = image[srcLeft ? LeftShoulder : RightShoulder];
            Vector3 e = image[srcLeft ? LeftElbow : RightElbow];
            Vector3 w = image[srcLeft ? LeftWrist : RightWrist];

            Vector3 upperDir = MapImageDir(e - s, rig, aspect);
            Vector3 foreDir = MapImageDir(w - e, rig, aspect);
            if (upperDir.sqrMagnitude < 0.5f || foreDir.sqrMagnitude < 0.5f) return false;

            Vector3 anchor = avatarLeft ? rig.LeftAnchor : rig.RightAnchor;
            float upperLen = avatarLeft ? rig.LeftUpperLen : rig.RightUpperLen;
            float foreLen = avatarLeft ? rig.LeftForeLen : rig.RightForeLen;

            Vector3 elbow = SmoothElbow(avatarLeft, anchor + upperDir * upperLen);
            Vector3 wrist = SmoothWrist(avatarLeft, elbow + foreDir * foreLen);

            elbowLocal = elbow;
            wristLocal = wrist;
            wristRotation = LookFrom(wrist - elbow, rig.Up);
            return true;
        }

        /// <summary>
        /// Wrist-only fallback from the hand landmarker. When a face is present the wrist is placed
        /// relative to the avatar's head and scaled by apparent face size, so it is invariant to
        /// camera distance and framing; otherwise it spreads across the avatar's reach from the shoulder.
        /// </summary>
        public bool TryGetArmFromHand(Vector3 handWrist, Vector2 headImage, float faceSize, float aspect,
            in AvatarArmRig rig, bool avatarLeft, out Vector3 wristLocal, out Quaternion wristRotation)
        {
            wristLocal = Vector3.zero;
            wristRotation = Quaternion.identity;
            if (!rig.Valid) return false;

            Vector3 anchor = avatarLeft ? rig.LeftAnchor : rig.RightAnchor;
            Vector3 wrist;

            if (faceSize > 1e-4f && rig.HeadMetric > 1e-4f)
            {
                float metric = rig.HeadMetric * HeadReachGain / faceSize;
                float h = (handWrist.x - headImage.x) * aspect * (SwapArms ? -1f : 1f) * metric;
                float v = -(handWrist.y - headImage.y) * metric;
                wrist = rig.HeadLocal + h * rig.Right + v * rig.Up;
            }
            else
            {
                float reach = (avatarLeft ? rig.LeftUpperLen + rig.LeftForeLen : rig.RightUpperLen + rig.RightForeLen) * HandReachGain;
                float h = (0.5f - handWrist.x) * 2f * (SwapArms ? -1f : 1f);
                float v = (0.5f - handWrist.y) * 2f;
                wrist = anchor + (h * rig.Right + v * rig.Up) * reach;
            }

            wrist = SmoothWrist(avatarLeft, wrist);
            wristLocal = wrist;
            wristRotation = LookFrom(wrist - anchor, rig.Up);
            return true;
        }

        public void Reset()
        {
            _leftWristInit = _leftElbowInit = _rightWristInit = _rightElbowInit = false;
        }

        // Image landmarks are normalised, y-down. x is scaled by aspect so angles aren't squashed,
        // and z (monocular depth) is damped to forward. Maps onto the avatar's frontal basis.
        private Vector3 MapImageDir(Vector3 d, in AvatarArmRig rig, float aspect)
        {
            float right = d.x * aspect * (SwapArms ? -1f : 1f);
            float up = -d.y;
            float forward = d.z * DepthGain * (InvertForward ? -1f : 1f);
            Vector3 v = right * rig.Right + up * rig.Up + forward * rig.Forward;
            return v.sqrMagnitude > 1e-10f ? v.normalized : Vector3.zero;
        }

        private static Quaternion LookFrom(Vector3 forward, Vector3 up) =>
            forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, up) : Quaternion.identity;

        private Vector3 SmoothWrist(bool left, Vector3 wrist)
        {
            float t = 1f - Mathf.Clamp01(Smoothing);
            if (left)
            {
                _leftWrist = _leftWristInit ? Vector3.Lerp(_leftWrist, wrist, t) : wrist;
                _leftWristInit = true;
                return _leftWrist;
            }
            _rightWrist = _rightWristInit ? Vector3.Lerp(_rightWrist, wrist, t) : wrist;
            _rightWristInit = true;
            return _rightWrist;
        }

        private Vector3 SmoothElbow(bool left, Vector3 elbow)
        {
            float t = 1f - Mathf.Clamp01(Smoothing);
            if (left)
            {
                _leftElbow = _leftElbowInit ? Vector3.Lerp(_leftElbow, elbow, t) : elbow;
                _leftElbowInit = true;
                return _leftElbow;
            }
            _rightElbow = _rightElbowInit ? Vector3.Lerp(_rightElbow, elbow, t) : elbow;
            _rightElbowInit = true;
            return _rightElbow;
        }
    }
}
