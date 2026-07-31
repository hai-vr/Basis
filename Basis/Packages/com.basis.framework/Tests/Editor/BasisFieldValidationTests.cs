using System;
using Basis.BasisUI;
using NUnit.Framework;

namespace Basis.Tests.UI
{
    /// <summary>
    /// Covers the rules behind required panel fields: what counts as empty, and when a field is
    /// allowed to complain. The reveal rule is the part worth pinning — a field that holds its
    /// complaint back must still report the problem to whatever gates the submit, or the entry goes
    /// back to being silently dropped.
    /// </summary>
    public class BasisFieldValidationTests
    {
        const string Message = "Name is required.";

        [Test]
        public void Required_RejectsNull()
        {
            Assert.That(BasisFieldValidation.Required(Message)(null), Is.EqualTo(Message));
        }

        [Test]
        public void Required_RejectsEmpty()
        {
            Assert.That(BasisFieldValidation.Required(Message)(string.Empty), Is.EqualTo(Message));
        }

        [TestCase(" ")]
        [TestCase("   ")]
        [TestCase("\t")]
        [TestCase("\n")]
        [TestCase(" \t \n ")]
        public void Required_RejectsWhitespaceOnly(string text)
        {
            Assert.That(BasisFieldValidation.Required(Message)(text), Is.EqualTo(Message),
                "a field of spaces is empty to anyone reading it.");
        }

        [Test]
        public void Required_AcceptsRealText()
        {
            Assert.That(BasisFieldValidation.Required(Message)("Ada"), Is.Null);
        }

        [Test]
        public void Required_AcceptsTextWithSurroundingSpace()
        {
            Assert.That(BasisFieldValidation.Required(Message)("  Ada  "), Is.Null);
        }

        [Test]
        public void EagerField_ShowsTheProblemBeforeAnyInteraction()
        {
            Assert.That(BasisFieldValidation.ResolveShown(Message, gradeImmediately: true, revealed: false),
                Is.EqualTo(Message));
        }

        [Test]
        public void LazyField_StaysQuietUntilRevealed()
        {
            Assert.That(BasisFieldValidation.ResolveShown(Message, gradeImmediately: false, revealed: false),
                Is.Null,
                "a freshly opened page must not be covered in red over boxes nobody has touched.");
        }

        [Test]
        public void LazyField_ShowsTheProblemOnceRevealed()
        {
            Assert.That(BasisFieldValidation.ResolveShown(Message, gradeImmediately: false, revealed: true),
                Is.EqualTo(Message));
        }

        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void NoProblem_ShowsNothingRegardlessOfReveal(bool gradeImmediately, bool revealed)
        {
            Assert.That(BasisFieldValidation.ResolveShown(null, gradeImmediately, revealed), Is.Null);
            Assert.That(BasisFieldValidation.ResolveShown(string.Empty, gradeImmediately, revealed), Is.Null);
        }

        [Test]
        public void NormalizeProblem_TreatsEmptyAsNoProblem()
        {
            Assert.That(BasisFieldValidation.NormalizeProblem(null), Is.Null);
            Assert.That(BasisFieldValidation.NormalizeProblem(string.Empty), Is.Null);
            Assert.That(BasisFieldValidation.NormalizeProblem(Message), Is.EqualTo(Message));
        }

        [Test]
        public void HoldingTheComplaintBackDoesNotHideItFromASubmitGate()
        {
            Func<string, string> validator = BasisFieldValidation.Required(Message);

            string problem = BasisFieldValidation.NormalizeProblem(validator(string.Empty));
            string shown = BasisFieldValidation.ResolveShown(problem, gradeImmediately: false, revealed: false);

            Assert.That(shown, Is.Null, "nothing on screen yet");
            Assert.That(problem, Is.EqualTo(Message), "but the gate still sees the field is empty");
        }
    }
}
