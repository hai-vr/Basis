using NUnit.Framework;
using UnityEngine;
using UnityEngine.Animations.Rigging;

namespace Basis.Tests.IK
{
    /// <summary>
    /// The spine proportion-match math (BasisSpineProportionCore): a per-avatar torso ratio captured at
    /// calibration, clamped to a small cap and applied as a uniform spine scale. The two properties that
    /// keep it safe are pinned here: it is EXACTLY 1 (byte-identical no-op) when the avatar matches the
    /// wearer or the measurement is nonsensical, and it never scales beyond the cap.
    /// </summary>
    public class BasisSpineProportionCoreTests
    {
        const float Eps = 1e-6f;

        // ---- ComputeRatio: wearer torso / avatar torso, both straight head-to-hips in the same space ----

        [Test]
        public void Ratio_MatchingTorsos_IsExactlyOne()
        {
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeRatio(0.55f, 0.55f), Eps);
        }

        [Test]
        public void Ratio_AvatarTorsoLongerThanWearer_IsBelowOne()
        {
            // Wearer 0.50 m, avatar 0.60 m -> the avatar spine must shrink toward 0.833.
            float r = BasisSpineProportionCore.ComputeRatio(0.50f, 0.60f);
            Assert.AreEqual(0.5f / 0.6f, r, Eps);
            Assert.Less(r, 1f);
        }

        [Test]
        public void Ratio_AvatarTorsoShorterThanWearer_IsAboveOne()
        {
            // Wearer 0.60 m, avatar 0.50 m -> the avatar spine must stretch toward 1.2.
            float r = BasisSpineProportionCore.ComputeRatio(0.60f, 0.50f);
            Assert.AreEqual(1.2f, r, Eps);
            Assert.Greater(r, 1f);
        }

        [Test]
        public void Ratio_ZeroOrNegativeOrTinyLengths_AreInert()
        {
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeRatio(0f, 0.55f), Eps);
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeRatio(0.55f, 0f), Eps);
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeRatio(-0.5f, 0.55f), Eps);
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeRatio(0.55f, 0.001f), Eps);
        }

        [Test]
        public void Ratio_OutsideSanityBand_IsRejectedAsAScaleMismatch()
        {
            // A 3x or 0.3x reading is a space/scale bug, not a real body -- reject it (return 1) rather than
            // let a bad frame drive a huge correction. The cap would bound it, but rejecting is cleaner.
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeRatio(0.60f, 0.20f), Eps);
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeRatio(0.20f, 0.60f), Eps);
        }

        [Test]
        public void Ratio_NaN_IsInert()
        {
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeRatio(float.NaN, 0.55f), Eps);
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeRatio(0.55f, float.NaN), Eps);
        }

        // ---- ComputeScale: the ratio clamped to +/- maxScale around 1 ----

        [Test]
        public void Scale_MatchingProportions_IsBitIdenticalNoOp()
        {
            // The whole "same usability when it does not apply" guarantee: ratio 1 must return LITERALLY 1f.
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeScale(1f, 0.12f));
        }

        [Test]
        public void Scale_WithinCap_PassesTheRatioThrough()
        {
            Assert.AreEqual(1.05f, BasisSpineProportionCore.ComputeScale(1.05f, 0.12f), Eps);
            Assert.AreEqual(0.95f, BasisSpineProportionCore.ComputeScale(0.95f, 0.12f), Eps);
        }

        [Test]
        public void Scale_BeyondCap_ClampsSymmetrically()
        {
            // Stretch and shrink both cap at the same distance from 1.
            Assert.AreEqual(1.12f, BasisSpineProportionCore.ComputeScale(1.20f, 0.12f), Eps);
            Assert.AreEqual(0.88f, BasisSpineProportionCore.ComputeScale(0.80f, 0.12f), Eps);
        }

        [Test]
        public void Scale_ZeroCap_IsAlwaysNoOp()
        {
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeScale(1.20f, 0f), Eps);
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeScale(0.80f, 0f), Eps);
        }

        [Test]
        public void Scale_InvalidRatio_IsNoOp()
        {
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeScale(0f, 0.12f), Eps);
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeScale(-1f, 0.12f), Eps);
            Assert.AreEqual(1f, BasisSpineProportionCore.ComputeScale(float.NaN, 0.12f), Eps);
        }

        [Test]
        public void Scale_StaysWithinCap_AcrossTheWholeRatioRange()
        {
            const float cap = 0.12f;
            for (int i = 0; i <= 40; i++)
            {
                float ratio = 0.5f + i * (1.5f / 40f); // 0.5 .. 2.0
                float s = BasisSpineProportionCore.ComputeScale(ratio, cap);
                Assert.GreaterOrEqual(s, 1f - cap - Eps, $"ratio {ratio} under-shot the cap");
                Assert.LessOrEqual(s, 1f + cap + Eps, $"ratio {ratio} over-shot the cap");
            }
        }

        [Test]
        public void EndToEnd_ScaledAvatarTorso_LandsAtOrTowardTheWearerTorso()
        {
            // A mild mismatch inside the cap is corrected exactly: scale the avatar torso by the result and
            // it equals the wearer's. (0.55 wearer / 0.60 avatar = 0.9167 ratio, |1-0.9167| < 0.12 cap.)
            float userTorso = 0.55f, avatarTorso = 0.60f, cap = 0.12f;
            float ratio = BasisSpineProportionCore.ComputeRatio(userTorso, avatarTorso);
            float scale = BasisSpineProportionCore.ComputeScale(ratio, cap);
            Assert.AreEqual(userTorso, avatarTorso * scale, Eps);

            // A mismatch larger than the cap is corrected only PART way (capped), never past the wearer.
            float bigAvatar = 0.75f; // ratio 0.733, exceeds the 12% cap
            float bigScale = BasisSpineProportionCore.ComputeScale(
                BasisSpineProportionCore.ComputeRatio(userTorso, bigAvatar), cap);
            Assert.AreEqual(1f - cap, bigScale, Eps);
            Assert.Greater(bigAvatar * bigScale, userTorso, "capped correction must not overshoot the wearer");
        }
    }
}
