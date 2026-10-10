using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Real-PostgreSQL checks for the admin amount override: the migration's table/column really exist and the
/// append-only "newest row wins" read and the nullable <c>PAYMENTS.ORIGINAL_AMOUNT</c> behave against the real
/// provider (ordering, precision, nullability) — things the in-memory unit fakes cannot prove.
/// </summary>
public sealed class PaymentAmountOverrideIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private sealed class StepClock(DateTime start) : IClock
    {
        private DateTime _now = start;

        public DateTime UtcNow => _now;

        public void Advance() => _now = _now.AddMinutes(1);
    }

    [Fact]
    public async Task Repository_GetCurrentReturnsNewestEntry_AndHistoryIsNewestFirstAndAppendOnly()
    {
        var clock = new StepClock(new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc));
        var admin = Guid.NewGuid();

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var repo = new PaymentAmountOverrideRepository(db);

            Assert.Null(await repo.GetCurrentAsync(CancellationToken.None)); // nothing configured yet

            await repo.AddAsync(PAYMENT_AMOUNT_OVERRIDE.Create(true, 20m, "first", admin, clock), CancellationToken.None);
            clock.Advance();
            await repo.AddAsync(PAYMENT_AMOUNT_OVERRIDE.Create(false, 20m, "second", admin, clock), CancellationToken.None);
            clock.Advance();
            await repo.AddAsync(PAYMENT_AMOUNT_OVERRIDE.Create(true, 35.50m, "third", admin, clock), CancellationToken.None);
        }

        using (var scope = fixture.CreateScope())
        {
            var repo = new PaymentAmountOverrideRepository(scope.ServiceProvider.GetRequiredService<AppDbContext>());

            var current = await repo.GetCurrentAsync(CancellationToken.None);
            Assert.NotNull(current);
            Assert.Equal("third", current.REASON);
            Assert.True(current.IS_ENABLED);
            Assert.Equal(35.50m, current.OVERRIDE_AMOUNT);
            Assert.Equal(admin, current.CHANGED_BY_USER_ID);

            Assert.Equal(3, await repo.CountAsync(CancellationToken.None));
            var firstPage = await repo.ListAsync(1, 2, CancellationToken.None);
            Assert.Equal(["third", "second"], firstPage.Select(e => e.REASON).ToArray());
            var secondPage = await repo.ListAsync(2, 2, CancellationToken.None);
            Assert.Equal(["first"], secondPage.Select(e => e.REASON).ToArray());
        }
    }

    [Fact]
    public async Task Payment_OriginalAmountRoundTrips_AndIsNullForNormalPayments()
    {
        var clock = new StepClock(new DateTime(2026, 10, 10, 9, 0, 0, DateTimeKind.Utc));
        var overridden = $"pi_{Guid.NewGuid():N}";
        var normal = $"pi_{Guid.NewGuid():N}";
        Guid orderId;

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = ORDER.Create($"OV-{Guid.NewGuid():N}"[..20], Guid.NewGuid(), 1890m, 0m, 0m, 1890m, null);
            db.Orders().Add(order);
            await db.SaveChangesAsync();
            orderId = order.ORDER_ID;

            var repo = new PaymentRepository(db);
            await repo.AddAsync(PAYMENT.Create(orderId, PaymentMethod.PromptPay, overridden, 20m, clock, originalAmount: 1890m), CancellationToken.None);
            await repo.AddAsync(PAYMENT.Create(orderId, PaymentMethod.PromptPay, normal, 1890m, clock), CancellationToken.None);
        }

        using (var scope = fixture.CreateScope())
        {
            var repo = new PaymentRepository(scope.ServiceProvider.GetRequiredService<AppDbContext>());

            var loadedOverridden = await repo.GetByProviderPaymentIntentIdAsync(overridden, CancellationToken.None);
            Assert.NotNull(loadedOverridden);
            Assert.Equal(20m, loadedOverridden.AMOUNT);
            Assert.Equal(1890m, loadedOverridden.ORIGINAL_AMOUNT);
            Assert.True(loadedOverridden.IsAmountOverridden);

            var loadedNormal = await repo.GetByProviderPaymentIntentIdAsync(normal, CancellationToken.None);
            Assert.NotNull(loadedNormal);
            Assert.Equal(1890m, loadedNormal.AMOUNT);
            Assert.Null(loadedNormal.ORIGINAL_AMOUNT);
            Assert.False(loadedNormal.IsAmountOverridden);
        }
    }
}
