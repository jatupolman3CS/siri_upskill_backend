using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Notification;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.UnitTests.Notification;

/// <summary>
/// <see cref="IUserNotificationOutbox"/> (task P11-04, docs/contracts/P11-04-live-invites-ics-reminders.md §3.2): it
/// stages a <see cref="USER_NOTIFICATION"/> on the scoped context without saving, cuts title/body to the contract
/// limits, and refuses an unsafe link. Resolved through the module's real DI registration; no database is touched.
/// </summary>
public class UserNotificationOutboxTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    private static ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Email:Provider"] = "Log" })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FakeClock(Now));
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql("Host=localhost;Port=1;Database=unit-test;Username=none;Password=none"));
        services.AddNotificationModule(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceProvider provider = BuildProvider();

        private readonly IServiceScope scope;

        public Harness()
        {
            scope = provider.CreateScope();
            Outbox = scope.ServiceProvider.GetRequiredService<IUserNotificationOutbox>();
            Context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        }

        public IUserNotificationOutbox Outbox { get; }

        public AppDbContext Context { get; }

        public IReadOnlyList<USER_NOTIFICATION> Staged() =>
            Context.ChangeTracker.Entries<USER_NOTIFICATION>().Where(e => e.State == EntityState.Added).Select(e => e.Entity).ToList();

        public void Dispose()
        {
            scope.Dispose();
            provider.Dispose();
        }
    }

    [Fact]
    public void DependencyInjection_ResolvesTheOutboxAsScoped()
    {
        using var provider = BuildProvider();
        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();

        var a1 = scopeA.ServiceProvider.GetRequiredService<IUserNotificationOutbox>();
        var a2 = scopeA.ServiceProvider.GetRequiredService<IUserNotificationOutbox>();
        var b = scopeB.ServiceProvider.GetRequiredService<IUserNotificationOutbox>();

        Assert.Same(a1, a2);
        Assert.NotSame(a1, b);
    }

    [Fact]
    public void Stage_ValidNotification_IsStagedUnreadAndStampedFromTheClockWithoutSaving()
    {
        using var h = new Harness();
        var userId = Guid.CreateVersion7();

        h.Outbox.Stage(userId, "live.invite", "ยืนยันตารางเรียนสด", "คุณมีคาบเรียนสด 5 คาบ", "/learn/python-basics?tab=live");

        var n = Assert.Single(h.Staged());
        Assert.Equal(userId, n.UserId);
        Assert.Equal("live.invite", n.Type);
        Assert.Equal("ยืนยันตารางเรียนสด", n.Title);
        Assert.Equal("คุณมีคาบเรียนสด 5 คาบ", n.Body);
        Assert.Equal("/learn/python-basics?tab=live", n.LinkUrl);
        Assert.Null(n.ReadAtUtc);
        Assert.Equal(Now, n.CreatedAtUtc);
        Assert.NotEqual(Guid.Empty, n.Id);
    }

    [Fact]
    public void Stage_NoLink_IsAllowed()
    {
        using var h = new Harness();

        h.Outbox.Stage(Guid.CreateVersion7(), "live.cancelled", "ยกเลิก", "คาบเรียนถูกยกเลิก", null);
        h.Outbox.Stage(Guid.CreateVersion7(), "live.cancelled", "ยกเลิก", "คาบเรียนถูกยกเลิก", "   ");

        Assert.All(h.Staged(), n => Assert.Null(n.LinkUrl));
        Assert.Equal(2, h.Staged().Count);
    }

    [Fact]
    public void Stage_TitleAndBodyAtTheLimit_AreKeptAndOneOverIsCut()
    {
        using var h = new Harness();

        h.Outbox.Stage(Guid.CreateVersion7(), "live.invite", new string('ก', 200), new string('ข', 2000), null);
        h.Outbox.Stage(Guid.CreateVersion7(), "live.invite", new string('ก', 201), new string('ข', 2001), null);
        h.Outbox.Stage(Guid.CreateVersion7(), "live.invite", new string('ก', 5000), new string('ข', 50_000), null);

        Assert.All(h.Staged(), n =>
        {
            Assert.Equal(200, n.Title.Length);
            Assert.Equal(2000, n.Body.Length);
            Assert.Equal(new string('ก', 200), n.Title);
        });
    }

    [Fact]
    public void Stage_CutThatWouldSplitASurrogatePair_BacksOffSoTheTextStaysValid()
    {
        using var h = new Harness();
        var emoji = char.ConvertFromUtf32(0x1F393); // 2 UTF-16 code units
        var title = new string('a', 199) + emoji;   // the 200-char cut lands between the surrogates

        h.Outbox.Stage(Guid.CreateVersion7(), "live.invite", title, "body", null);

        var n = Assert.Single(h.Staged());
        Assert.Equal(new string('a', 199), n.Title);
        Assert.False(char.IsHighSurrogate(n.Title[^1]));
    }

    [Fact]
    public void Stage_TypeAtTheLimit_IsAcceptedAndOverIsRejected()
    {
        using var h = new Harness();

        h.Outbox.Stage(Guid.CreateVersion7(), new string('t', 64), "title", "body", null);
        Assert.Throws<ArgumentException>(() =>
            h.Outbox.Stage(Guid.CreateVersion7(), new string('t', 65), "title", "body", null));

        Assert.Single(h.Staged());
    }

    [Theory]
    [InlineData("", "title", "body")]
    [InlineData("  ", "title", "body")]
    [InlineData("live.invite", "", "body")]
    [InlineData("live.invite", "   ", "body")]
    [InlineData("live.invite", "title", "")]
    [InlineData("live.invite", "title", "  ")]
    public void Stage_BlankTypeTitleOrBody_Throws(string type, string title, string body)
    {
        using var h = new Harness();

        Assert.ThrowsAny<ArgumentException>(() => h.Outbox.Stage(Guid.CreateVersion7(), type, title, body, null));

        Assert.Empty(h.Staged());
    }

    [Fact]
    public void Stage_EmptyUserId_Throws()
    {
        using var h = new Harness();

        Assert.Throws<ArgumentException>(() => h.Outbox.Stage(Guid.Empty, "live.invite", "title", "body", null));

        Assert.Empty(h.Staged());
    }

    [Theory]
    [InlineData("/learn/python-basics?tab=live")]
    [InlineData("/instructor/sessions/0198a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b")]
    [InlineData("/")]
    [InlineData("https://siriupskill.test/learn/python-basics")]
    [InlineData("http://localhost:4202/learn/python-basics")]
    public void Stage_SafeLink_IsKept(string link)
    {
        using var h = new Harness();

        h.Outbox.Stage(Guid.CreateVersion7(), "live.invite", "title", "body", link);

        Assert.Equal(link, Assert.Single(h.Staged()).LinkUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JaVaScRiPt:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("//evil.example/phish")]
    [InlineData("/\\evil.example/phish")]
    [InlineData("learn/python-basics")]
    [InlineData("ftp://example.test/file")]
    [InlineData("/learn\r\nSet-Cookie: x=1")]
    [InlineData("/learn\talert")]
    [InlineData("not a url")]
    public void Stage_UnsafeLink_ThrowsAndStagesNothing(string link)
    {
        using var h = new Harness();

        Assert.Throws<ArgumentException>(() => h.Outbox.Stage(Guid.CreateVersion7(), "live.invite", "title", "body", link));

        Assert.Empty(h.Staged());
    }

    [Fact]
    public void Stage_LinkAtTheLimit_IsKeptAndOneOverIsRejected()
    {
        using var h = new Harness();
        var atLimit = "/" + new string('a', 999);

        h.Outbox.Stage(Guid.CreateVersion7(), "live.invite", "title", "body", atLimit);
        Assert.Throws<ArgumentException>(() =>
            h.Outbox.Stage(Guid.CreateVersion7(), "live.invite", "title", "body", atLimit + "a"));

        Assert.Equal(atLimit, Assert.Single(h.Staged()).LinkUrl);
    }
}
