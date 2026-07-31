using Basis.BasisUI;
using NUnit.Framework;

namespace Basis.Tests.UI
{
    /// <summary>
    /// Covers the shared severity grading behind the tinted status cards. The point of the helper is
    /// the release margin: live stats sit on a threshold and jitter across it, and a card that
    /// retints on every crossing replays its fade several times a second.
    /// </summary>
    public class BasisPanelTintGradeTests
    {
        const double Caution = 100.0;
        const double Hot = 200.0;

        static BasisPanelSeverity Fresh(double value) =>
            BasisPanelTint.Grade(value, Caution, Hot, BasisPanelSeverity.None);

        [Test]
        public void WellUnderThreshold_IsCalm()
        {
            Assert.That(Fresh(10.0), Is.EqualTo(BasisPanelSeverity.Calm));
        }

        [Test]
        public void AtCaution_Warns()
        {
            Assert.That(Fresh(Caution), Is.EqualTo(BasisPanelSeverity.Caution));
        }

        [Test]
        public void AtHot_ReadsHot()
        {
            Assert.That(Fresh(Hot), Is.EqualTo(BasisPanelSeverity.Hot));
        }

        [Test]
        public void FarAboveHot_ReadsHot()
        {
            Assert.That(Fresh(Hot * 10.0), Is.EqualTo(BasisPanelSeverity.Hot));
        }

        [Test]
        public void JustUnderCautionFromCalm_StaysCalm()
        {
            Assert.That(BasisPanelTint.Grade(Caution - 0.01, Caution, Hot, BasisPanelSeverity.Calm),
                Is.EqualTo(BasisPanelSeverity.Calm));
        }

        [Test]
        public void JustUnderCautionFromCaution_HoldsCaution()
        {
            Assert.That(BasisPanelTint.Grade(Caution - 0.01, Caution, Hot, BasisPanelSeverity.Caution),
                Is.EqualTo(BasisPanelSeverity.Caution),
                "a stat wobbling one unit under the line must not flicker back to calm.");
        }

        [Test]
        public void JustUnderHotFromHot_HoldsHot()
        {
            Assert.That(BasisPanelTint.Grade(Hot - 0.01, Caution, Hot, BasisPanelSeverity.Hot),
                Is.EqualTo(BasisPanelSeverity.Hot));
        }

        [Test]
        public void ClearingTheHotReleaseMargin_RelaxesToCaution()
        {
            double released = Hot * BasisPanelTint.GradeReleaseFraction - 0.01;
            Assert.That(BasisPanelTint.Grade(released, Caution, Hot, BasisPanelSeverity.Hot),
                Is.EqualTo(BasisPanelSeverity.Caution),
                "the hold is a margin, not a latch — a real recovery still relaxes the grade.");
        }

        [Test]
        public void ClearingTheCautionReleaseMargin_RelaxesToCalm()
        {
            double released = Caution * BasisPanelTint.GradeReleaseFraction - 0.01;
            Assert.That(BasisPanelTint.Grade(released, Caution, Hot, BasisPanelSeverity.Caution),
                Is.EqualTo(BasisPanelSeverity.Calm));
        }

        [Test]
        public void RisingPastCautionFromCalm_DoesNotSkipToHot()
        {
            Assert.That(BasisPanelTint.Grade(Caution + 1.0, Caution, Hot, BasisPanelSeverity.Calm),
                Is.EqualTo(BasisPanelSeverity.Caution));
        }

        [Test]
        public void PreviousGradeNeverRaisesSeverity()
        {
            Assert.That(BasisPanelTint.Grade(10.0, Caution, Hot, BasisPanelSeverity.Hot),
                Is.EqualTo(BasisPanelSeverity.Calm),
                "a stat that has genuinely recovered must not stay red because it once was.");
        }

        [Test]
        public void AccentForNone_IsNotATint()
        {
            Assert.That(BasisPanelTint.AccentFor(BasisPanelSeverity.Caution),
                Is.EqualTo(BasisPanelTint.Caution));
            Assert.That(BasisPanelTint.AccentFor(BasisPanelSeverity.Hot),
                Is.EqualTo(BasisPanelTint.Hot));
            Assert.That(BasisPanelTint.AccentFor(BasisPanelSeverity.Calm),
                Is.EqualTo(BasisPanelTint.Calm));
        }
    }
}
