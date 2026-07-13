using Basis.Scripts.Drivers;
using Unity.Mathematics;
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

        // One-euro tuning, applied on the CAMERA clock. It only has to take the sensor noise off; the carry pass
        // below is what bridges the gap between samples, so this stays light rather than stacking two heavy
        // low-passes and burying the hands in latency. Beta matches the FBIK tracker default.
        private const float CutoffResponsive = 10f;
        private const float CutoffSmooth = 1.5f;
        private const float DepthCutoffScale = 0.4f;
        private const float Beta = 3.25f;
        private const float DerivativeCutoff = 1f;
        private const float ArmLengthTrackingHz = 0.5f;

        private ArmFilter _leftWrist, _leftElbow, _rightWrist, _rightElbow;
        private float _leftUserArm, _rightUserArm;

        /// <summary>
        /// Two-stage filter over a body-space offset.
        ///
        /// Stage one runs ONLY when a fresh camera sample lands, on the camera's own delta — a one-euro filter
        /// fed a held sample at render rate reads every new sample as a burst of speed, opens its cutoff and
        /// snaps to it, which is precisely the stutter. Depth gets its own filter on a lower cutoff, because
        /// monocular z is by far the noisiest axis.
        ///
        /// Stage two runs every rendered frame and carries the pose toward that sample, turning the camera's
        /// staircase back into continuous motion.
        ///
        /// Both stages work in BODY space, so the avatar turning underneath is applied instantly and never
        /// looks like hand movement to the filter.
        /// </summary>
        private struct ArmFilter
        {
            public BasisEuroVec3State Lateral;
            public BasisEuroVec3State Depth;
            public Vector3 Sampled;
            public Vector3 Carried;
            public bool HasSample;

            public Vector3 Apply(Vector3 body, in MediaPipeTiming timing, float cutoff)
            {
                if (timing.IsNewSample || !HasSample)
                {
                    float dt = timing.SampleDelta;
                    float3 lateral = BasisFilterMath.EuroVec3(ref Lateral, new float3(body.x, body.y, 0f),
                        dt, cutoff, Beta, DerivativeCutoff);
                    float3 depth = BasisFilterMath.EuroVec3(ref Depth, new float3(0f, 0f, body.z),
                        dt, cutoff * DepthCutoffScale, Beta, DerivativeCutoff);
                    Sampled = new Vector3(lateral.x, lateral.y, depth.z);

                    if (!HasSample)
                    {
                        Carried = Sampled;
                        HasSample = true;
                        return Carried;
                    }
                }

                Carried = Vector3.Lerp(Carried, Sampled,
                    BasisFilterMath.Alpha(timing.CarryCutoff, timing.RenderDelta));
                return Carried;
            }

            public void Reset()
            {
                Lateral = default;
                Depth = default;
                HasSample = false;
            }
        }

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

        private float Cutoff => Mathf.Lerp(CutoffResponsive, CutoffSmooth, Mathf.Clamp01(Smoothing));

        /// <summary>Full arm reconstruction from the metric body pose, with a kinematically valid elbow.</summary>
        public bool TryGetArm(Vector3[] pose, in AvatarArmRig rig, bool avatarLeft, in MediaPipeTiming timing,
            out Vector3 wristLocal, out Vector3 elbowLocal, out Quaternion wristRotation)
        {
            wristLocal = Vector3.zero;
            elbowLocal = Vector3.zero;
            wristRotation = Quaternion.identity;

            if (!rig.Valid || pose == null || pose.Length < MediaPipeSpace.PoseCount) return false;
            if (!MediaPipeSpace.TryBodyFrame(pose, out _, out Quaternion bodyFrame)) return false;

            Vector3 shoulder = pose[avatarLeft ? MediaPipeSpace.LeftShoulder : MediaPipeSpace.RightShoulder];
            Vector3 elbow = pose[avatarLeft ? MediaPipeSpace.LeftElbow : MediaPipeSpace.RightElbow];
            Vector3 wrist = pose[avatarLeft ? MediaPipeSpace.LeftWrist : MediaPipeSpace.RightWrist];

            float userArm = TrackUserArm(avatarLeft,
                Vector3.Distance(shoulder, elbow) + Vector3.Distance(elbow, wrist), timing);
            float upperLen = avatarLeft ? rig.LeftUpperLen : rig.RightUpperLen;
            float foreLen = avatarLeft ? rig.LeftForeLen : rig.RightForeLen;
            float avatarArm = upperLen + foreLen;
            if (userArm < 1e-3f || avatarArm < 1e-4f) return false;

            Quaternion toBody = Quaternion.Inverse(bodyFrame);
            Vector3 wristBody = toBody * (wrist - shoulder);
            Vector3 elbowBody = toBody * (elbow - shoulder);
            Vector3 headBody = toBody * (pose[MediaPipeSpace.Nose] - shoulder);

            float cutoff = Cutoff;
            wristBody = avatarLeft
                ? _leftWrist.Apply(wristBody, in timing, cutoff)
                : _rightWrist.Apply(wristBody, in timing, cutoff);
            elbowBody = avatarLeft
                ? _leftElbow.Apply(elbowBody, in timing, cutoff)
                : _rightElbow.Apply(elbowBody, in timing, cutoff);

            Vector3 anchor = avatarLeft ? rig.LeftAnchor : rig.RightAnchor;
            float reach = avatarArm / userArm;
            float lift = VerticalScale(headBody, Vector3.Dot(rig.HeadLocal - anchor, rig.Up), wristBody.y, reach);

            Vector3 wristTarget = ClampReach(anchor, Place(anchor, wristBody, reach, lift, in rig), avatarArm);
            Vector3 measuredElbow = Place(anchor, elbowBody, reach, lift, in rig);

            wristLocal = wristTarget;
            elbowLocal = SolveElbow(anchor, wristTarget, measuredElbow, upperLen, foreLen, rig.Up);
            wristRotation = LookFrom(wristLocal - elbowLocal, rig.Up);
            return true;
        }

        /// <summary>
        /// Wrist-only fallback for when the body pose is lost but a hand is still tracked. Placed relative
        /// to the avatar's head and scaled by apparent face size, so it stays put as the user moves toward
        /// or away from the camera. Image x runs camera-right, which is the user's left, hence the negation.
        /// </summary>
        public bool TryGetArmFromHand(Vector3 handWrist, Vector2 headImage, float faceSize, float aspect,
            in AvatarArmRig rig, bool avatarLeft, in MediaPipeTiming timing, out Vector3 wristLocal, out Quaternion wristRotation)
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

            Vector3 offset = new Vector3(h, v, 0f);
            offset = avatarLeft
                ? _leftWrist.Apply(offset, in timing, Cutoff)
                : _rightWrist.Apply(offset, in timing, Cutoff);

            wristLocal = ClampReach(anchor, rig.HeadLocal + offset.x * rig.Right + offset.y * rig.Up, avatarArm);
            wristRotation = LookFrom(wristLocal - anchor, rig.Up);
            return true;
        }

        public void Reset()
        {
            _leftWrist.Reset();
            _leftElbow.Reset();
            _rightWrist.Reset();
            _rightElbow.Reset();
            _leftUserArm = _rightUserArm = 0f;
        }

        /// <summary>
        /// Puts the elbow on the circle of positions that the avatar's own bone lengths actually allow, picking
        /// the point around that circle nearest the measured elbow. Scaling the measured elbow directly (as this
        /// used to) leaves |wrist-elbow| disagreeing with the forearm, so the LowerArm tracker ends up fighting
        /// the arm solver instead of hinting it.
        /// </summary>
        private static Vector3 SolveElbow(Vector3 shoulder, Vector3 wrist, Vector3 measured,
            float upperLen, float foreLen, Vector3 fallbackUp)
        {
            Vector3 toWrist = wrist - shoulder;
            float span = toWrist.magnitude;
            if (span < 1e-4f) return measured;

            Vector3 axis = toWrist / span;
            if (span >= upperLen + foreLen - 1e-4f)
            {
                return shoulder + axis * upperLen;
            }

            float along = (upperLen * upperLen - foreLen * foreLen + span * span) / (2f * span);
            float radiusSq = upperLen * upperLen - along * along;
            if (radiusSq <= 1e-8f) return shoulder + axis * upperLen;

            Vector3 center = shoulder + axis * along;
            Vector3 swivel = Vector3.ProjectOnPlane(measured - center, axis);
            if (swivel.sqrMagnitude < 1e-8f)
            {
                swivel = Vector3.ProjectOnPlane(-fallbackUp, axis);
            }
            if (swivel.sqrMagnitude < 1e-8f) return center;

            return center + swivel.normalized * Mathf.Sqrt(radiusSq);
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

        // Arm length is a body constant, so this rejects sample noise while still adapting if the model's metric
        // estimate drifts. It advances on the CAMERA clock — stepping it every rendered frame over a held sample
        // would make it converge many times faster than intended.
        private float TrackUserArm(bool left, float measured, in MediaPipeTiming timing)
        {
            if (!timing.IsNewSample) return left ? _leftUserArm : _rightUserArm;

            float alpha = 1f - Mathf.Exp(-ArmLengthTrackingHz * timing.SampleDelta);
            if (left)
            {
                _leftUserArm = _leftUserArm > 1e-4f && measured > 1e-3f
                    ? Mathf.Lerp(_leftUserArm, measured, alpha)
                    : Mathf.Max(_leftUserArm, measured);
                return _leftUserArm;
            }
            _rightUserArm = _rightUserArm > 1e-4f && measured > 1e-3f
                ? Mathf.Lerp(_rightUserArm, measured, alpha)
                : Mathf.Max(_rightUserArm, measured);
            return _rightUserArm;
        }

        private static Quaternion LookFrom(Vector3 forward, Vector3 up) =>
            forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward.normalized, up) : Quaternion.identity;
    }
}
