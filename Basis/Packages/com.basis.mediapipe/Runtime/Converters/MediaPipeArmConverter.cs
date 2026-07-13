using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// Retargets the arms from PoseLandmarker WORLD landmarks (metric metres, real depth) instead of
    /// image positions.
    ///
    /// Wrist and elbow are measured relative to the user's own shoulder, expressed in a body frame built
    /// from their shoulder line and torso, then divided by the user's own arm length. What is left is a
    /// unitless "fraction of my reach, in my body's frame" vector that does not care how far from the
    /// camera the user sits or how long their arms are. Multiplying by the avatar's arm length and
    /// applying the avatar's body frame — built by the exact same rule — puts the hand where it is on you.
    ///
    /// Reach matching alone still misses when the avatar's head sits at a different height above the
    /// shoulders than yours, which is precisely the "hand next to my face" case, so the vertical component
    /// is blended toward a head-relative scale as the hand rises to head height.
    /// </summary>
    public sealed class MediaPipeArmConverter
    {
        public float Smoothing = 0.5f;
        public float HeadAnchor = 1f;
        public float MaxReach = 0.98f;
        public float HandReachGain = 1.1f;
        public bool SwapArms = false;

        private const float ArmLengthTracking = 0.05f;

        private Vector3 _leftElbow, _leftWrist, _rightElbow, _rightWrist;
        private bool _leftWristInit, _leftElbowInit, _rightWristInit, _rightElbowInit;
        private float _leftUserArm, _rightUserArm;

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

        /// <summary>Full arm reconstruction from the metric body pose, with a real elbow pole.</summary>
        public bool TryGetArm(Vector3[] pose, in AvatarArmRig rig, bool avatarLeft,
            out Vector3 wristLocal, out Vector3 elbowLocal, out Quaternion wristRotation)
        {
            wristLocal = Vector3.zero;
            elbowLocal = Vector3.zero;
            wristRotation = Quaternion.identity;

            if (!rig.Valid || pose == null || pose.Length < MediaPipeSpace.PoseCount) return false;
            if (!MediaPipeSpace.TryBodyFrame(pose, out _, out Quaternion bodyFrame)) return false;

            bool srcLeft = avatarLeft ^ SwapArms;
            Vector3 shoulder = pose[srcLeft ? MediaPipeSpace.LeftShoulder : MediaPipeSpace.RightShoulder];
            Vector3 elbow = pose[srcLeft ? MediaPipeSpace.LeftElbow : MediaPipeSpace.RightElbow];
            Vector3 wrist = pose[srcLeft ? MediaPipeSpace.LeftWrist : MediaPipeSpace.RightWrist];

            float userArm = TrackUserArm(avatarLeft,
                Vector3.Distance(shoulder, elbow) + Vector3.Distance(elbow, wrist));
            float upperLen = avatarLeft ? rig.LeftUpperLen : rig.RightUpperLen;
            float foreLen = avatarLeft ? rig.LeftForeLen : rig.RightForeLen;
            float avatarArm = upperLen + foreLen;
            if (userArm < 1e-3f || avatarArm < 1e-4f) return false;

            Quaternion toBody = Quaternion.Inverse(bodyFrame);
            Vector3 wristBody = toBody * (wrist - shoulder);
            Vector3 elbowBody = toBody * (elbow - shoulder);

            Vector3 anchor = avatarLeft ? rig.LeftAnchor : rig.RightAnchor;
            float reach = avatarArm / userArm;
            float lift = VerticalScale(toBody * (pose[MediaPipeSpace.Nose] - shoulder),
                Vector3.Dot(rig.HeadLocal - anchor, rig.Up), wristBody.y, reach);

            Vector3 wristTarget = ClampReach(anchor, Place(anchor, wristBody, reach, lift, in rig), avatarArm);
            Vector3 elbowTarget = ClampReach(anchor, Place(anchor, elbowBody, reach, lift, in rig), upperLen);

            elbowLocal = SmoothElbow(avatarLeft, elbowTarget);
            wristLocal = SmoothWrist(avatarLeft, wristTarget);
            wristRotation = LookFrom(wristLocal - elbowLocal, rig.Up);
            return true;
        }

        /// <summary>
        /// Wrist-only fallback for when the body pose is lost but a hand is still tracked. Placed relative
        /// to the avatar's head and scaled by apparent face size, so it stays put as the user moves toward
        /// or away from the camera. Image x runs camera-right, which is the user's left, hence the negation.
        /// </summary>
        public bool TryGetArmFromHand(Vector3 handWrist, Vector2 headImage, float faceSize, float aspect,
            in AvatarArmRig rig, bool avatarLeft, out Vector3 wristLocal, out Quaternion wristRotation)
        {
            wristLocal = Vector3.zero;
            wristRotation = Quaternion.identity;
            if (!rig.Valid || faceSize <= 1e-4f || rig.HeadMetric <= 1e-4f) return false;

            float metric = rig.HeadMetric * HandReachGain / faceSize;
            float h = -(handWrist.x - headImage.x) * aspect * metric;
            float v = (handWrist.y - headImage.y) * metric;

            Vector3 anchor = avatarLeft ? rig.LeftAnchor : rig.RightAnchor;
            float avatarArm = avatarLeft
                ? rig.LeftUpperLen + rig.LeftForeLen
                : rig.RightUpperLen + rig.RightForeLen;

            Vector3 wrist = ClampReach(anchor, rig.HeadLocal + h * rig.Right + v * rig.Up, avatarArm);
            wristLocal = SmoothWrist(avatarLeft, wrist);
            wristRotation = LookFrom(wristLocal - anchor, rig.Up);
            return true;
        }

        public void Reset()
        {
            _leftWristInit = _leftElbowInit = _rightWristInit = _rightElbowInit = false;
            _leftUserArm = _rightUserArm = 0f;
        }

        /// <summary>
        /// Blends the vertical scale from pure reach matching at shoulder height toward a head-relative
        /// scale at head height, so a hand held beside the face lands beside the avatar's face even when
        /// the avatar's neck is proportioned differently.
        /// </summary>
        private float VerticalScale(Vector3 headBody, float avatarHeadUp, float wristUp, float reach)
        {
            if (HeadAnchor <= 0f || headBody.y < 1e-3f || avatarHeadUp < 1e-4f) return reach;

            float headScale = avatarHeadUp / headBody.y;
            float t = Mathf.Clamp01(wristUp / headBody.y) * Mathf.Clamp01(HeadAnchor);
            return Mathf.Lerp(reach, headScale, t);
        }

        private static Vector3 Place(Vector3 anchor, Vector3 body, float reach, float lift, in AvatarArmRig rig) =>
            anchor
            + (body.x * reach) * rig.Right
            + (body.y * lift) * rig.Up
            + (body.z * reach) * rig.Forward;

        private Vector3 ClampReach(Vector3 anchor, Vector3 target, float limit)
        {
            Vector3 delta = target - anchor;
            float max = limit * Mathf.Max(0.1f, MaxReach);
            float distance = delta.magnitude;
            return distance > max && distance > 1e-6f ? anchor + delta * (max / distance) : target;
        }

        // Arm length is a body constant, so this rejects per-frame landmark noise while still adapting
        // if the model's metric estimate drifts as the user moves around the frame.
        private float TrackUserArm(bool left, float measured)
        {
            if (left)
            {
                _leftUserArm = _leftUserArm > 1e-4f && measured > 1e-3f
                    ? Mathf.Lerp(_leftUserArm, measured, ArmLengthTracking)
                    : Mathf.Max(_leftUserArm, measured);
                return _leftUserArm;
            }
            _rightUserArm = _rightUserArm > 1e-4f && measured > 1e-3f
                ? Mathf.Lerp(_rightUserArm, measured, ArmLengthTracking)
                : Mathf.Max(_rightUserArm, measured);
            return _rightUserArm;
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
