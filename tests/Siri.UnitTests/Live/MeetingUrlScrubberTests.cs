using Siri.Modules.Live.Application;

namespace Siri.UnitTests.Live;

/// <summary>Unit tests for <see cref="MeetingUrlScrubber"/>: free text an instructor typed must never carry a meeting-room link into an
/// e-mail, calendar file or notification (docs/contracts/P11-04-live-invites-ics-reminders.md §0 item 3).</summary>
public class MeetingUrlScrubberTests
{
    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij")]
    [InlineData("http://meet.google.com/abc-defg-hij")]
    [InlineData("HTTPS://MEET.GOOGLE.COM/ABC-DEFG-HIJ")]
    [InlineData("meet.google.com/abc-defg-hij")]
    [InlineData("https://zoom.us/j/987654321?pwd=SECRETPASSCODE")]
    [InlineData("https://us02web.zoom.us/j/987654321?pwd=SECRET")]
    [InlineData("https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc%40thread.v2/0?context=%7b%7d")]
    [InlineData("https://teams.live.com/meet/9876543210")]
    public void Scrub_ReplacesAMeetingLink_WithThePlaceholder(string link)
    {
        var text = MeetingUrlScrubber.Scrub($"เข้าห้อง {link} นะ");

        Assert.Equal($"เข้าห้อง {MeetingUrlScrubber.Placeholder} นะ", text);
    }

    [Fact]
    public void Scrub_RemovesEveryLinkInTheText()
    {
        var text = MeetingUrlScrubber.Scrub("A https://zoom.us/j/1 B https://meet.google.com/x C teams.microsoft.com/l/x D");

        Assert.Equal($"A {MeetingUrlScrubber.Placeholder} B {MeetingUrlScrubber.Placeholder} C {MeetingUrlScrubber.Placeholder} D", text);
    }

    [Theory]
    [InlineData("https://evilzoom.us/j/1")]
    [InlineData("https://notmeet.google.com.evil.test/x")]
    public void Scrub_LeavesALookAlikeDomainAlone_BecauseItIsNotAMeetingHost(string link)
    {
        // "evilzoom.us" is a different registrable domain from "zoom.us" and the validator would reject it as a room link too;
        // scrubbing is only about real room hosts, so a look-alike is not touched.
        Assert.Equal(link, MeetingUrlScrubber.Scrub(link));
    }

    [Fact]
    public void Scrub_PlainTextWithoutALink_IsUnchanged()
    {
        const string text = "คาบที่ 1 — เรียนผ่านแพลตฟอร์ม https://app.example.test/live/abc/join";

        Assert.Equal(text, MeetingUrlScrubber.Scrub(text));
    }

    [Fact]
    public void Scrub_NullAndEmpty_PassThrough()
    {
        Assert.Null(MeetingUrlScrubber.Scrub(null));
        Assert.Equal(string.Empty, MeetingUrlScrubber.Scrub(string.Empty));
        Assert.Equal(string.Empty, MeetingUrlScrubber.ScrubRequired(string.Empty));
    }

    [Fact]
    public void Scrub_AlsoCoversConfiguredExtraHosts()
    {
        Assert.Equal(
            $"x {MeetingUrlScrubber.Placeholder} y",
            MeetingUrlScrubber.Scrub("x https://webex.example.test/room/1 y", ["webex.example.test"]));
        Assert.Equal(
            $"x {MeetingUrlScrubber.Placeholder} y",
            MeetingUrlScrubber.Scrub("x https://zoom.us/j/1 y", ["webex.example.test"]));
    }

    [Fact]
    public void Scrub_ALinkInsideBracketsOrQuotes_DoesNotSwallowTheClosingCharacter()
    {
        Assert.Equal($"({MeetingUrlScrubber.Placeholder})", MeetingUrlScrubber.Scrub("(https://zoom.us/j/1)"));
        Assert.Equal($"\"{MeetingUrlScrubber.Placeholder}\"", MeetingUrlScrubber.Scrub("\"https://zoom.us/j/1\""));
    }
}
