using Basis.Scripts.Drivers;
using UnityEngine;
namespace Basis.MediaPipe
{
    public sealed class MediaPipeBodyConverter
    {
        public float Strength = 0.6f, MaxAngle = 35f, Smoothing = 0.7f;
        public bool InvertTwist = false, InvertLean = false, InvertRoll = false;
        private const float CutoffResponsive = 6f, CutoffSmooth = 0.6f, Beta = 1.5f, DerivativeCutoff = 1f;
        private Quaternion neutralInverse = Quaternion.identity;
        private bool calibrated;
        private BasisEuroQuatState euro;
        private Quaternion sampled = Quaternion.identity, carried = Quaternion.identity;
        private bool hasSample;
        private float Cutoff => Mathf.Lerp(CutoffResponsive, CutoffSmooth, Mathf.Clamp01(Smoothing));
        public void Calibrate(in BasisMediaPipeResult result)
        {
            if (TryTorsoRotation(result, out Quaternion rot))
            {
                neutralInverse = Quaternion.Inverse(rot);
                calibrated = true;
            }
        }
        public void Reset()
        {
            calibrated = false;
            euro = default;
            hasSample = false;
            sampled = Quaternion.identity;
            carried = Quaternion.identity;

        }
        public bool TryGetTorsoOffset(in BasisMediaPipeResult result, in MediaPipeTiming timing, out Quaternion offset)
        {
            offset = Quaternion.identity;
            if (!TryTorsoRotation(result, out Quaternion rot)) return false;

            if (!calibrated)
            {
                neutralInverse = Quaternion.Inverse(rot);
                calibrated = true;
            }

            Vector3 euler = (neutralInverse * rot).eulerAngles;
            float lean = Axis(euler.x, InvertLean), twist = Axis(euler.y, InvertTwist);
            // Roll is the side-lean, and it used to be dropped on the floor. For someone sitting at a webcam it is
            // the most visible thing their torso does — you sway sideways far more than you twist.
            float roll = Axis(euler.z, InvertRoll);
            Quaternion target = Quaternion.Euler(lean, twist, roll);

            // Same two-clock split the arms use: one-euro on the camera's delta when a fresh sample lands, then a
            // carry slerp every rendered frame. A filter run at render rate over a held sample snaps and holds.
            if (timing.IsNewSample || !hasSample)
            {
                sampled = BasisFilterMath.EuroQuat(ref euro, target, timing.SampleDelta, Cutoff, Beta, DerivativeCutoff);
                if (!hasSample)
                {
                    carried = sampled;
                    hasSample = true;
                    offset = carried;
                    return true;
                }
            }

            carried = Quaternion.Slerp(carried, sampled, BasisFilterMath.Alpha(timing.CarryCutoff, timing.RenderDelta));
            offset = carried;
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
