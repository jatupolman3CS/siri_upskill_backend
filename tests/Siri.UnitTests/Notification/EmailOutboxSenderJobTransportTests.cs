using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Email;
using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Infrastructure;
using Siri.Modules.Notification.Infrastructure.Delivery;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.UnitTests.Notification;

/// <summary>
/// With the Kafka transport on, the Hangfire <c>email-outbox-send</c> job must do nothing — if it also sent mail, every email would go
/// out twice. The proof is behavioural: the job is given a database that cannot be reached, so any query would throw.
/// </summary>
public class EmailOutboxSenderJobTransportTests
{
    private sealed class RecordingSender : IEmailSender
    {
        public int Sends { get; private set; }

        public Task<Result> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Sends++;
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class UnusedRepository : IEmailOutboxRepository
    {
        public Task<IReadOnlyList<EMAIL_OUTBOX_MESSAGE>> GetPendingMessagesAsync(int batchSize, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<EMAIL_OUTBOX_MESSAGE?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task AddAsync(EMAIL_OUTBOX_MESSAGE message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task UpdateAsync(EMAIL_OUTBOX_MESSAGE message, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class PassThroughGuard : IEmailDeliveryGuard
    {
        public Task<EmailClaimTicket> TryClaimAsync(Guid messageId, CancellationToken cancellationToken) =>
            Task.FromResult(new EmailClaimTicket(DeliveryClaim.Claimed, "token"));

        public Task MarkDeliveredAsync(Guid messageId, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task ReleaseAsync(Guid messageId, string? token, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class NoThrottle : IEmailSendThrottle
    {
        public Task WaitForSlotAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static (EmailOutboxSenderJob Job, RecordingSender Sender, IDisposable Scope) Build(NotificationTransport transport)
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql("Host=127.0.0.1;Port=1;Database=unit-test;Username=none;Password=none;Timeout=2"));
        var provider = services.BuildServiceProvider();
        var scope = provider.CreateScope();

        var clock = new FakeClock(new DateTime(2026, 10, 9, 3, 0, 0, DateTimeKind.Utc));
        var sender = new RecordingSender();
        var handler = new EmailDeliveryHandler(
            new UnusedRepository(), sender, new PassThroughGuard(), new NoThrottle(), clock, NullLogger<EmailDeliveryHandler>.Instance);

        var job = new EmailOutboxSenderJob(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            handler,
            clock,
            Options.Create(new NotificationDeliveryOptions { Transport = transport }),
            NullLogger<EmailOutboxSenderJob>.Instance);

        return (job, sender, scope);
    }

    [Fact]
    public async Task RunAsync_KafkaTransport_ReturnsWithoutQueryingTheDatabaseOrSending()
    {
        var (job, sender, scope) = Build(NotificationTransport.Kafka);
        using (scope)
        {
            await job.RunAsync(CancellationToken.None);

            Assert.Equal(0, sender.Sends);
        }
    }

    [Fact]
    public async Task RunAsync_DatabaseTransport_StillQueriesTheOutbox()
    {
        var (job, _, scope) = Build(NotificationTransport.Database);
        using (scope)
        {
            // The unreachable database makes the query fail — which is exactly the evidence that the job did not stand down.
            await Assert.ThrowsAnyAsync<Exception>(() => job.RunAsync(CancellationToken.None));
        }
    }
}
