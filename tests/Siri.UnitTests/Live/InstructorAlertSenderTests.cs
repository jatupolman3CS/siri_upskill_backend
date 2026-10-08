using Microsoft.Extensions.Logging.Abstractions;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Notification.Contracts;

namespace Siri.UnitTests.Live;

/// <summary>The instructor alert e-mails: every database-derived value is HTML-encoded, subjects are single-line, links are platform pages only,
/// and an instructor without an address is skipped without logging it.</summary>
public class InstructorAlertSenderTests
{
    private static readonly Guid InstructorId = Guid.NewGuid();

    private readonly RecordingOutbox _outbox = new();
    private readonly RecordingInApp _inApp = new();
    private readonly FakeContacts _contacts = new();
    private readonly ListLogger<InstructorAlertSender> _logger = new();

    private InstructorAlertSender Sender() => new(_outbox, _inApp, _contacts, LiveTestData.OptionsOf(), _logger);

    [Fact]
    public async Task GoogleReconnectNeeded_QueuesAnEmailToTheInstructor_LinkingToTheSettingsPage()
    {
        await Sender().GoogleReconnectNeededAsync(InstructorId, 3, CancellationToken.None);

        var message = Assert.Single(_outbox.Messages);
        Assert.Equal("teacher@gmail.test", message.ToEmail);
        Assert.Equal("เชื่อม Google Calendar ใหม่เพื่อให้ห้อง Meet ทำงานต่อ", message.Subject);
        Assert.Equal("live-google-reconnect", message.TemplateKey);
        Assert.Contains("https://app.example.test/instructor/live-settings", message.BodyHtml);
        Assert.Contains("<strong>3</strong>", message.BodyHtml);
    }

    [Fact]
    public async Task MeetingNeedsLink_LinksToTheSessionPage_AndHasNoMeetingUrl()
    {
        var sessionId = Guid.NewGuid();

        await Sender().MeetingNeedsLinkAsync(InstructorId, sessionId, "คาบที่ 2", "คอร์สภาษาไทย", CancellationToken.None);

        var message = Assert.Single(_outbox.Messages);
        Assert.Equal("วางลิงก์ห้องประชุมสำหรับคาบ คาบที่ 2", message.Subject);
        Assert.Equal("live-meeting-needs-link", message.TemplateKey);
        Assert.Contains($"https://app.example.test/instructor/sessions/{sessionId:D}", message.BodyHtml);
        Assert.DoesNotContain("meet.google.com", message.BodyHtml);
    }

    [Fact]
    public async Task MeetingFailed_UsesTheContractSubject()
    {
        await Sender().MeetingFailedAsync(InstructorId, Guid.NewGuid(), "คาบที่ 3", "คอร์ส", CancellationToken.None);

        var message = Assert.Single(_outbox.Messages);
        Assert.Equal("สร้างห้อง Google Meet ไม่สำเร็จ — คาบที่ 3", message.Subject);
        Assert.Equal("live-meeting-failed", message.TemplateKey);
    }

    [Fact]
    public async Task EveryDatabaseDerivedValue_IsHtmlEncodedInTheBody()
    {
        await Sender().MeetingNeedsLinkAsync(
            InstructorId, Guid.NewGuid(), "<script>alert(1)</script>", "Tom & \"Jerry\" <b>bold</b>", CancellationToken.None);

        var body = Assert.Single(_outbox.Messages).BodyHtml;
        Assert.DoesNotContain("<script>alert(1)</script>", body);
        Assert.DoesNotContain("<b>bold</b>", body);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", body);
        Assert.Contains("Tom &amp; &quot;Jerry&quot; &lt;b&gt;bold&lt;/b&gt;", body);
    }

    [Fact]
    public async Task Subject_NeverCarriesLineBreaks_SoATitleCannotInjectHeaders()
    {
        await Sender().MeetingNeedsLinkAsync(InstructorId, Guid.NewGuid(), "Hello\r\nBcc: attacker@example.test", "c", CancellationToken.None);

        var subject = Assert.Single(_outbox.Messages).Subject;
        Assert.DoesNotContain('\r', subject);
        Assert.DoesNotContain('\n', subject);
    }

    [Fact]
    public async Task Subject_LongTitle_IsTruncated()
    {
        await Sender().MeetingFailedAsync(InstructorId, Guid.NewGuid(), new string('ก', 500), "c", CancellationToken.None);

        Assert.True(Assert.Single(_outbox.Messages).Subject.Length < 150);
    }

    [Fact]
    public async Task InstructorWithoutAnEmailAddress_IsSkipped_AndTheAddressIsNeverLogged()
    {
        _contacts.Email = null;

        await Sender().GoogleReconnectNeededAsync(InstructorId, 1, CancellationToken.None);

        Assert.Empty(_outbox.Messages);
        Assert.Contains(InstructorId.ToString(), _logger.All);
    }

    [Fact]
    public async Task NothingIsLoggedThatContainsTheAddressOrTheTitle()
    {
        await Sender().MeetingNeedsLinkAsync(InstructorId, Guid.NewGuid(), "ชื่อคาบลับ", "คอร์สลับ", CancellationToken.None);

        Assert.DoesNotContain("teacher@gmail.test", _logger.All);
    }

    [Fact]
    public void Constructor_AcceptsNullLoggerForTests()
    {
        // Guards the primary-constructor signature the module's DI registration relies on.
        _ = new InstructorAlertSender(_outbox, _inApp, _contacts, LiveTestData.OptionsOf(), NullLogger<InstructorAlertSender>.Instance);
    }

    private sealed record Message(string ToEmail, string Subject, string BodyHtml, string? TemplateKey);

    private sealed class RecordingOutbox : IEmailOutbox
    {
        public List<Message> Messages { get; } = [];

        public void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey) =>
            Messages.Add(new Message(toEmail, subject, bodyHtml, templateKey));
    }

    private sealed class FakeContacts : IUserContactReader
    {
        public string? Email { get; set; } = "teacher@gmail.test";

        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Email);

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
