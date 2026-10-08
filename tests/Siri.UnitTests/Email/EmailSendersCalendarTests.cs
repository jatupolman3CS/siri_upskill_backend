using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Email;

namespace Siri.UnitTests.Email;

/// <summary>
/// What each <see cref="IEmailSender"/> does with an <see cref="EmailMessage"/> that carries a calendar part
/// (task P11-04): the log-only and unconfigured senders must not crash and must not leak the document; the SMTP
/// sender must reject a bad recipient/calendar <i>before</i> opening a connection (proved by the specific error
/// code — a connection attempt to the closed port used below would surface as <c>email.send_failed</c> instead).
/// </summary>
public class EmailSendersCalendarTests
{
    private const string IcsMarker = "UID:0198a1b2c3d47e5f8a9b0c1d2e3f4a5b@siriupskill.test";

    private static readonly string Ics =
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\n" + IcsMarker + "\r\nSUMMARY:คาบเรียนสด\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static EmailMessage Message(string method = "REQUEST", string? ics = null, string to = "learner@example.test") =>
        new(to, "ยืนยันตารางเรียนสด", "<p>ตารางเรียน</p>", new EmailCalendarContent(method, ics ?? Ics));

    private static SmtpEmailSender NewSmtpSender() =>
        new(
            Options.Create(new SmtpEmailSenderOptions
            {
                // Closed local port: if the sender ever tried to connect it would fail with email.send_failed.
                Host = "127.0.0.1",
                Port = 1,
                FromAddress = "no-reply@siriupskill.test",
                AllowInsecure = true,
            }),
            NullLogger<SmtpEmailSender>.Instance);

    [Fact]
    public async Task LoggingEmailSender_MessageWithCalendar_SucceedsAndDoesNotLogTheCalendar()
    {
        var logger = new CapturingLogger<LoggingEmailSender>();
        var sender = new LoggingEmailSender(logger);

        var result = await sender.SendAsync(Message(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains("learner@example.test", entry, StringComparison.Ordinal);
        Assert.DoesNotContain(IcsMarker, entry, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN:VCALENDAR", entry, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnconfiguredEmailSender_MessageWithCalendar_FailsLoudlyWithoutLoggingTheCalendar()
    {
        var logger = new CapturingLogger<UnconfiguredEmailSender>();
        var sender = new UnconfiguredEmailSender(logger);

        var result = await sender.SendAsync(Message(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(UnconfiguredEmailSender.ProviderNotConfiguredCode, result.Error.Code);
        Assert.DoesNotContain(logger.Entries, e => e.Contains(IcsMarker, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("REPLY")]
    [InlineData("request")]
    [InlineData("REQUEST\r\nBcc: attacker@evil.test")]
    public async Task SmtpEmailSender_InvalidCalendarMethod_FailsBeforeConnecting(string method)
    {
        var result = await NewSmtpSender().SendAsync(Message(method), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(EmailMimeMessageFactory.InvalidCalendarCode, result.Error.Code);
    }

    [Fact]
    public async Task SmtpEmailSender_OversizedCalendar_FailsBeforeConnecting()
    {
        var ics = "BEGIN:VCALENDAR\r\n" + new string('a', EmailMimeMessageFactory.MaxCalendarContentLength) + "\r\nEND:VCALENDAR\r\n";

        var result = await NewSmtpSender().SendAsync(Message(ics: ics), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(EmailMimeMessageFactory.InvalidCalendarCode, result.Error.Code);
    }

    [Theory]
    [InlineData("learner@example.test\r\nBcc: attacker@evil.test")]
    [InlineData("learner@example.test, attacker@evil.test")]
    public async Task SmtpEmailSender_InvalidRecipient_FailsBeforeConnecting(string to)
    {
        var withCalendar = await NewSmtpSender().SendAsync(Message(to: to), CancellationToken.None);
        var plain = await NewSmtpSender().SendAsync(new EmailMessage(to, "s", "<p>b</p>"), CancellationToken.None);

        Assert.Equal(EmailMimeMessageFactory.InvalidRecipientCode, withCalendar.Error.Code);
        Assert.Equal(EmailMimeMessageFactory.InvalidRecipientCode, plain.Error.Code);
    }

    [Fact]
    public async Task SmtpEmailSender_ValidMessageButNoServer_ReportsASendFailureNotAValidationError()
    {
        // Control: with a valid recipient and calendar the sender does reach the network step (and fails
        // there because nothing listens on the port) — so the codes in the tests above really do come from
        // pre-connection validation.
        var result = await NewSmtpSender().SendAsync(Message(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("email.send_failed", result.Error.Code);
    }

    [Fact]
    public void EmailMessage_ThreeArgumentConstruction_StillCompilesAndHasNoCalendar()
    {
        var message = new EmailMessage("a@example.test", "Subject", "<p>Body</p>");

        Assert.Null(message.Calendar);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add(formatter(state, exception));
    }
}
