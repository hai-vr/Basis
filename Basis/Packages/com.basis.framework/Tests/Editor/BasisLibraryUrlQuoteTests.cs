using Basis.BasisUI;
using NUnit.Framework;

namespace Basis.Tests.UI
{
    /// <summary>
    /// Covers the quote peeling on a pasted BEE file URL. Windows' "Copy as path" and most chat
    /// clients hand the link over already wrapped, and every downstream parse of it fails on the
    /// quotes, so they have to come off before the entry is graded — and before the <c>#password</c>
    /// fragment is split, or the trailing quote lands in the base64 decode instead.
    /// </summary>
    public class BasisLibraryUrlQuoteTests
    {
        [TestCase("\"https://example.com/thing.bee\"")]
        [TestCase("'https://example.com/thing.bee'")]
        [TestCase("`https://example.com/thing.bee`")]
        [TestCase("\u201Chttps://example.com/thing.bee\u201D")]
        [TestCase("\u2018https://example.com/thing.bee\u2019")]
        public void StripSurroundingQuotes_RemovesWrappingPair(string pasted)
        {
            Assert.That(InputValidation.StripSurroundingQuotes(pasted),
                Is.EqualTo("https://example.com/thing.bee"));
        }

        [TestCase("\"https://example.com/thing.bee")]
        [TestCase("https://example.com/thing.bee\"")]
        public void StripSurroundingQuotes_RemovesUnbalancedQuote(string pasted)
        {
            Assert.That(InputValidation.StripSurroundingQuotes(pasted),
                Is.EqualTo("https://example.com/thing.bee"),
                "half a wrapped paste is just as unusable as a matched pair.");
        }

        [Test]
        public void StripSurroundingQuotes_TrimsWhitespaceInsideAndOutside()
        {
            Assert.That(InputValidation.StripSurroundingQuotes("  \" https://example.com/thing.bee \"  "),
                Is.EqualTo("https://example.com/thing.bee"));
        }

        [Test]
        public void StripSurroundingQuotes_KeepsWindowsPathIntact()
        {
            Assert.That(InputValidation.StripSurroundingQuotes("\"C:\\Users\\me\\My World.bee\""),
                Is.EqualTo("C:\\Users\\me\\My World.bee"),
                "copy-as-path is the whole reason this exists; the spaces inside must survive.");
        }

        [Test]
        public void StripSurroundingQuotes_LeavesInteriorApostropheAlone()
        {
            const string url = "https://example.com/o'brien/thing.bee";
            Assert.That(InputValidation.StripSurroundingQuotes(url), Is.EqualTo(url),
                "an apostrophe is legal mid-path; only the ends are ever peeled.");
        }

        [Test]
        public void StripSurroundingQuotes_LeavesCleanUrlUntouched()
        {
            const string url = "https://example.com/thing.bee";
            Assert.That(InputValidation.StripSurroundingQuotes(url), Is.EqualTo(url));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void StripSurroundingQuotes_EmptyInputBecomesEmptyString(string pasted)
        {
            Assert.That(InputValidation.StripSurroundingQuotes(pasted), Is.Empty);
        }

        [Test]
        public void StripSurroundingQuotes_QuotesOnlyBecomesEmptyString()
        {
            Assert.That(InputValidation.StripSurroundingQuotes("\"\""), Is.Empty,
                "stripping must not walk past the other end and throw.");
        }

        [Test]
        public void SplitUrlFragmentPassword_UnwrapsQuotedShareStringBeforeSplitting()
        {
            // "url#base64(hunter2)" — the trailing quote sits after the fragment, so peeling has to
            // happen first or the password decode gets a stray character and silently gives up.
            InputValidation.SplitUrlFragmentPassword(
                "\"https://example.com/thing.bee#aHVudGVyMg==\"", string.Empty,
                out string url, out string password);

            Assert.That(url, Is.EqualTo("https://example.com/thing.bee"));
            Assert.That(password, Is.EqualTo("hunter2"));
        }

        [Test]
        public void SplitUrlFragmentPassword_UnwrapsQuotedUrlWithSeparatePassword()
        {
            InputValidation.SplitUrlFragmentPassword(
                "\"https://example.com/thing.bee\"", "hunter2",
                out string url, out string password);

            Assert.That(url, Is.EqualTo("https://example.com/thing.bee"));
            Assert.That(password, Is.EqualTo("hunter2"));
        }

        [Test]
        public void SplitUrlFragmentPassword_LeavesPasswordQuotesAlone()
        {
            InputValidation.SplitUrlFragmentPassword(
                "https://example.com/thing.bee", "\"quoted\"",
                out _, out string password);

            Assert.That(password, Is.EqualTo("\"quoted\""),
                "a password may legitimately start and end with a quote; only the URL is unwrapped.");
        }

        [TestCase('"')]
        [TestCase('\u201C')]
        [TestCase('\u201D')]
        public void RejectQuoteCharacter_DropsDoubleQuotes(char typed)
        {
            Assert.That(InputValidation.RejectQuoteCharacter(string.Empty, 0, typed), Is.EqualTo('\0'));
        }

        [TestCase('h')]
        [TestCase(':')]
        [TestCase('/')]
        [TestCase('\'')]
        [TestCase('#')]
        public void RejectQuoteCharacter_KeepsEverythingElse(char typed)
        {
            Assert.That(InputValidation.RejectQuoteCharacter(string.Empty, 0, typed), Is.EqualTo(typed),
                "the filter runs on every keystroke in the field, so it must only ever catch quotes.");
        }
    }
}
