using System.Text;
using Basis.Network.Core;
using BasisNetworkServer.BasisNetworking;
using Xunit;

namespace BasisServerTests;

/// <summary>
/// Chat/display-name sanitization and word-filter behavior.
/// Chat path (BasisNetworkChat.HandleChatMessage): BasisWordFilter.Filter -> BasisChatSanitizer.Sanitize.
/// Connect path (BasisServerHandleEvents): BasisDisplayNameSanitizer.Sanitize, empty result rejects the peer.
/// Kept as a single class on purpose: BasisNetworkChat holds static word-list state and xunit
/// parallelizes across classes; nothing here mutates that state (LoadWordFilter is never called).
/// All non-ASCII test data is written as \u escapes so the source stays encoding-proof.
/// </summary>
public class SanitizerAndWordFilterTests
{
    // ---------------- BasisChatSanitizer (transport limits only) ----------------

    [Theory]
    [InlineData("hello world")]
    [InlineData("The quick brown fox jumps over the lazy dog.")]
    [InlineData("punctuation !?~ 123 :)")]
    [InlineData("x")]
    public void ChatSanitizer_CleanText_PassesUnchanged(string message)
    {
        Assert.Equal(message, BasisChatSanitizer.Sanitize(message));
    }

    [Fact]
    public void ChatSanitizer_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, BasisChatSanitizer.Sanitize(null!));
        Assert.Equal(string.Empty, BasisChatSanitizer.Sanitize(string.Empty));
    }

    // The chat sanitizer enforces length only; control/zero-width/RTL characters are
    // intentionally left alone (the word filter and clients handle content concerns).
    [Theory]
    [InlineData("a\nb")]
    [InlineData("tab\tseparated")]
    [InlineData("zero200Bwidth")]
    [InlineData("rtl202Eoverride")]
    [InlineData("bell0007char")]
    public void ChatSanitizer_ControlAndInvisibleCharacters_PassThrough(string message)
    {
        Assert.Equal(message, BasisChatSanitizer.Sanitize(message));
    }

    [Theory]
    [InlineData("4F60597D4E16754C")] // Chinese
    [InlineData("30533093306B3061306F")] // Japanese
    [InlineData("D83DDC4DD83CDF89")] // thumbs up + party popper emoji
    [InlineData("caf00E9 mixed 597D")]
    public void ChatSanitizer_LegitimateUnicode_Preserved(string message)
    {
        Assert.Equal(message, BasisChatSanitizer.Sanitize(message));
    }

    [Fact]
    public void ChatSanitizer_AtCharacterCap_Unchanged()
    {
        string message = new string('a', BasisChatSanitizer.MaxMessageCharacters);
        Assert.Equal(message, BasisChatSanitizer.Sanitize(message));
    }

    [Theory]
    [InlineData(257)]
    [InlineData(300)]
    [InlineData(10000)]
    public void ChatSanitizer_OverCharacterCap_TruncatedToCap(int length)
    {
        string result = BasisChatSanitizer.Sanitize(new string('a', length));
        Assert.Equal(new string('a', BasisChatSanitizer.MaxMessageCharacters), result);
    }

    [Fact]
    public void ChatSanitizer_TruncationDoesNotSplitSurrogatePair()
    {
        // 255 ASCII + one astral emoji = 257 UTF-16 units; cutting at 256 would land
        // between the surrogates, so the whole pair must be dropped instead.
        string result = BasisChatSanitizer.Sanitize(new string('a', 255) + "D83DDC4D");
        Assert.Equal(new string('a', 255), result);
    }

    [Fact]
    public void ChatSanitizer_EmojiMessage_ClampsToWholeEmojiAtExactByteCap()
    {
        string input = string.Concat(Enumerable.Repeat("D83DDC4D", 130));
        string result = BasisChatSanitizer.Sanitize(input);
        // 256 UTF-16 units = 128 emoji = exactly 512 UTF-8 bytes, which is allowed.
        Assert.Equal(string.Concat(Enumerable.Repeat("D83DDC4D", 128)), result);
        Assert.Equal(BasisChatSanitizer.MaxMessageBytes, Encoding.UTF8.GetByteCount(result));
    }

    [Fact]
    public void ChatSanitizer_CjkOverByteCap_TrimsWholeCharacters()
    {
        // 256 chars * 3 bytes = 768 bytes; trims one scalar at a time down to 170 chars (510 bytes).
        string result = BasisChatSanitizer.Sanitize(new string('597D', 256));
        Assert.Equal(new string('597D', 170), result);
        Assert.True(Encoding.UTF8.GetByteCount(result) <= BasisChatSanitizer.MaxMessageBytes);
    }

    [Fact]
    public void ChatSanitizer_ByteTrim_RemovesWholeEmojiScalars()
    {
        // 250 CJK (750 bytes) + 3 emoji (12 bytes) = 256 units / 762 bytes: the three emoji
        // must come off as whole pairs, then CJK singles until under the byte cap.
        string input = new string('597D', 250) + "D83DDC4DD83DDC4DD83DDC4D";
        string result = BasisChatSanitizer.Sanitize(input);
        Assert.Equal(new string('597D', 170), result);
    }

    [Fact]
    public void ChatSanitizer_Idempotent()
    {
        string[] inputs =
        {
            "hello world",
            new string('a', 300),
            new string('597D', 256),
            string.Concat(Enumerable.Repeat("D83DDC4D", 130)),
            new string('597D', 250) + "D83DDC4DD83DDC4DD83DDC4D",
        };
        foreach (string input in inputs)
        {
            string once = BasisChatSanitizer.Sanitize(input);
            Assert.Equal(once, BasisChatSanitizer.Sanitize(once));
        }
    }

    [Fact]
    public void ChatSanitizer_Constants_MatchChatWireContract()
    {
        Assert.Equal(256, BasisChatSanitizer.MaxMessageCharacters);
        Assert.Equal(512, BasisChatSanitizer.MaxMessageBytes);
        Assert.Equal(SerializableBasis.ChatMessage.MaxPayloadBytes, BasisChatSanitizer.MaxMessageBytes);
    }

    // ---------------- BasisDisplayNameSanitizer ----------------

    [Theory]
    [InlineData("PlayerOne")]
    [InlineData("Bob_42")]
    [InlineData("73A95BB64E00")] // CJK name
    [InlineData("AliceD83CDFAE")] // name with game-controller emoji
    [InlineData("a")]
    public void DisplayName_CleanNames_Unchanged(string name)
    {
        Assert.Equal(name, BasisDisplayNameSanitizer.Sanitize(name));
        Assert.True(BasisDisplayNameSanitizer.IsValid(name));
    }

    [Fact]
    public void DisplayName_NullOrEmpty_ReturnsEmptyAndInvalid()
    {
        Assert.Equal(string.Empty, BasisDisplayNameSanitizer.Sanitize(null!));
        Assert.Equal(string.Empty, BasisDisplayNameSanitizer.Sanitize(string.Empty));
        Assert.False(BasisDisplayNameSanitizer.IsValid(null!));
        Assert.False(BasisDisplayNameSanitizer.IsValid(string.Empty));
    }

    [Theory]
    [InlineData('0000')] // NUL
    [InlineData('0007')] // BEL
    [InlineData('001B')] // ESC
    [InlineData('007F')] // DEL
    [InlineData('009D')] // C1 control
    public void DisplayName_ControlCharacters_Removed(char control)
    {
        Assert.Equal("Player", BasisDisplayNameSanitizer.Sanitize("Pla" + control + "yer"));
    }

    [Fact]
    public void DisplayName_TabsAndNewlines_RemovedAsControls_NotFoldedToSpace()
    {
        // Control check runs before the whitespace fold, so \t and \n vanish entirely.
        Assert.Equal("ab", BasisDisplayNameSanitizer.Sanitize("a\tb"));
        Assert.Equal("ab", BasisDisplayNameSanitizer.Sanitize("a\nb"));
        Assert.Equal("ab", BasisDisplayNameSanitizer.Sanitize("a\r\nb"));
    }

    [Theory]
    [InlineData('200B')] // zero width space
    [InlineData('200C')] // zero width non-joiner
    [InlineData('200D')] // zero width joiner
    [InlineData('200E')] // left-to-right mark
    [InlineData('202A')] // left-to-right embedding
    [InlineData('202E')] // right-to-left override
    [InlineData('2066')] // left-to-right isolate
    [InlineData('FEFF')] // zero width no-break space / BOM
    [InlineData('00AD')] // soft hyphen
    public void DisplayName_FormatCharacters_Removed(char format)
    {
        Assert.Equal("Player", BasisDisplayNameSanitizer.Sanitize("Pla" + format + "yer"));
    }

    [Theory]
    [InlineData('115F')] // Hangul choseong filler
    [InlineData('1160')] // Hangul jungseong filler
    [InlineData('3164')] // Hangul filler
    [InlineData('FFA0')] // halfwidth Hangul filler
    [InlineData('2800')] // Braille pattern blank
    [InlineData('180E')] // Mongolian vowel separator
    public void DisplayName_KnownInvisibleGlyphs_Removed(char glyph)
    {
        Assert.Equal("Player", BasisDisplayNameSanitizer.Sanitize("Pla" + glyph + "yer"));
    }

    // BasisServerHandleEvents rejects the connection when the sanitized name is empty.
    [Theory]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    [InlineData("200B200B")]
    [InlineData("31643164")]
    [InlineData("280028002800")]
    [InlineData("00A000A0")]
    [InlineData("200B FEFF")]
    public void DisplayName_BlankAfterSanitize_IsEmptyAndInvalid(string name)
    {
        Assert.Equal(string.Empty, BasisDisplayNameSanitizer.Sanitize(name));
        Assert.False(BasisDisplayNameSanitizer.IsValid(name));
    }

    [Theory]
    [InlineData("A00A0B")] // no-break space
    [InlineData("A3000B")] // ideographic space
    [InlineData("A2028B")] // line separator
    public void DisplayName_UnicodeWhitespace_FoldedToPlainSpace(string name)
    {
        Assert.Equal("A B", BasisDisplayNameSanitizer.Sanitize(name));
    }

    [Theory]
    [InlineData("  Alice  ", "Alice")]
    [InlineData("00A0Bob3000", "Bob")]
    [InlineData("30003000Cara", "Cara")]
    public void DisplayName_OuterWhitespace_Trimmed(string name, string expected)
    {
        Assert.Equal(expected, BasisDisplayNameSanitizer.Sanitize(name));
    }

    [Fact]
    public void DisplayName_InteriorWhitespaceRuns_FoldedButNotCollapsed()
    {
        Assert.Equal("A   B", BasisDisplayNameSanitizer.Sanitize("A 00A0 B"));
    }

    [Fact]
    public void DisplayName_RtlOverride_StrippedKeepingVisibleText()
    {
        Assert.Equal("abcdef", BasisDisplayNameSanitizer.Sanitize("abc202Edef"));
    }

    [Fact]
    public void DisplayName_ZwjEmojiSequence_LosesJoiner()
    {
        // Format stripping applies inside emoji ZWJ sequences too; the parts remain.
        Assert.Equal("D83DDC68D83DDC69",
            BasisDisplayNameSanitizer.Sanitize("D83DDC68200DD83DDC69"));
    }

    [Fact]
    public void DisplayName_Idempotent()
    {
        string[] inputs =
        {
            "  Alice  ",
            "Pla200Byer ",
            "A 00A0 B",
            "abc202Edef",
            "73A95BB64E00",
            "31643164",
        };
        foreach (string input in inputs)
        {
            string once = BasisDisplayNameSanitizer.Sanitize(input);
            Assert.Equal(once, BasisDisplayNameSanitizer.Sanitize(once));
        }
    }

    // ---------------- BasisWordFilter ----------------
    // Blacklist words below ("damn", "crap", "ass", "go die") are entries the server's
    // default chat_word_filter.txt actually ships (BasisNetworkChat.LoadWordFilter).

    [Fact]
    public void WordFilter_ExactWord_Detected()
    {
        Assert.True(BasisWordFilter.ContainsBannedWord("damn", new[] { "damn" }, out string matched));
        Assert.Equal("damn", matched);
    }

    [Fact]
    public void WordFilter_ExactWord_MaskedWithAsterisks()
    {
        Assert.Equal("****", BasisWordFilter.Filter("damn", new[] { "damn" }));
        Assert.Equal("***", BasisWordFilter.Filter("ass", new[] { "ass" }));
    }

    [Theory]
    [InlineData("DAMN")]
    [InlineData("DaMn")]
    [InlineData("dAmN")]
    public void WordFilter_TextCase_Ignored(string text)
    {
        Assert.True(BasisWordFilter.ContainsBannedWord(text, new[] { "damn" }, out _));
        Assert.Equal("****", BasisWordFilter.Filter(text, new[] { "damn" }));
    }

    [Fact]
    public void WordFilter_WordInsideSentence_MaskedInPlace()
    {
        Assert.Equal("you **** fool", BasisWordFilter.Filter("you damn fool", new[] { "damn" }));
    }

    [Fact]
    public void WordFilter_WordAtStartAndEndOfMessage_Masked()
    {
        Assert.Equal("**** that hurt", BasisWordFilter.Filter("damn that hurt", new[] { "damn" }));
        Assert.Equal("that was ****", BasisWordFilter.Filter("that was damn", new[] { "damn" }));
    }

    [Theory]
    [InlineData("damn!", "damn", "****!")]
    [InlineData("(damn)", "damn", "(****)")]
    [InlineData("my ass.", "ass", "my ***.")]
    [InlineData("my ass hurts", "ass", "my *** hurts")]
    public void WordFilter_PunctuationAndSpaceAdjacent_Masked(string text, string word, string expected)
    {
        Assert.True(BasisWordFilter.ContainsBannedWord(text, new[] { word }, out _));
        Assert.Equal(expected, BasisWordFilter.Filter(text, new[] { word }));
    }

    [Theory]
    [InlineData("hello there friend", "damn")]
    [InlineData("The quick brown fox jumps over the lazy dog.", "damn")]
    [InlineData("a simple sentence", "ass")] // scattered a..s..s rescued by trigram backtracking
    [InlineData("hello there friend", "crap")]
    public void WordFilter_CleanSentences_Pass(string text, string word)
    {
        Assert.False(BasisWordFilter.ContainsBannedWord(text, new[] { word }, out string matched));
        Assert.Equal(string.Empty, matched);
        Assert.Equal(text, BasisWordFilter.Filter(text, new[] { word }));
    }

    // Substring semantics as implemented: occurrences embedded in longer legitimate words
    // are ignored via trigram context ("ssi" in assignment, "las" in class, "ppy" in crappy,
    // "nat" in damnation) or the match-boundary check ("ssa" in assassinate).
    [Theory]
    [InlineData("assignment", "ass")]
    [InlineData("class", "ass")]
    [InlineData("bass", "ass")]
    [InlineData("assassinate", "ass")]
    [InlineData("scrape", "crap")]
    [InlineData("crappy", "crap")]
    [InlineData("damnation", "damn")]
    public void WordFilter_EmbeddedInLongerWord_NotFlagged(string text, string word)
    {
        Assert.False(BasisWordFilter.ContainsBannedWord(text, new[] { word }, out _));
        Assert.Equal(text, BasisWordFilter.Filter(text, new[] { word }));
    }

    [Fact]
    public void WordFilter_SpacedOutLetters_Detected()
    {
        Assert.True(BasisWordFilter.ContainsBannedWord("d a m n", new[] { "damn" }, out _));
        Assert.Equal("* * * *", BasisWordFilter.Filter("d a m n", new[] { "damn" }));
    }

    [Fact]
    public void WordFilter_PunctuatedInsertion_Detected()
    {
        Assert.Equal("*.*.*.*", BasisWordFilter.Filter("d.a.m.n", new[] { "damn" }));
    }

    [Fact]
    public void WordFilter_ZeroWidthSpaceInsertion_Detected()
    {
        // U+200B is its own text element, so it is skipped like any inserted character;
        // only the matched letters are starred and the ZWSP survives in the output.
        Assert.True(BasisWordFilter.ContainsBannedWord("da200Bmn", new[] { "damn" }, out _));
        Assert.Equal("**200B**", BasisWordFilter.Filter("da200Bmn", new[] { "damn" }));
    }

    [Theory]
    [InlineData("d@mn", "damn")]
    [InlineData("d4mn", "damn")]
    [InlineData("d03B1mn", "damn")] // Greek small alpha
    [InlineData("FF44FF41FF4DFF4E", "damn")] // fullwidth letters
    [InlineData("cr4p", "crap")]
    public void WordFilter_HomoglyphAndLeetSubstitution_Detected(string text, string word)
    {
        Assert.True(BasisWordFilter.ContainsBannedWord(text, new[] { word }, out string matched));
        Assert.Equal(word, matched);
        Assert.Equal("****", BasisWordFilter.Filter(text, new[] { word }));
    }

    [Fact]
    public void WordFilter_LatinDiacritics_NotFolded()
    {
        // U+00E2 (a with circumflex) is not in the homoglyph table for 'a'; the filter does
        // no diacritic normalization, so this passes through (current behavior of the map).
        Assert.False(BasisWordFilter.ContainsBannedWord("d00E2mn", new[] { "damn" }, out _));
        Assert.Equal("d00E2mn", BasisWordFilter.Filter("d00E2mn", new[] { "damn" }));
    }

    [Fact]
    public void WordFilter_MultiWordPhrase_DetectedAndMasked()
    {
        Assert.True(BasisWordFilter.ContainsBannedWord("please go die now", new[] { "go die" }, out string matched));
        Assert.Equal("go die", matched);
        // The phrase's interior space is part of the match and is starred too.
        Assert.Equal("please ****** now", BasisWordFilter.Filter("please go die now", new[] { "go die" }));
    }

    [Fact]
    public void WordFilter_PhraseWordsFarApart_NotFlagged()
    {
        // "out"/"uts"/"tsi" trigrams backtrack the partial "go " match, so an innocent
        // sentence containing both words separately is not treated as the phrase.
        Assert.False(BasisWordFilter.ContainsBannedWord("go outside and die", new[] { "go die" }, out _));
        Assert.Equal("go outside and die", BasisWordFilter.Filter("go outside and die", new[] { "go die" }));
    }

    [Fact]
    public void WordFilter_MatchedWord_ReportsFirstBlacklistEntryThatMatches()
    {
        Assert.True(BasisWordFilter.ContainsBannedWord("crap damn", new[] { "damn", "crap" }, out string matched));
        Assert.Equal("damn", matched);
    }

    [Fact]
    public void WordFilter_MultipleBannedWords_AllMasked()
    {
        Assert.Equal("**** ****", BasisWordFilter.Filter("damn crap", new[] { "damn", "crap" }));
    }

    [Fact]
    public void WordFilter_RepeatedOccurrences_AllMasked()
    {
        Assert.Equal("**** **** ****", BasisWordFilter.Filter("damn damn damn", new[] { "damn" }));
    }

    [Fact]
    public void WordFilter_EmptyAndNullInputs_SafeDefaults()
    {
        string[] list = { "damn" };

        Assert.False(BasisWordFilter.ContainsBannedWord(string.Empty, list, out string matched));
        Assert.Equal(string.Empty, matched);
        Assert.False(BasisWordFilter.ContainsBannedWord(null!, list, out matched));
        Assert.Equal(string.Empty, matched);
        Assert.False(BasisWordFilter.ContainsBannedWord("damn", Array.Empty<string>(), out matched));
        Assert.Equal(string.Empty, matched);
        Assert.False(BasisWordFilter.ContainsBannedWord("damn", null!, out matched));
        Assert.Equal(string.Empty, matched);

        Assert.Equal(string.Empty, BasisWordFilter.Filter(string.Empty, list));
        Assert.Null(BasisWordFilter.Filter(null!, list));
        Assert.Equal("damn", BasisWordFilter.Filter("damn", Array.Empty<string>()));
        Assert.Equal("damn", BasisWordFilter.Filter("damn", null!));
    }

    [Fact]
    public void WordFilter_BlankBlacklistEntries_Ignored()
    {
        Assert.False(BasisWordFilter.ContainsBannedWord("damn", new[] { "" }, out _));
        Assert.Equal("damn", BasisWordFilter.Filter("damn", new[] { "" }));
        Assert.True(BasisWordFilter.ContainsBannedWord("damn", new[] { "", "damn" }, out string matched));
        Assert.Equal("damn", matched);
    }

    [Fact]
    public void WordFilter_LongCleanMessage_CompletesUnchanged()
    {
        // Sentence contains no 'a'/'d' (or their ASCII homoglyphs '@','4','0','O'), so none
        // of the words can ever complete; pins that a large message is handled and untouched.
        string text = string.Concat(Enumerable.Repeat("we welcome everyone to the event tonight. ", 120));
        string[] list = { "damn", "crap", "ass" };
        Assert.False(BasisWordFilter.ContainsBannedWord(text, list, out _));
        Assert.Equal(text, BasisWordFilter.Filter(text, list));
    }

    [Fact]
    public void WordFilter_LongMessageWithManyHits_AllMasked()
    {
        string text = string.Concat(Enumerable.Repeat("damn ", 100));
        string result = BasisWordFilter.Filter(text, new[] { "damn" });
        Assert.Equal(string.Concat(Enumerable.Repeat("**** ", 100)), result);
        Assert.Equal(text.Length, result.Length);
    }

    [Fact]
    public void WordFilter_Idempotent()
    {
        string[] list = { "damn", "crap", "ass" };
        string[] inputs = { "you damn fool", "d a m n", "damn crap", "assignment", "DAMN" };
        foreach (string input in inputs)
        {
            string once = BasisWordFilter.Filter(input, list);
            Assert.Equal(once, BasisWordFilter.Filter(once, list));
        }
    }

    [Fact]
    public void ServerChatEntryPoint_FilterMessage_PassthroughWhenNoListLoaded()
    {
        // LoadWordFilter is never invoked by tests, so the static list is empty and the
        // server entry point must forward messages untouched.
        Assert.Equal("damn message", BasisNetworkChat.FilterMessage("damn message"));
        Assert.Equal(string.Empty, BasisNetworkChat.FilterMessage(string.Empty));
    }
}
