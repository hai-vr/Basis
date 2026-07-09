using NUnit.Framework;

namespace Basis.Tests.IK
{
    /// <summary>
    /// Regression tests for the Auto height-mode decision
    /// (<see cref="BasisCalibrationMath.AutoHeightModePicksArmSpan"/>): pick the metric pair that
    /// yields the LARGER DeviceScale. The point of "larger" is a reach guarantee — with
    /// DS = max(avatarEye/playerEye, avatarSpan/playerSpan), the player's scaled arm span always
    /// reaches at least the avatar's span (the avatar's arms can always straighten) and the scaled
    /// eye height lands at-or-above the avatar's eyes. Picking the smaller pair instead would leave
    /// long-armed avatars with arms the player can never extend.
    /// </summary>
    public class BasisAutoHeightModeTests
    {
        [Test]
        public void PicksArmSpan_ExactlyWhenAvatarIsRelativelyLongerArmed()
        {
            // Player: 1.61 m eyes, 1.61 m span (ape index 1). Avatar longer-armed than the player
            // (span ratio > eye ratio) → arm span; shorter-armed → eye height.
            Assert.That(BasisCalibrationMath.AutoHeightModePicksArmSpan(1.5f, 1.61f, 1.8f, 1.61f), Is.True,
                "long-armed avatar must scale by arm span or the player can never straighten its arms.");
            Assert.That(BasisCalibrationMath.AutoHeightModePicksArmSpan(1.5f, 1.61f, 1.3f, 1.61f), Is.False,
                "short-armed avatar must scale by eye height (arm-span scaling would shrink the world view).");
        }

        [Test]
        public void EqualRatios_PreferEyeHeight()
        {
            // Proportional avatar (same ape index as the player): the two scales are identical, so
            // prefer the eye-height pair — it is the stabler measurement and carries the standing
            // eye-height corrections.
            Assert.That(BasisCalibrationMath.AutoHeightModePicksArmSpan(1.5f, 1.61f, 1.5f, 1.61f), Is.False);
        }

        [Test]
        public void ChosenScale_AlwaysCoversAvatarReach_AndEyeHeight()
        {
            // The guarantee itself, swept over player/avatar proportion combinations: with the picked
            // pair, scaled player span >= avatar span AND scaled player eye >= avatar eye.
            float[] playerEyes = { 1.40f, 1.61f, 1.85f };
            float[] playerSpans = { 1.30f, 1.61f, 1.95f };
            float[] avatarEyes = { 0.9f, 1.5f, 2.1f };
            float[] avatarSpans = { 0.8f, 1.5f, 2.4f };
            foreach (float pe in playerEyes)
            foreach (float ps in playerSpans)
            foreach (float ae in avatarEyes)
            foreach (float asp in avatarSpans)
            {
                bool armSpan = BasisCalibrationMath.AutoHeightModePicksArmSpan(ae, pe, asp, ps);
                float deviceScale = armSpan ? asp / ps : ae / pe;
                Assert.That(ps * deviceScale, Is.GreaterThanOrEqualTo(asp - 1e-4f),
                    $"pe={pe} ps={ps} ae={ae} as={asp}: scaled reach must cover the avatar's arm span.");
                Assert.That(pe * deviceScale, Is.GreaterThanOrEqualTo(ae - 1e-4f),
                    $"pe={pe} ps={ps} ae={ae} as={asp}: scaled eye height must land at-or-above the avatar's eyes.");
            }
        }

        [Test]
        public void DegenerateInputs_FallBackToEyeHeight()
        {
            // A missing/garbage span measurement must never flip the mode.
            Assert.That(BasisCalibrationMath.AutoHeightModePicksArmSpan(1.5f, 1.61f, 0f, 1.61f), Is.False);
            Assert.That(BasisCalibrationMath.AutoHeightModePicksArmSpan(1.5f, 1.61f, 1.8f, 0f), Is.False);
            Assert.That(BasisCalibrationMath.AutoHeightModePicksArmSpan(0f, 1.61f, 1.8f, 1.61f), Is.False);
            Assert.That(BasisCalibrationMath.AutoHeightModePicksArmSpan(1.5f, 0f, 1.8f, 1.61f), Is.False);
        }
    }
}
