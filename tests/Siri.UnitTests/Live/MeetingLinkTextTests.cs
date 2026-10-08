using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>
/// D3 (integrator-qa): a session title/description/cancel reason containing a Meet/Zoom/Teams URL leaked on the PUBLIC course detail.
/// <see cref="MeetingLinkText"/> is the single rule (SharedKernel — Catalog rejects on write and scrubs on read, Live scrubs outbound text), so it is
/// tested here against the plain forms and the disguises an instructor — or someone trying to get a room link past a filter — would use.
/// </summary>
public class MeetingLinkTextTests
{
    // ---- Links that must be detected -----------------------------------------------------------------

    [Theory]
    // plain
    [InlineData("https://meet.google.com/abc-defg-hij")]
    [InlineData("http://meet.google.com/abc-defg-hij")]
    [InlineData("https://zoom.us/j/987654321?pwd=SECRETPASSCODE")]
    [InlineData("https://us02web.zoom.us/j/987654321?pwd=SECRET")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=%7b%7d")]
    [InlineData("https://teams.live.com/meet/9876543210")]
    // scheme-less
    [InlineData("meet.google.com/abc-defg-hij")]
    [InlineData("zoom.us/j/987654321")]
    [InlineData("teams.microsoft.com/l/meetup-join/x")]
    // case
    [InlineData("HTTPS://MEET.GOOGLE.COM/ABC-DEFG-HIJ")]
    [InlineData("Meet.Google.Com/abc-defg-hij")]
    [InlineData("hTTps://Us02Web.ZOOM.us/j/1")]
    // surrounded by other text / punctuation
    [InlineData("เข้าห้องที่ https://zoom.us/j/123 ครับ")]
    [InlineData("(https://zoom.us/j/1)")]
    [InlineData("\"https://zoom.us/j/1\"")]
    [InlineData("<https://zoom.us/j/1>")]
    [InlineData("link:meet.google.com/abc-defg-hij")]
    public void ContainsLink_PlainAndSchemeLessAndMixedCase_IsDetected(string text)
    {
        Assert.True(MeetingLinkText.ContainsLink(text));
    }

    [Theory]
    // zero-width / invisible characters inside the host
    [InlineData("meet.goo\u200Bgle.com/abc-defg-hij")] // zero-width space
    [InlineData("meet.goo\u200Cgle.com/abc-defg-hij")] // zero-width non-joiner
    [InlineData("meet.goo\u200Dgle.com/abc-defg-hij")] // zero-width joiner
    [InlineData("meet.goo\u2060gle.com/abc-defg-hij")] // word joiner
    [InlineData("meet.goo\u00ADgle.com/abc-defg-hij")] // soft hyphen
    [InlineData("\uFEFFmeet.google.com/abc-defg-hij")] // BOM in front
    [InlineData("https://zo\u200Bom.us/j/123")]
    [InlineData("https://teams.microsoft\u200B.com/l/x")]
    [InlineData("meet\u200B.\u200Bgoogle\u200B.\u200Bcom/abc")]
    // tab / line breaks inside the host (URL parsers delete them)
    [InlineData("meet.goo\tgle.com/abc-defg-hij")]
    [InlineData("https://meet.go\togle.com/abc-defg-hij")]
    [InlineData("meet.goo\r\ngle.com/abc-defg-hij")]
    [InlineData("zoom\n.us/j/123")]
    [InlineData("meet.\tgoogle.com/abc")]
    // full-width characters and dots (IDNA maps them to ASCII)
    [InlineData("ｍｅｅｔ.ｇｏｏｇｌｅ.ｃｏｍ/abc-defg-hij")]
    [InlineData("meet\u3002google\u3002com/abc-defg-hij")]
    [InlineData("meet\uFF0Egoogle\uFF0Ecom/abc-defg-hij")]
    [InlineData("zoom\uFF61us/j/123")]
    // percent- and HTML-encoded
    [InlineData("https%3A%2F%2Fmeet.google.com%2Fabc-defg-hij")]
    [InlineData("meet%2Egoogle%2Ecom%2Fabc-defg-hij")]
    [InlineData("https%3A%2F%2Fzoom.us%2Fj%2F123")]
    [InlineData("meet&#46;google&#46;com/abc")]
    [InlineData("https&#58;//zoom&#46;us/j/1")]
    // combinations
    [InlineData("https%3A%2F%2Fmeet.goo\u200Bgle.com%2Fabc")]
    public void ContainsLink_DisguisedLinks_AreDetected(string text)
    {
        Assert.True(MeetingLinkText.ContainsLink(text), $"not detected: {text.Replace("\u200B", "<ZWSP>")}");
    }

    [Fact]
    public void ContainsLink_AlsoCoversConfiguredExtraHosts()
    {
        Assert.False(MeetingLinkText.ContainsLink("https://webex.example.test/room/1"));
        Assert.True(MeetingLinkText.ContainsLink("https://webex.example.test/room/1", ["webex.example.test"]));
        Assert.True(MeetingLinkText.ContainsLink("https://zoom.us/j/1", ["webex.example.test"])); // the defaults are always included
    }

    // ---- Text that must NOT be flagged -----------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("คาบที่ 1 — แนะนำคอร์ส")]
    [InlineData("Live Q&A 50% off — week 3 recap")]
    [InlineData("50%25 discount")]
    [InlineData("Meeting about Google Meet and Zoom and Microsoft Teams")] // product names are not links
    [InlineData("https://example.com/slides/week-1.pdf")]
    [InlineData("https://www.youtube.com/watch?v=abc123")]
    [InlineData("https://app.example.test/live/0b6f/join")] // the platform's own join link
    [InlineData("https://evilzoom.us/j/1")] // look-alike registrable domain
    [InlineData("https://notmeet.google.com.evil.test/x")]
    [InlineData("meetgoogle.com/abc")]
    [InlineData("เรียนรู้ 🎓\u200D📚 กับเรา")] // emoji ZWJ sequence — invisible characters alone are not a link
    public void ContainsLink_OrdinaryText_IsNotFlagged(string? text)
    {
        Assert.False(MeetingLinkText.ContainsLink(text));
    }

    // ---- Scrub ------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij")]
    [InlineData("https://us02web.zoom.us/j/987654321?pwd=SECRET")]
    [InlineData("meet.goo\u200Bgle.com/abc-defg-hij")]
    [InlineData("meet.goo\tgle.com/abc-defg-hij")]
    [InlineData("ｍｅｅｔ.ｇｏｏｇｌｅ.ｃｏｍ/abc-defg-hij")]
    [InlineData("https%3A%2F%2Fmeet.google.com%2Fabc-defg-hij")]
    [InlineData("meet&#46;google&#46;com/abc-defg-hij")]
    public void Scrub_RemovesTheLink_PlainOrDisguised(string link)
    {
        var scrubbed = MeetingLinkText.Scrub($"เข้าห้อง {link} นะ");

        Assert.NotNull(scrubbed);
        Assert.Contains(MeetingLinkText.Placeholder, scrubbed);
        Assert.DoesNotContain("abc-defg-hij", scrubbed);
        Assert.DoesNotContain("987654321", scrubbed);
        Assert.False(MeetingLinkText.ContainsLink(scrubbed), "the scrubbed text must itself be clean");
    }

    [Fact]
    public void Scrub_PlainLink_KeepsTheSurroundingTextExactly()
    {
        Assert.Equal(
            $"เข้าห้อง {MeetingLinkText.Placeholder} นะ",
            MeetingLinkText.Scrub("เข้าห้อง https://meet.google.com/abc-defg-hij นะ"));
    }

    [Theory]
    [InlineData("คาบที่ 1 — แนะนำคอร์ส")]
    [InlineData("Live Q&A 50% off")]
    [InlineData("https://example.com/slides")]
    [InlineData("https://evilzoom.us/j/1")]
    [InlineData("สวัสดี\u200Bครับ")] // invisible characters but no link: returned untouched
    public void Scrub_TextWithoutALink_IsReturnedUnchanged(string text)
    {
        Assert.Equal(text, MeetingLinkText.Scrub(text));
    }

    [Fact]
    public void Scrub_NullAndEmpty_PassThrough()
    {
        Assert.Null(MeetingLinkText.Scrub(null));
        Assert.Equal(string.Empty, MeetingLinkText.Scrub(string.Empty));
        Assert.Equal(string.Empty, MeetingLinkText.ScrubRequired(string.Empty));
    }

    [Fact]
    public void Scrub_APlainLinkAndADisguisedLinkInTheSameText_AreBothRemoved_InOneCall()
    {
        // A first pass over the plain form must not leave the disguised one behind (the text is only safe once NO variant matches).
        var zeroWidthSpace = ((char)0x200B).ToString();
        var scrubbed = MeetingLinkText.ScrubRequired(
            $"1) https://zoom.us/j/111  2) meet.go{zeroWidthSpace}ogle.com/aaa-bbbb-ccc  3) https%3A%2F%2Fteams.live.com%2Fmeet%2F9");

        Assert.False(MeetingLinkText.ContainsLink(scrubbed), scrubbed);
        Assert.DoesNotContain("111", scrubbed);
        Assert.DoesNotContain("aaa-bbbb-ccc", scrubbed);
        Assert.DoesNotContain("teams.live.com", scrubbed);
        Assert.Equal(3, scrubbed.Split(MeetingLinkText.Placeholder).Length - 1);
    }

    [Fact]
    public void Scrub_IsIdempotent()
    {
        var once = MeetingLinkText.ScrubRequired("a https://zoom.us/j/1 b meet.go\u200Bogle.com/x c");
        var twice = MeetingLinkText.ScrubRequired(once);

        Assert.Equal(once, twice);
    }

    [Fact]
    public void ContainsLink_PathologicalInput_FinishesQuickly_AndNeverThrows()
    {
        // Long runs of host-like labels are the worst case for the matcher; the regex has a timeout and the helper fails closed instead of throwing.
        var evil = string.Concat(Enumerable.Repeat("a-", 1500)) + "." + string.Concat(Enumerable.Repeat("b.", 1500));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _ = MeetingLinkText.ContainsLink(evil);
        _ = MeetingLinkText.Scrub(evil);

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
    }

    [Fact]
    public void LiveOptions_DefaultHosts_AreTheSharedKernelList()
    {
        // Single source: the Live allow-list and the text rule can never drift apart.
        Assert.Same(MeetingLinkText.DefaultHosts, Siri.Modules.Live.Application.LiveOptions.DefaultAllowedMeetingHosts);
    }
}
