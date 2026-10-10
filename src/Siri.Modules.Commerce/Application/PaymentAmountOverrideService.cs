using Microsoft.Extensions.Logging;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// Admin-facing read/write of the payment amount override (see <see cref="PAYMENT_AMOUNT_OVERRIDE"/>).
/// No <c>I…Service</c> interface — same reasoning as <c>OrderService</c>'s doc comment. The caller (controller)
/// has already enforced <c>AdminOnly</c> and supplies the acting admin's id from <c>IUserContext</c>; this
/// service never takes a user id from a request body.
/// </summary>
public sealed class PaymentAmountOverrideService(
    IPaymentAmountOverrideRepository repository,
    IClock clock,
    ILogger<PaymentAmountOverrideService> logger)
{
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    public async Task<PaymentAmountOverrideState> GetCurrentAsync(CancellationToken cancellationToken)
    {
        var current = await repository.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        return ToState(current);
    }

    public async Task<Result<PaymentAmountOverrideState>> SetAsync(
        Guid adminUserId,
        SetPaymentAmountOverrideCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var entry = PAYMENT_AMOUNT_OVERRIDE.Create(
            command.IsEnabled,
            command.OverrideAmount,
            command.Reason,
            adminUserId,
            clock);

        await repository.AddAsync(entry, cancellationToken).ConfigureAwait(false);

        // Money-affecting admin action: always leave an operational trail next to the audit row. The reason is
        // free text typed by an admin, so it stays out of the log line (the audit row has it).
        logger.LogWarning(
            "Payment amount override {State} by admin {AdminUserId}: amount {OverrideAmount} THB (entry {EntryId}).",
            entry.IS_ENABLED ? "ENABLED" : "DISABLED",
            adminUserId,
            entry.OVERRIDE_AMOUNT,
            entry.PAYMENT_AMOUNT_OVERRIDE_ID);

        return ToState(entry);
    }

    public async Task<PagedResult<PaymentAmountOverrideEntry>> ListHistoryAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePage = page <= 0 ? 1 : page;
        var effectivePageSize = pageSize <= 0 ? DefaultPageSize : Math.Min(pageSize, MaxPageSize);

        var total = await repository.CountAsync(cancellationToken).ConfigureAwait(false);
        var rows = await repository.ListAsync(effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);

        return PagedResult<PaymentAmountOverrideEntry>.Create(
            rows.Select(ToEntry).ToList(),
            total,
            effectivePage,
            effectivePageSize);
    }

    private static PaymentAmountOverrideState ToState(PAYMENT_AMOUNT_OVERRIDE? entry) =>
        entry is null
            ? new PaymentAmountOverrideState(false, null, null, null, null)
            : new PaymentAmountOverrideState(entry.IS_ENABLED, entry.OVERRIDE_AMOUNT, entry.REASON, entry.CHANGED_BY_USER_ID, entry.CHANGED_AT_UTC);

    private static PaymentAmountOverrideEntry ToEntry(PAYMENT_AMOUNT_OVERRIDE entry) =>
        new(entry.PAYMENT_AMOUNT_OVERRIDE_ID, entry.IS_ENABLED, entry.OVERRIDE_AMOUNT, entry.REASON, entry.CHANGED_BY_USER_ID, entry.CHANGED_AT_UTC);
}

/// <summary>The setting currently in force. Before any change was ever made: disabled, everything else null.</summary>
public sealed record PaymentAmountOverrideState(
    bool IsEnabled,
    decimal? OverrideAmount,
    string? Reason,
    Guid? ChangedByUserId,
    DateTime? ChangedAtUtc);

public sealed record PaymentAmountOverrideEntry(
    Guid Id,
    bool IsEnabled,
    decimal OverrideAmount,
    string Reason,
    Guid ChangedByUserId,
    DateTime ChangedAtUtc);

public sealed record SetPaymentAmountOverrideCommand(bool IsEnabled, decimal OverrideAmount, string Reason);
