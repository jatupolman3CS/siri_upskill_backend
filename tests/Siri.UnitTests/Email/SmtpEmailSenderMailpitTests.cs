using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Email;

namespace Siri.UnitTests.Email;

/// <summary>Skipped unless <c>SIRI_TEST_MAILPIT=1</c> — a manual check against the real dev mail catcher that
/// <c>scripts/dev.ps1</c> starts (SMTP 127.0.0.1:1025, HTTP API http://localhost:8025). Not a mock: it sends one
/// real email through <see cref="SmtpEmailSender"/> and reads it back from Mailpit.</summary>
public sealed class MailpitFactAttribute : FactAttribute
{
    public MailpitFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SIRI_TEST_MAILPIT") != "1")
        {
            Skip = "Set SIRI_TEST_MAILPIT=1 with the dev stack's Mailpit running (SMTP :1025, API :8025) to run.";
        }
    }
}

public class SmtpEmailSenderMailpitTests
{
    private const string MailpitApi = "http://localhost:8025/api/v1";

    private static readonly string Ics = string.Join(
        "\r\n",
        "BEGIN:VCALENDAR",
        "VERSION:2.0",
        "PRODID:-//SIRI UpSkill//Live Sessions//TH",
        "CALSCALE:GREGORIAN",
        "METHOD:REQUEST",
        "X-WR-TIMEZONE:Asia/Bangkok",
        "BEGIN:VEVENT",
        "UID:0198a1b2c3d47e5f8a9b0c1d2e3f4a5b@siriupskill.test",
        "DTSTAMP:20261001T030000Z",
        "SEQUENCE:0",
        "DTSTART:20271001T030000Z",
        "DTEND:20271001T050000Z",
        "SUMMARY:ทดสอบ P11-04 คาบเรียนสด",
        "LOCATION:ออนไลน์ — https://siriupskill.test/live/0198a1b2c3d47e5f8a9b0c1d2e3f4a5b/join",
        "URL:https://siriupskill.test/live/0198a1b2c3d47e5f8a9b0c1d2e3f4a5b/join",
        "ORGANIZER;CN=SIRI UpSkill:mailto:no-reply@siriupskill.test",
        "ATTENDEE;CN=Learner;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=FALSE:mailto:learner@example.test",
        "STATUS:CONFIRMED",
        "END:VEVENT",
        "END:VCALENDAR",
        string.Empty);

    [MailpitFact]
    public async Task SendAsync_MessageWithCalendar_ArrivesInMailpitWithTheCalendarPartAndAttachment()
    {
        var recipient = $"p11-04-{Guid.NewGuid():N}@example.test";
        var sender = new SmtpEmailSender(
            Options.Create(new SmtpEmailSenderOptions
            {
                Host = "127.0.0.1",
                Port = 1025,
                FromAddress = "no-reply@siriupskill.test",
                FromDisplayName = "SIRI UpSkill Dev",
                AllowInsecure = true,
            }),
            NullLogger<SmtpEmailSender>.Instance);

        var result = await sender.SendAsync(
            new EmailMessage(
                recipient,
                "[P11-04 test] ยืนยันตารางเรียนสด",
                "<h2>ยืนยันตารางเรียนสด</h2><p>อีเมลทดสอบจาก SmtpEmailSenderMailpitTests</p>",
                new EmailCalendarContent("REQUEST", Ics)),
            CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);

        using var http = new HttpClient();
        using var search = await http.GetAsync($"{MailpitApi}/search?query={Uri.EscapeDataString("to:" + recipient)}");
        search.EnsureSuccessStatusCode();
        var found = await search.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, found.GetProperty("messages_count").GetInt32());
        var id = found.GetProperty("messages")[0].GetProperty("ID").GetString();

        var raw = await http.GetStringAsync($"{MailpitApi}/message/{id}/raw");
        Assert.Contains("multipart/mixed", raw, StringComparison.Ordinal);
        Assert.Contains("multipart/alternative", raw, StringComparison.Ordinal);
        Assert.Contains("method=REQUEST", raw, StringComparison.Ordinal);
        Assert.Contains("invite.ics", raw, StringComparison.Ordinal);

        var message = await http.GetFromJsonAsync<JsonElement>($"{MailpitApi}/message/{id}");
        var attachment = Assert.Single(message.GetProperty("Attachments").EnumerateArray());
        Assert.Equal("invite.ics", attachment.GetProperty("FileName").GetString());
        Assert.StartsWith("text/calendar", attachment.GetProperty("ContentType").GetString(), StringComparison.Ordinal);

        var partId = attachment.GetProperty("PartID").GetString();
        var attachmentBody = await http.GetStringAsync($"{MailpitApi}/message/{id}/part/{partId}");
        Assert.Equal(Ics, attachmentBody);
    }
}
