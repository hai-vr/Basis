using NUnit.Framework;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Regression tests for the calibration "feel" math (<see cref="BasisCalibrationMath.ComputeDeviceScale"/>),
    /// the formula <see cref="BasisHeightDriver.ChooseHeightToUse"/> runs to turn a measured eye height into the
    /// DeviceScale the camera is scaled by. The avatar feels right iff the DeviceScale denominator equals the
    /// player's TRUE standing eye height; these pin what happens when it doesn't -- the cause of the report that
    /// people must nudge once or twice to stop feeling too tall.
    ///
    /// Backends measure the denominator differently: OpenXR binds the head to centerEyePosition so the tracked
    /// point IS the eye (eyeReference 0), while OpenVR tracks the HMD pose origin sitting a gap g below the eyes
    /// and bridges it with CenterEyeVerticalOffset. If that bridge under-reports the gap (or is 0 because the
    /// SteamVR eye transform wasn't ready at capture), the denominator is short and the avatar renders too tall.
    /// </summary>
    public class BasisCalibrationHeightTests
    {
        // Real-world standing eye heights, avatar authored eye heights, and custom avatar scales to sweep.
        static readonly float[] StandingEye = { 1.40f, 1.55f, 1.61f, 1.75f, 1.90f };
        static readonly float[] AvatarEye = { 1.30f, 1.50f, 1.61f, 1.72f };
        static readonly float[] UpScale = { 0.80f, 1.00f, 1.50f };

        /// <summary>The viewpoint the camera renders at: real standing eye height scaled by DeviceScale
        /// (BasisLocalCameraDriver sets camera.localScale = DeviceScale).</summary>
        static float RenderedViewpoint(float trueStandingEye, float deviceScale) => trueStandingEye * deviceScale;

        [Test]
        public void CorrectDenominator_ViewpointLandsExactlyAtAvatarEye()
        {
            foreach (float E in StandingEye)
                foreach (float A in AvatarEye)
                    foreach (float u in UpScale)
                    {
                        // Denominator measured perfectly (== true standing eye): player height E, no reference
                        // gap, no nudge. This is the well-formed calibration the formula assumes.
                        float deviceScale = BasisCalibrationMath.ComputeDeviceScale(A, u, E, 0f, 0f);
                        float avatarRenderedEye = A * u;
                        Assert.That(RenderedViewpoint(E, deviceScale), Is.EqualTo(avatarRenderedEye).Within(1e-4f),
                            $"E={E} A={A} u={u}: a correctly measured eye height must land the viewpoint on the avatar's eye.");
                    }
        }

        [Test]
        public void OpenXrTrackedPointIsEye_NoNudgeNeeded()
        {
            // OpenXR: tracked point is centerEyePosition, so the measured height already IS the standing eye
            // and eyeReference is 0. No systematic bias, no nudge.
            foreach (float E in StandingEye)
                foreach (float A in AvatarEye)
                {
                    float deviceScale = BasisCalibrationMath.ComputeDeviceScale(A, 1f, E, 0f, 0f);
                    Assert.That(RenderedViewpoint(E, deviceScale), Is.EqualTo(A).Within(1e-4f),
                        $"E={E} A={A}: the eye-tracked backend must be unbiased without a nudge.");
                }
        }

        [Test]
        public void UnderBridgedEyeReference_RendersTooTall_ByTheShortfallRatio()
        {
            // OpenVR: the HMD pose origin sits a gap g below the eyes; the captured CenterEyeVerticalOffset o
            // should equal g but under-reports it (o < g, or 0 when SteamVR's eye transform isn't ready). The
            // denominator is then short by (g - o), so DeviceScale is too high and the viewpoint too tall.
            foreach (float E in StandingEye)
                foreach (float g in new[] { 0.05f, 0.10f, 0.15f })
                    foreach (float o in new[] { 0f, g * 0.5f })
                    {
                        const float A = 1.61f, u = 1f;
                        float deviceOriginY = E - g;        // what OpenVR reports as PlayerEyeHeight
                        float shortfall = g - o;            // the un-bridged remainder
                        float deviceScale = BasisCalibrationMath.ComputeDeviceScale(A, u, deviceOriginY, o, 0f);

                        float viewpoint = RenderedViewpoint(E, deviceScale);
                        float avatarRenderedEye = A * u;
                        Assert.That(viewpoint, Is.GreaterThan(avatarRenderedEye),
                            $"E={E} g={g} o={o}: an under-bridged eye reference must render too tall.");
                        // The tallness is exactly E/(E - shortfall): the bias is the un-bridged measurement gap.
                        Assert.That(viewpoint / avatarRenderedEye, Is.EqualTo(E / (E - shortfall)).Within(1e-4f),
                            $"E={E} g={g} o={o}: too-tall factor must equal E/(E-shortfall).");
                    }
        }

        [Test]
        public void Nudge_EqualToTheShortfall_RestoresCorrectFeel()
        {
            // The +0.1 nudge people apply is AdditionalPlayerHeight added to the denominator. Adding exactly the
            // measurement shortfall makes the denominator true again -- so the nudge they reach for is a direct
            // readout of how far the eye reference under-measured. ~0.10 m of shortfall == one-to-two nudges.
            foreach (float E in StandingEye)
                foreach (float g in new[] { 0.05f, 0.10f, 0.15f })
                {
                    const float A = 1.61f, u = 1f;
                    float deviceOriginY = E - g;
                    float shortfall = g; // worst case: eye reference reported 0

                    float biased = BasisCalibrationMath.ComputeDeviceScale(A, u, deviceOriginY, 0f, 0f);
                    Assert.That(RenderedViewpoint(E, biased), Is.Not.EqualTo(A * u).Within(1e-3f),
                        "sanity: the un-nudged calibration is off.");

                    float nudged = BasisCalibrationMath.ComputeDeviceScale(A, u, deviceOriginY, 0f, shortfall);
                    Assert.That(RenderedViewpoint(E, nudged), Is.EqualTo(A * u).Within(1e-4f),
                        $"E={E} g={g}: a nudge equal to the shortfall must restore the avatar eye exactly.");
                }
        }

        [Test]
        public void DegenerateDenominator_ReturnsOne_NoNaN()
        {
            // A zero/negative measured height (bad poll) must never produce NaN/Inf scale that poisons bones.
            float deviceScale = BasisCalibrationMath.ComputeDeviceScale(1.61f, 1f, 0f, 0f, 0f);
            Assert.That(deviceScale, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(float.IsNaN(deviceScale) || float.IsInfinity(deviceScale), Is.False);
        }
    }
}
