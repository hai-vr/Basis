using Basis.Scripts.BasisSdk.Interactions;
using NUnit.Framework;
using UnityEngine;

namespace Basis.Tests.Interactions
{
    /// <summary>
    /// Pins which interactable a hand takes when the pointer ray and the proximity bubble disagree.
    /// The reported failure is the first case: a prop straight ahead could not be grabbed while
    /// another prop sat off to the side, because the bubble overwrote the ray unconditionally.
    ///
    /// Geometry is sized like a real reach — the hand at chest height, props between 0.1 m and 3 m,
    /// a 0.5 m bubble and a 0.15 m grab radius.
    /// </summary>
    public class BasisInteractTargetPickerTests
    {
        private const float Reach = 0.5f;
        private const float Grab = 0.15f;
        private const float Tolerance = 1e-4f;

        private static readonly Vector3 Hand = new Vector3(0f, 1.2f, 0f);
        private static readonly Vector3 Aim = Vector3.forward;

        private static BasisInteractReach Classify(Vector3 point, out float score, float scale = 1f)
        {
            return BasisInteractTargetPicker.Classify(point, Hand, Aim, Hand, Grab, Reach, scale, out score);
        }

        private static bool Wins(Vector3 challenger, Vector3 holder, float margin = 0f)
        {
            BasisInteractReach challengerReach = Classify(challenger, out float challengerScore);
            BasisInteractReach holderReach = Classify(holder, out float holderScore);
            return BasisInteractTargetPicker.Beats(challengerReach, challengerScore, holderReach, holderScore, margin);
        }

        // ── the reported bug ────────────────────────────────────────────────

        [Test]
        public void PointedAt_BeatsSomethingOffToTheSide_EvenWhenTheSideOneIsNearer()
        {
            Vector3 ahead = Hand + Vector3.forward * 0.6f;
            Vector3 beside = Hand + Vector3.right * 0.3f;

            Assert.AreEqual(BasisInteractReach.Aimed, Classify(ahead, out _));
            Assert.AreEqual(BasisInteractReach.Nearby, Classify(beside, out _));
            Assert.IsTrue(Wins(ahead, beside));
            Assert.IsFalse(Wins(beside, ahead));
        }

        [Test]
        public void PointedAt_BeatsSomethingBehindTheHand()
        {
            Vector3 ahead = Hand + Vector3.forward * 0.6f;
            Vector3 behind = Hand + Vector3.back * 0.2f;

            Assert.AreEqual(BasisInteractReach.Nearby, Classify(behind, out _));
            Assert.IsTrue(Wins(ahead, behind));
        }

        [Test]
        public void OffAimButWithinReach_IsStillACandidate()
        {
            // Loose bubble grabbing is preserved: with nothing aimed at, a prop beside the hand is
            // still takeable, it just cannot outrank one being pointed at.
            Assert.AreEqual(BasisInteractReach.Nearby, Classify(Hand + Vector3.right * 0.4f, out _));
            Assert.AreEqual(BasisInteractReach.None, Classify(Hand + Vector3.right * 0.9f, out _));
        }

        // ── the hand wins over aim ──────────────────────────────────────────

        [Test]
        public void SomethingInTheHand_BeatsSomethingPointedAt()
        {
            Vector3 inHand = Hand + Vector3.right * 0.1f;
            Vector3 ahead = Hand + Vector3.forward * 0.6f;

            Assert.AreEqual(BasisInteractReach.InHand, Classify(inHand, out _));
            Assert.IsTrue(Wins(inHand, ahead));
            Assert.IsFalse(Wins(ahead, inHand));
        }

        [Test]
        public void SomethingInTheHand_QualifiesFromAnyDirection()
        {
            Assert.AreEqual(BasisInteractReach.InHand, Classify(Hand + Vector3.back * 0.1f, out _));
            Assert.AreEqual(BasisInteractReach.InHand, Classify(Hand + Vector3.down * 0.1f, out _));
        }

        [Test]
        public void WithinTheHand_TheNearerOneWins()
        {
            Assert.IsTrue(Wins(Hand + Vector3.right * 0.05f, Hand + Vector3.right * 0.12f));
        }

        // ── the aim cone ────────────────────────────────────────────────────

        [Test]
        public void TheAimCone_WidensWithDistance()
        {
            float near = BasisInteractTargetPicker.AimConeRadius(0.5f, 1f);
            float far = BasisInteractTargetPicker.AimConeRadius(3f, 1f);

            Assert.Greater(far, near);
            Assert.AreEqual(0.5f * Mathf.Tan(BasisInteractTargetPicker.AimConeHalfAngleDegrees * Mathf.Deg2Rad), near, Tolerance);
        }

        [Test]
        public void TheAimCone_NeverCollapsesAtTheHand()
        {
            Assert.AreEqual(BasisInteractTargetPicker.AimConeMinRadius, BasisInteractTargetPicker.AimConeRadius(0f, 1f), Tolerance);
        }

        [Test]
        public void AProp_JustOutsideTheCone_DropsToNearby()
        {
            float depth = 0.4f;
            float radius = BasisInteractTargetPicker.AimConeRadius(depth, 1f);

            Assert.AreEqual(BasisInteractReach.Aimed, Classify(Hand + Vector3.forward * depth + Vector3.right * (radius - 0.01f), out _));
            Assert.AreEqual(BasisInteractReach.Nearby, Classify(Hand + Vector3.forward * depth + Vector3.right * (radius + 0.01f), out _));
        }

        [Test]
        public void AmongAimedProps_TheNearerAlongTheRayWins()
        {
            Assert.IsTrue(Wins(Hand + Vector3.forward * 0.8f, Hand + Vector3.forward * 2.5f));
        }

        [Test]
        public void AmongAimedPropsAtTheSameDepth_TheMoreCentredWins()
        {
            Vector3 centred = Hand + Vector3.forward * 1f;
            Vector3 edge = Hand + Vector3.forward * 1f + Vector3.right * 0.2f;

            Classify(centred, out float centredScore);
            Classify(edge, out float edgeScore);

            Assert.AreEqual(1f, centredScore, Tolerance);
            Assert.AreEqual(1f + 0.2f * BasisInteractTargetPicker.OffAxisWeight, edgeScore, Tolerance);
            Assert.IsTrue(Wins(centred, edge));
        }

        [Test]
        public void ADistantAimedProp_StillBeatsANearOffAimOne()
        {
            // Bands decide before scores do: three metres down the ray beats twenty centimetres aside.
            Assert.IsTrue(Wins(Hand + Vector3.forward * 3f, Hand + Vector3.right * 0.2f));
        }

        // ── holding on to a target ──────────────────────────────────────────

        [Test]
        public void AMarginallyBetterChallenger_DoesNotStealTheTarget()
        {
            Vector3 held = Hand + Vector3.forward * 1f;
            Vector3 challenger = Hand + Vector3.forward * 0.98f;

            Assert.IsFalse(Wins(challenger, held, BasisInteractTargetPicker.SwitchMargin));
        }

        [Test]
        public void AClearlyBetterChallenger_TakesTheTarget()
        {
            Vector3 held = Hand + Vector3.forward * 1f;
            Vector3 challenger = Hand + Vector3.forward * 0.5f;

            Assert.IsTrue(Wins(challenger, held, BasisInteractTargetPicker.SwitchMargin));
        }

        [Test]
        public void ABetterBand_IgnoresTheMargin()
        {
            Assert.IsTrue(BasisInteractTargetPicker.Beats(
                BasisInteractReach.Aimed, 3f,
                BasisInteractReach.Nearby, 0.05f,
                BasisInteractTargetPicker.SwitchMargin));
        }

        [Test]
        public void TheStickyScale_KeepsATargetAimedJustOutsideTheFreshCone()
        {
            float depth = 0.4f;
            float radius = BasisInteractTargetPicker.AimConeRadius(depth, 1f);
            Vector3 drifted = Hand + Vector3.forward * depth + Vector3.right * (radius * 1.2f);

            Assert.AreEqual(BasisInteractReach.Nearby, Classify(drifted, out _));
            Assert.AreEqual(BasisInteractReach.Aimed, Classify(drifted, out _, BasisInteractTargetPicker.StickyScale));
        }

        [Test]
        public void TheStickyScale_AlsoWidensTheHandAndReachBands()
        {
            Vector3 justOutOfHand = Hand + Vector3.back * (Grab * 1.2f);
            Vector3 justOutOfReach = Hand + Vector3.right * (Reach * 1.2f);

            Assert.AreEqual(BasisInteractReach.Nearby, Classify(justOutOfHand, out _));
            Assert.AreEqual(BasisInteractReach.InHand, Classify(justOutOfHand, out _, BasisInteractTargetPicker.StickyScale));

            Assert.AreEqual(BasisInteractReach.None, Classify(justOutOfReach, out _));
            Assert.AreEqual(BasisInteractReach.Nearby, Classify(justOutOfReach, out _, BasisInteractTargetPicker.StickyScale));
        }

        // ── degenerate input ────────────────────────────────────────────────

        [Test]
        public void AZeroLengthAim_FallsBackToForwardInsteadOfNaN()
        {
            BasisInteractReach reach = BasisInteractTargetPicker.Classify(
                Hand + Vector3.forward * 0.6f, Hand, Vector3.zero, Hand, Grab, Reach, 1f, out float score);

            Assert.AreEqual(BasisInteractReach.Aimed, reach);
            Assert.AreEqual(0.6f, score, Tolerance);
        }

        [Test]
        public void NothingReachable_ScoresWorstAndLosesToEverything()
        {
            Assert.AreEqual(BasisInteractReach.None, Classify(Hand + Vector3.right * 5f, out float score));
            Assert.AreEqual(float.MaxValue, score);
            Assert.IsFalse(Wins(Hand + Vector3.right * 5f, Hand + Vector3.right * 0.4f));
            Assert.IsTrue(Wins(Hand + Vector3.right * 0.4f, Hand + Vector3.right * 5f));
        }
    }
}
