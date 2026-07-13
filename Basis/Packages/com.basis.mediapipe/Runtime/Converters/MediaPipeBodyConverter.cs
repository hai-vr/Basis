using Basis.Scripts.Drivers;
using Unity.Mathematics;
using UnityEngine;

namespace Basis.MediaPipe
{
    /// <summary>
    /// Torso lean/twist/roll from PoseLandmarker world landmarks. Rotation only — the world landmarks are
    /// HIP-CENTRED, so they carry no global translation to read a sway out of even if we wanted one; a body
    /// shift shows up as the shoulders moving over the hips, which is a lean, and is already captured here.
    ///
    /// Reads the same body frame the arms retarget against and reports how far it has turned from a calibrated
    /// neutral. What comes out is an OFFSET, not a chest orientation — the caller composes it onto the avatar's
    /// own body so the chest keeps following the body and only picks up your torso on top.
    /// </summary>
    public sealed class MediaPipeBodyConverter
    {
        /// <summary>Scales the whole torso offset. Below 1 keeps it as a suggestion of movement rather than a copy.</summary>
        public float Strength = 0.6f;

        /// <summary>Ceiling on each axis before Strength, so a bad frame cannot throw the torso somewhere a spine will not go.</summary>
        public float MaxAngle = 35f;

        public float Smoothing = 0.7f;
        public bool InvertTwist = false;
        public bool InvertLean = false;
        public bool InvertRoll = false;

        private const float CutoffResponsive = 6f;
        private const float CutoffSmooth = 0.6f;
        private const float Beta = 1.5f;
        private const float DerivativeCutoff = 1f;

        /// <summary>Scales the shrug. 0 leaves the shoulders alone entirely.</summary>
        public float ShoulderStrength = 0.6f;

        /// <summary>Clavicle swing at a full shrug. Real shoulders barely rotate — this is a small motion.</summary>
        public float MaxShrugDegrees = 14f;

        /// <summary>Ceiling on the raw shrug signal (in shoulder-widths) before Strength.</summary>
        private const float MaxShrugSignal = 0.25f;

        private Quaternion _neutralInverse = Quaternion.identity;
        private bool _calibrated;
        private BasisEuroQuatState _euro;
        private Quaternion _sampled = Quaternion.identity;
        private Quaternion _carried = Quaternion.identity;
        private bool _hasSample;

        private float _leftRestDrop, _rightRestDrop;
        private bool _shrugCalibrated;
        private BasisEuroVec3State _shrugEuro;
        private Vector3 _shrugSampled;
        private Vector3 _shrugCarried;
        private bool _hasShrugSample;

        private float Cutoff => Mathf.Lerp(CutoffResponsive, CutoffSmooth, Mathf.Clamp01(Smoothing));

        public void Calibrate(in BasisMediaPipeResult result)
        {
            if (TryTorsoRotation(result, out Quaternion rot))
            {
                _neutralInverse = Quaternion.Inverse(rot);
                _calibrated = true;
            }
            if (MediaPipeSpace.TryShoulderDrop(result.PoseWorldLandmarks, out float left, out float right))
            {
                _leftRestDrop = left;
                _rightRestDrop = right;
                _shrugCalibrated = true;
            }
        }

        public void Reset()
        {
            _calibrated = false;
            _euro = default;
            _hasSample = false;
            _sampled = Quaternion.identity;
            _carried = Quaternion.identity;

            _shrugCalibrated = false;
            _shrugEuro = default;
            _hasShrugSample = false;
            _shrugSampled = Vector3.zero;
            _shrugCarried = Vector3.zero;
        }

        /// <summary>
        /// How far each shoulder has risen from its resting height, 0 = rest, 1 = a full shrug. Negative means
        /// dropped. Shoulders are independent, so a one-sided shrug survives.
        /// </summary>
        public bool TryGetShrug(in BasisMediaPipeResult result, in MediaPipeTiming timing, out float left, out float right)
        {
            left = 0f;
            right = 0f;
            if (ShoulderStrength <= 0f) return false;
            if (!MediaPipeSpace.TryShoulderDrop(result.PoseWorldLandmarks, out float leftDrop, out float rightDrop)) return false;

            if (!_shrugCalibrated)
            {
                _leftRestDrop = leftDrop;
                _rightRestDrop = rightDrop;
                _shrugCalibrated = true;
            }

            // Drop SHRINKS as the shoulder rises, so the rest height minus the current one is the shrug.
            Vector3 raw = new Vector3(_leftRestDrop - leftDrop, _rightRestDrop - rightDrop, 0f);

            if (timing.IsNewSample || !_hasShrugSample)
            {
                _shrugSampled = (Vector3)BasisFilterMath.EuroVec3(ref _shrugEuro, (float3)raw, timing.SampleDelta,
                    Cutoff, Beta, DerivativeCutoff);
                if (!_hasShrugSample)
                {
                    _shrugCarried = _shrugSampled;
                    _hasShrugSample = true;
                }
            }
            _shrugCarried = Vector3.Lerp(_shrugCarried, _shrugSampled,
                BasisFilterMath.Alpha(timing.CarryCutoff, timing.RenderDelta));

            left = Shrug(_shrugCarried.x);
            right = Shrug(_shrugCarried.y);
            return true;
        }

        private float Shrug(float raw) =>
            Mathf.Clamp(raw, -MaxShrugSignal, MaxShrugSignal) / MaxShrugSignal * ShoulderStrength;

        /// <summary>Torso lean/twist/roll relative to the calibrated neutral, in the avatar's body axes.</summary>
        public bool TryGetTorsoOffset(in BasisMediaPipeResult result, in MediaPipeTiming timing, out Quaternion offset)
        {
            offset = Quaternion.identity;
            if (!TryTorsoRotation(result, out Quaternion rot)) return false;

            if (!_calibrated)
            {
                _neutralInverse = Quaternion.Inverse(rot);
                _calibrated = true;
            }

            Vector3 euler = (_neutralInverse * rot).eulerAngles;
            float lean = Axis(euler.x, InvertLean);
            float twist = Axis(euler.y, InvertTwist);
            // Roll is the side-lean, and it used to be dropped on the floor. For someone sitting at a webcam it is
            // the most visible thing their torso does — you sway sideways far more than you twist.
            float roll = Axis(euler.z, InvertRoll);

            Quaternion target = Quaternion.Euler(lean, twist, roll);

            // Same two-clock split the arms use: one-euro on the camera's delta when a fresh sample lands, then a
            // carry slerp every rendered frame. A filter run at render rate over a held sample snaps and holds.
            if (timing.IsNewSample || !_hasSample)
            {
                _sampled = BasisFilterMath.EuroQuat(ref _euro, target, timing.SampleDelta,
                    Cutoff, Beta, DerivativeCutoff);
                if (!_hasSample)
                {
                    _carried = _sampled;
                    _hasSample = true;
                    offset = _carried;
                    return true;
                }
            }

            _carried = Quaternion.Slerp(_carried, _sampled,
                BasisFilterMath.Alpha(timing.CarryCutoff, timing.RenderDelta));
            offset = _carried;
            return true;
        }

        private float Axis(float raw, bool invert)
        {
            float angle = raw > 180f ? raw - 360f : raw;
            angle = Mathf.Clamp(angle, -MaxAngle, MaxAngle);
            return angle * (invert ? -1f : 1f) * Strength;
        }

        private static bool TryTorsoRotation(in BasisMediaPipeResult result, out Quaternion rot)
        {
            rot = Quaternion.identity;
            if (!result.HasPose) return false;
            return MediaPipeSpace.TryBodyFrame(result.PoseWorldLandmarks, out _, out rot);
        }
    }
}
