using Microsoft.Extensions.Logging.Abstractions;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Notification.Contracts;

namespace Siri.UnitTests.Live;

/// <summary>The three P11-13 instructor alerts (recording imported / import failed / reconnect Google): Thai text, links to the platform's own pages, every value encoded,
/// an in-app notification next to the e-mail, and never a room link, Drive id or secret.</summary>
public class InstructorRecordingAlertTests
{
    private static readonly Guid InstructorId = Guid.NewGuid();

    private readonly RecordingEmailOutbox _outbox = new();
    private readonly RecordingInApp _inApp = new();
    private readonly StubContacts _contacts = new();

    private InstructorAlertSender Sender() => new(_outbox, _inApp, _contacts, LiveTestData.OptionsOf(), NullLogger<InstructorAlertSender>.Instance);

    [Fact]
    public async Task RecordingImported_TellsTheInstructorTheLessonIsLive_AndLinksToTheSession()
    {
        var sessionId = Guid.NewGuid();

        await Sender().RecordingImportedAsync(InstructorId, sessionId, "คาบที่ 4", "คอร์สภาษาไทย", CancellationToken.None);

        var message = Assert.Single(_outbox.Messages);
        Assert.Equal("teacher@gmail.test", message.ToEmail);
        Assert.Equal("เพิ่มบันทึกการสอนของคาบ คาบที่ 4 แล้ว", message.Subject);
        Assert.Equal("live-recording-imported", message.TemplateKey);
        Assert.Contains($"https://app.example.test/instructor/sessions/{sessionId:D}", message.BodyHtml);
        Assert.Contains("ผู้เรียนที่ลงทะเบียน", message.BodyHtml); // learners can watch it at once: the instructor is warned
        Assert.DoesNotContain("meet.google.com", message.BodyHtml);

        var notification = Assert.Single(_inApp.Items);
        Assert.Equal((InstructorId, "live.recording_imported"), (notification.UserId, notification.Type));
        Assert.Equal($"/instructor/sessions/{sessionId:D}", notification.LinkUrl);
    }

    [Fact]
    public async Task RecordingImportFailed_PointsToTheManualUploadAndTheRetry()
    {
        var sessionId = Guid.NewGuid();

        await Sender().RecordingImportFailedAsync(InstructorId, sessionId, "คาบที่ 5", "คอร์ส", CancellationToken.None);

        var message = Assert.Single(_outbox.Messages);
        Assert.Equal("นำเข้าบันทึกของคาบ คาบที่ 5 ไม่สำเร็จ", message.Subject);
        Assert.Equal("live-recording-import-failed", message.TemplateKey);
        Assert.Contains("อัปโหลดวิดีโอบันทึกการสอนด้วยตัวเอง", message.BodyHtml);
        Assert.Contains($"https://app.example.test/instructor/sessions/{sessionId:D}", message.BodyHtml);
        Assert.Equal("live.recording_import_failed", Assert.Single(_inApp.Items).Type);
    }

    [Fact]
    public async Task RecordingNeedsReconnect_LinksToTheGoogleSettingsPage()
    {
        await Sender().RecordingNeedsReconnectAsync(InstructorId, Guid.NewGuid(), "คาบที่ 6", "คอร์ส", CancellationToken.None);

        var message = Assert.Single(_outbox.Messages);
        Assert.Equal("เชื่อมต่อ Google ใหม่เพื่อนำเข้าบันทึกของคาบ คาบที่ 6", message.Subject);
        Assert.Equal("live-recording-needs-reconnect", message.TemplateKey);
        Assert.Contains("https://app.example.test/instructor/live-settings", message.BodyHtml);

        var notification = Assert.Single(_inApp.Items);
        Assert.Equal("live.recording_needs_reconnect", notification.Type);
        Assert.Equal("/instructor/live-settings", notification.LinkUrl);
    }

    [Fact]
    public async Task EveryDatabaseDerivedValue_IsEncoded_InAllThree()
    {
        const string title = "<script>alert(1)</script>";
        const string course = "Tom & \"Jerry\" <b>bold</b>";

        await Sender().RecordingImportedAsync(InstructorId, Guid.NewGuid(), title, course, CancellationToken.None);
        await Sender().RecordingImportFailedAsync(InstructorId, Guid.NewGuid(), title, course, CancellationToken.None);
        await Sender().RecordingNeedsReconnectAsync(InstructorId, Guid.NewGuid(), title, course, CancellationToken.None);

        foreach (var message in _outbox.Messages)
        {
            Assert.DoesNotContain("<script>alert(1)</script>", message.BodyHtml);
            Assert.DoesNotContain("<b>bold</b>", message.BodyHtml);
            Assert.Contains("&lt;script&gt;", message.BodyHtml);
        }
    }

    [Fact]
    public async Task ATitleCarryingLineBreaksOrAMeetingLink_CannotInjectHeadersOrALink()
    {
        await Sender().RecordingImportedAsync(
            InstructorId, Guid.NewGuid(), "Hello\r\nBcc: attacker@example.test https://meet.google.com/abc-defg-hij", "คอร์ส", CancellationToken.None);

        var message = Assert.Single(_outbox.Messages);
        Assert.DoesNotContain('\r', message.Subject);
        Assert.DoesNotContain('\n', message.Subject);
        Assert.DoesNotContain("abc-defg-hij", message.BodyHtml);
        Assert.DoesNotContain("abc-defg-hij", message.Subject);
    }

    [Fact]
    public async Task AnInstructorWithoutAnEmailAddress_StillGetsTheInAppNotification()
    {
        _contacts.Email = null;

        await Sender().RecordingNeedsReconnectAsync(InstructorId, Guid.NewGuid(), "คาบ", "คอร์ส", CancellationToken.None);

        Assert.Empty(_outbox.Messages);
        Assert.Single(_inApp.Items);
    }

    private sealed record Message(string ToEmail, string Subject, string BodyHtml, string? TemplateKey);

    private sealed class RecordingEmailOutbox : IEmailOutbox
    {
        public List<Message> Messages { get; } = [];

        public void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey) =>
            Messages.Add(new Message(toEmail, subject, bodyHtml, templateKey));
    }

    private sealed class StubContacts : IUserContactReader
    {
        public string? Email { get; set; } = "teacher@gmail.test";

        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(Email);

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
