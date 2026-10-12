using Basis.IK;
using Basis.Scripts.BasisSdk.Players;
using Basis.Scripts.Common;
using NUnit.Framework;
using UnityEngine;
namespace Basis.Tests.Fitting
{
    public class BasisFitResolverTests
    {
        static BasisScaleInputs Inputs(BasisSelectedHeightMode mode, float playerEye, float playerSpan, float avatarEye, float avatarSpan, float shoulders = 0.35f, float k = 1f, float deviation = 0.15f)
        {
            return new BasisScaleInputs { Mode = mode, PlayerEye = playerEye, PlayerSpan = playerSpan, AvatarEye = avatarEye, AvatarSpan = avatarSpan, AvatarShoulderWidth = shoulders, AppliedUpScale = k, FitDeviation = deviation, SpanUsable = true };
        }
        static float StandingEyeInAvatar(in BasisScaleInputs i, in BasisScaleState s) => (i.PlayerEye + i.PlayerEyeOffset + s.GroundingOffset) * s.DeviceScale;
        static BasisBodyFitMeasurements Body(float playerEye, float playerSpan, float avatarEye, float avatarSpan, float uniformScale)
        {
            return new BasisBodyFitMeasurements
            {
                PlayerEyeHeight = playerEye,
                PlayerArmSpan = playerSpan,
                AvatarEyeHeight = avatarEye,
                AvatarArmSpan = avatarSpan,
                AvatarShoulderWidth = 0.35f,
                UniformScale = uniformScale,
            };
        }
        [Test]
        public void BestFitStaysNearTheEyeMatchForTinyAndGiantAvatars([Values(0.25f, 0.42f, 2.6f, 4f)] float eyeRatio, [Values(0.7f, 1.3f)] float avatarArmRatio)
        {
            BasisScaleInputs inputs = Inputs(BasisSelectedHeightMode.BestFit, 1.65f, 1.45f, 1.65f * eyeRatio, 1.45f * eyeRatio * avatarArmRatio, 0.35f * eyeRatio);
            BasisScaleState state = BasisScaleResolver.Resolve(inputs);
            Assert.That(state.BestFit.IsValid, Is.True);
            Assert.That(state.UniformScale / eyeRatio, Is.InRange(1f / 1.05f - 1e-4f, 1.05f + 1e-4f), "BestFit may only trade 5% of eye match for arms, whatever the avatar's size");
        }
        [Test]
        public void EveryModeLandsTheStandingEyeOnTheAvatarEye([Values(BasisSelectedHeightMode.EyeHeight, BasisSelectedHeightMode.ArmSpan, BasisSelectedHeightMode.BestFit, BasisSelectedHeightMode.Auto)] BasisSelectedHeightMode mode, [Values(0.6f, 1f, 1.4f)] float k, [Values(0.7f, 1f, 1.3f)] float avatarArmRatio, [Values(0f, 0.02f)] float eyeOffset)
        {
            var inputs = Inputs(mode, 1.62f, 1.45f, 1.55f, 1.45f * (1.55f / 1.62f) * avatarArmRatio, k: k);
            inputs.PlayerEyeOffset = eyeOffset;
            BasisScaleState state = BasisScaleResolver.Resolve(inputs);
            Assert.That(StandingEyeInAvatar(inputs, state), Is.EqualTo(1.55f * k).Within(1e-4f), $"{mode} k {k} arms {avatarArmRatio}: the camera must sit on the avatar's eye, never above its head");
        }
        [Test]
        public void EyeModeNeverMovesThePlaySpace()
        {
            var inputs = Inputs(BasisSelectedHeightMode.EyeHeight, 1.62f, 1.2f, 1.3f, 1.6f);
            BasisScaleState state = BasisScaleResolver.Resolve(inputs);
            Assert.That(state.GroundingOffset, Is.EqualTo(0f));
            Assert.That(state.UniformScale, Is.EqualTo(1.3f / 1.62f).Within(1e-6f));
        }
        [Test]
        public void BestFitStaysOnTheEyeWhileTheArmFitCanAbsorbTheMismatch()
        {
            float eyeRatio = 1.5f / 1.62f;
            var inputs = Inputs(BasisSelectedHeightMode.BestFit, 1.62f, 1.45f, 1.5f, 1.45f * eyeRatio * 1.08f);
            BasisScaleState state = BasisScaleResolver.Resolve(inputs);
            Assert.That(state.BestFit.Status, Is.EqualTo(BasisScaleFitStatus.EyeExact));
            Assert.That(state.UniformScale, Is.EqualTo(eyeRatio).Within(1e-5f));
            Assert.That(state.GroundingOffset, Is.EqualTo(0f));
        }
        [Test]
        public void BestFitMovesAtMostTheWindowForArmsTheFitCannotReach([Values(0.55f, 1.6f)] float avatarArmRatio)
        {
            float eyeRatio = 1.5f / 1.62f;
            var inputs = Inputs(BasisSelectedHeightMode.BestFit, 1.62f, 1.45f, 1.5f, 0.35f + (1.45f * eyeRatio - 0.35f) * avatarArmRatio);
            BasisScaleState state = BasisScaleResolver.Resolve(inputs);
            float move = state.UniformScale / eyeRatio;
            Assert.That(move, Is.InRange(1f / (1f + BasisScaleFitCore.DefaultMaxEyeDeviation) - 1e-4f, 1f + BasisScaleFitCore.DefaultMaxEyeDeviation + 1e-4f));
            Assert.That(Mathf.Abs(move - 1f), Is.GreaterThan(0.01f), "an unreachable arm mismatch should still use the window");
            Assert.That(Mathf.Sign(state.GroundingOffset), Is.EqualTo(avatarArmRatio < 1f ? 1f : -1f), "short avatar arms lift the play space, long ones lower it");
        }
        [Test]
        public void ArmSpanModeMatchesTheAuthoredSpanAndLeavesTheArmsUnstretched()
        {
            var inputs = Inputs(BasisSelectedHeightMode.ArmSpan, 1.62f, 1.45f, 1.5f, 1.2f);
            BasisScaleState state = BasisScaleResolver.Resolve(inputs);
            Assert.That(state.UniformScale, Is.EqualTo(1.2f / 1.45f).Within(1e-6f));
            BasisBodyFitResult fit = BasisBodyFitCore.Solve(Body(1.62f, 1.45f, 1.5f, 1.2f, state.UniformScale), 0.15f);
            Assert.That(fit.HasArmFit, Is.True);
            Assert.That(fit.ArmScale, Is.EqualTo(1f).Within(1e-5f));
        }
        [Test]
        public void SeatedAndDesktopNeverOffsetThePlaySpace()
        {
            var seated = Inputs(BasisSelectedHeightMode.ArmSpan, 1.62f, 1.45f, 1.5f, 1.2f);
            seated.Seated = true;
            Assert.That(BasisScaleResolver.Resolve(seated).GroundingOffset, Is.EqualTo(0f));
            var seatedBestFit = Inputs(BasisSelectedHeightMode.BestFit, 1.62f, 1.45f, 1.5f, 0.8f);
            seatedBestFit.Seated = true;
            Assert.That(BasisScaleResolver.Resolve(seatedBestFit).UniformScale, Is.EqualTo(1.5f / 1.62f).Within(1e-6f));
            var desktop = Inputs(BasisSelectedHeightMode.ArmSpan, 1.62f, 1.45f, 1.5f, 1.2f);
            desktop.Desktop = true;
            BasisScaleState state = BasisScaleResolver.Resolve(desktop);
            Assert.That(state.Mode, Is.EqualTo(BasisSelectedHeightMode.EyeHeight));
            Assert.That(state.GroundingOffset, Is.EqualTo(0f));
        }
        [Test]
        public void TheReportedSetupNoLongerSquashesTheBodyByThirteenPercent()
        {
            var inputs = Inputs(BasisSelectedHeightMode.BestFit, 1.544f, 1.454f, 1.604f, 1.272f, 0.278f);
            BasisScaleState state = BasisScaleResolver.Resolve(inputs);
            float eyeRatio = 1.604f / 1.544f;
            Assert.That(state.UniformScale / eyeRatio, Is.GreaterThanOrEqualTo(1f / 1.05f - 1e-4f));
            Assert.That(StandingEyeInAvatar(inputs, state), Is.EqualTo(1.604f).Within(1e-4f));
            BasisBodyFitResult fit = BasisBodyFitCore.Solve(Body(1.544f, 1.454f, 1.604f, 1.272f, state.UniformScale), 0.15f);
            float playerArm = (1.454f * state.UniformScale - 0.35f) * 0.5f, avatarArm = (1.272f - 0.35f) * 0.5f * fit.ArmScale;
            Assert.That(playerArm - avatarArm, Is.LessThan(0.02f), "the hands must reach within 2 cm once the fit and the window have both been used");
        }
        [Test]
        public void TorsoFitNeverTouchesTheNeckOrHead()
        {
            var fit = new BasisBodyFitResult { ArmScale = 1f, LegScale = 0.9f, TorsoScale = 1.12f, ArmStatus = BasisBodyFitStatus.Disabled, BodyStatus = BasisBodyFitStatus.Fitted };
            float[] scales = new float[BasisBodyFitApply.BoneCount];
            BasisBodyFitApply.CollectScales(in fit, scales);
            Assert.That(scales[12], Is.EqualTo(1.12f));
            Assert.That(scales[13], Is.EqualTo(1.12f));
            Assert.That(scales[14], Is.EqualTo(1.12f));
            Assert.That(scales[15], Is.EqualTo(1f), "neck");
            Assert.That(scales[16], Is.EqualTo(1f), "head");
        }
        [Test]
        public void TheFitOnlyEverResizesArms([Values(BasisSelectedHeightMode.EyeHeight, BasisSelectedHeightMode.ArmSpan, BasisSelectedHeightMode.BestFit)] BasisSelectedHeightMode mode, [Values(0.7f, 1f, 1.3f)] float avatarArmRatio)
        {
            var inputs = Inputs(mode, 1.62f, 1.45f, 1.55f, 1.45f * (1.55f / 1.62f) * avatarArmRatio);
            BasisScaleState state = BasisScaleResolver.Resolve(inputs);
            BasisBodyFitResult fit = BasisBodyFitCore.Solve(Body(1.62f, 1.45f, 1.55f, inputs.AvatarSpan, state.UniformScale), 0.15f);
            Assert.That(fit.HasBodyFit, Is.False, "standing, the hips and head stay where the avatar has them");
            Assert.That(fit.LegScale, Is.EqualTo(1f));
            Assert.That(fit.TorsoScale, Is.EqualTo(1f));
        }
        [Test]
        public void ShouldersHangingFromTheChestKeepTheUpperChestOutOfTheTorsoFit()
        {
            var root = new GameObject("TorsoTopRoot");
            try
            {
                Transform hips = new GameObject("Hips").transform, spine = new GameObject("Spine").transform, chest = new GameObject("Chest").transform, upper = new GameObject("UpperChest").transform, shoulder = new GameObject("LeftShoulder").transform;
                hips.SetParent(root.transform); spine.SetParent(hips); chest.SetParent(spine); upper.SetParent(chest); shoulder.SetParent(chest);
                var mapping = new BasisTransformMapping { Hips = hips, spine = spine, chest = chest, Upperchest = upper, leftShoulder = shoulder };
                Assert.That(BasisBodyFitApply.TorsoTop(mapping), Is.EqualTo(chest));
                Transform[] bones = new Transform[BasisBodyFitApply.BoneCount];
                BasisBodyFitApply.CollectBones(mapping, bones);
                Assert.That(bones[12], Is.EqualTo(spine));
                Assert.That(bones[13], Is.EqualTo(chest));
                Assert.That(bones[14], Is.Null);
                shoulder.SetParent(upper);
                Assert.That(BasisBodyFitApply.TorsoTop(mapping), Is.EqualTo(upper));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
        [Test]
        public void WristSpanHeuristicsAcceptAnOrdinaryReach()
        {
            Assert.That(BasisCalibrationMath.ArmSpanLooksUnderMeasured(1.64f, 1.45f), Is.False);
            Assert.That(BasisCalibrationMath.ArmSpanLooksUnderMeasured(1.64f, 1.38f), Is.False, "an average wrist-to-wrist span is not a bent-arm reading");
            Assert.That(BasisCalibrationMath.ArmSpanLooksUnderMeasured(1.64f, 1.05f), Is.True);
            Assert.That(BasisCalibrationMath.AutoHeightModePicksArmSpan(1.64f, 1.45f), Is.False);
        }
        [Test]
        public void GroundingOffsetIsSigned()
        {
            Assert.That(BasisCalibrationMath.HeightModeGroundingOffset(1.6f, 1f, 1.6f / 1.5f, 1.6f), Is.LessThan(0f));
            Assert.That(BasisCalibrationMath.HeightModeGroundingOffset(1.6f, 1f, 1.6f / 1.7f, 1.6f), Is.GreaterThan(0f));
            Assert.That(BasisCalibrationMath.HeightModeGroundingOffset(1.6f, 1f, 1f, 1.6f), Is.EqualTo(0f));
        }
    }
}
