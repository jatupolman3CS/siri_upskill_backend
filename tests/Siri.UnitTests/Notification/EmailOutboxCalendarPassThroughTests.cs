using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using Siri.Integrations.Email;
using Siri.Modules.Notification;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.UnitTests.Notification;

/// <summary>
/// The calendar part's whole trip through the outbox (task P11-04, docs/contracts/P11-04-live-invites-ics-reminders.md
/// §3.1/§3.3): <see cref="IEmailOutbox"/> overload → <c>EmailOutbox</c> stages a row → the sender job maps the row to an
/// <see cref="EmailMessage"/> → the MIME factory builds the message. No database is touched: a context over a
/// never-opened connection string is enough to inspect what was staged on the change tracker (the same approach as
/// ConcurrencyTokenInterceptorTests).
/// </summary>
public class EmailOutboxCalendarPassThroughTests
{
    private const string Ics = "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nMETHOD:REQUEST\r\nBEGIN:VEVENT\r\nSUMMARY:คาบเรียนสด\r\nEND:VEVENT\r\nEND:VCALENDAR\r\n";

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Provider"] = "Log" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FakeClock(new DateTime(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc)));
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql("Host=localhost;Port=1;Database=unit-test;Username=none;Password=none"));
        services.AddNotificationModule(configuration);
        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<EMAIL_OUTBOX_MESSAGE> Staged(AppDbContext context) =>
        context.ChangeTracker.Entries<EMAIL_OUTBOX_MESSAGE>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();

    // ---- the contract's default body: existing implementers/fakes keep compiling and working --------------------

    [Fact]
    public void DefaultOverload_ImplementerWithoutCalendarSupport_NullCalendarFallsThroughToTheFourArgumentOverload()
    {
        var outbox = new LegacyOutbox();
        IEmailOutbox contract = outbox;

        contract.Enqueue("a@example.test", "Subject", "<p>Body</p>", "key", calendar: null);

        var sent = Assert.Single(outbox.Plain);
        Assert.Equal(("a@example.test", "Subject", "<p>Body</p>", "key"), sent);
    }

    [Fact]
    public void DefaultOverload_ImplementerWithoutCalendarSupport_RefusesACalendarInsteadOfDroppingIt()
    {
        var outbox = new LegacyOutbox();
        IEmailOutbox contract = outbox;

        Assert.Throws<NotSupportedException>(() =>
            contract.Enqueue("a@example.test", "Subject", "<p>Body</p>", "key", new EmailCalendarPart("REQUEST", Ics)));

        Assert.Empty(outbox.Plain);
    }

    [Fact]
    public void FourArgumentCall_OnTheContract_StillResolvesToTheOriginalMethod()
    {
        var outbox = new LegacyOutbox();
        IEmailOutbox contract = outbox;

        contract.Enqueue("a@example.test", "Subject", "<p>Body</p>", null);

        Assert.Single(outbox.Plain);
    }

    // ---- the real EmailOutbox -------------------------------------------------------------------------------------

    [Fact]
    public void DependencyInjection_ResolvesTheRealOutboxAsScoped()
    {
        using var provider = BuildProvider();
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var a1 = scopeA.ServiceProvider.GetRequiredService<IEmailOutbox>();
        var a2 = scopeA.ServiceProvider.GetRequiredService<IEmailOutbox>();
        var b = scopeB.ServiceProvider.GetRequiredService<IEmailOutbox>();

        Assert.Same(a1, a2);
        Assert.NotSame(a1, b);
        Assert.Equal("EmailOutbox", a1.GetType().Name);
    }

    [Fact]
    public void Enqueue_FourArguments_StagesAPlainEmailWithoutSaving()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IEmailOutbox>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        outbox.Enqueue("a@example.test", "Subject", "<p>Body</p>", "welcome");

        var row = Assert.Single(Staged(context));
        Assert.Equal(EmailOutboxStatus.Pending, row.Status);
        Assert.Null(row.CalendarMethod);
        Assert.Null(row.CalendarIcs);
        Assert.Equal("welcome", row.TemplateKey);
    }

    [Theory]
    [InlineData("REQUEST")]
    [InlineData("CANCEL")]
    [InlineData("PUBLISH")]
    public void Enqueue_WithCalendarPart_StagesTheMethodAndIcsOnTheRowWithoutSaving(string method)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IEmailOutbox>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        outbox.Enqueue("a@example.test", "ยืนยันตารางเรียนสด", "<p>ตาราง</p>", "live-invite-batch", new EmailCalendarPart(method, Ics));

        var row = Assert.Single(Staged(context));
        Assert.Equal(method, row.CalendarMethod);
        Assert.Equal(Ics, row.CalendarIcs);
        Assert.Equal(EmailOutboxStatus.Pending, row.Status);
        Assert.Equal("live-invite-batch", row.TemplateKey);
        Assert.Equal("a@example.test", row.ToEmail);
    }

    [Fact]
    public void Enqueue_FiveArgumentsWithNullCalendar_StagesAnOrdinaryEmail()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IEmailOutbox>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        outbox.Enqueue("a@example.test", "Subject", "<p>Body</p>", null, calendar: null);

        var row = Assert.Single(Staged(context));
        Assert.Null(row.CalendarMethod);
        Assert.Null(row.CalendarIcs);
    }

    public static TheoryData<string, string> RejectedParts => new()
    {
        { "request", Ics },
        { "REPLY", Ics },
        { "REQUEST ", Ics },
        { "", Ics },
        { "REQUEST", "" },
        { "REQUEST", "   " },
        { "REQUEST", "VERSION:2.0\r\nBEGIN:VCALENDAR\r\nEND:VCALENDAR" },
        { "REQUEST", "BEGIN:VCALENDAR" + new string('x', 200_000) },
    };

    [Theory]
    [MemberData(nameof(RejectedParts))]
    public void Enqueue_InvalidCalendarPart_ThrowsAtTheCallerAndStagesNothing(string method, string ics)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IEmailOutbox>();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Throws<ArgumentException>(() =>
            outbox.Enqueue("a@example.test", "Subject", "<p>Body</p>", null, new EmailCalendarPart(method, ics)));

        Assert.Empty(context.ChangeTracker.Entries());
    }

    // ---- row -> EmailMessage -> MIME ------------------------------------------------------------------------------

    [Fact]
    public void ToEmailMessage_PlainRow_HasNoCalendar()
    {
        var row = EMAIL_OUTBOX_MESSAGE.Enqueue("a@example.test", "Subject", "<p>Body</p>", "welcome");

        var message = EmailOutboxSenderJob.ToEmailMessage(row);

        Assert.Equal(new EmailMessage("a@example.test", "Subject", "<p>Body</p>"), message);
        Assert.Null(message.Calendar);
    }

    [Theory]
    [InlineData("REQUEST")]
    [InlineData("CANCEL")]
    [InlineData("PUBLISH")]
    public void ToEmailMessage_CalendarRow_PassesMethodAndIcsThroughUntouched(string method)
    {
        var row = EMAIL_OUTBOX_MESSAGE.Enqueue("a@example.test", "ยกเลิกคาบเรียนสด", "<p>ยกเลิก</p>", "live-session-cancelled", method, Ics);

        var message = EmailOutboxSenderJob.ToEmailMessage(row);

        Assert.Equal("a@example.test", message.ToAddress);
        Assert.Equal("ยกเลิกคาบเรียนสด", message.Subject);
        Assert.Equal("<p>ยกเลิก</p>", message.HtmlBody);
        Assert.NotNull(message.Calendar);
        Assert.Equal(method, message.Calendar.Method);
        Assert.Equal(Ics, message.Calendar.IcsContent);
    }

    [Theory]
    [InlineData("REQUEST")]
    [InlineData("CANCEL")]
    [InlineData("PUBLISH")]
    public void EndToEnd_EnqueuedCalendarEmail_BecomesAMimeMessageWithThatMethodAndDocument(string method)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IEmailOutbox>()
            .Enqueue("a@example.test", "ยืนยันตารางเรียนสด", "<p>ตาราง</p>", "live-invite-batch", new EmailCalendarPart(method, Ics));
        var row = Assert.Single(Staged(scope.ServiceProvider.GetRequiredService<AppDbContext>()));

        var built = EmailMimeMessageFactory.Create(EmailOutboxSenderJob.ToEmailMessage(row), "no-reply@siriupskill.test", "SIRI UpSkill");

        Assert.True(built.IsSuccess);
        using var mime = built.Value;
        var mixed = Assert.IsType<Multipart>(mime.Body);
        var alternative = Assert.IsType<Multipart>(mixed[0]);
        var inline = Assert.IsType<TextPart>(alternative[1]);
        Assert.Equal(method, inline.ContentType.Parameters["method"]);
        Assert.Equal(Ics, inline.Text);
        Assert.Equal("invite.ics", Assert.IsType<TextPart>(mixed[1]).FileName);
    }

    /// <summary>An <see cref="IEmailOutbox"/> written before the calendar overload existed — implements only the
    /// original method, exactly like the fakes already in the Commerce tests.</summary>
    private sealed class LegacyOutbox : IEmailOutbox
    {
        public List<(string ToEmail, string Subject, string BodyHtml, string? TemplateKey)> Plain { get; } = [];

        public void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey) =>
            Plain.Add((toEmail, subject, bodyHtml, templateKey));
    }
}
