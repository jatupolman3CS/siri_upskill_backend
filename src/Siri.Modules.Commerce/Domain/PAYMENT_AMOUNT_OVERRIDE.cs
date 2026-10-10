using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// One admin decision about the "amount override": while the latest entry has <see cref="IS_ENABLED"/> set,
/// every NEW PaymentIntent is created for <see cref="OVERRIDE_AMOUNT"/> instead of the order total — used to
/// smoke-test the real money flow in production with a few baht. The override can only LOWER the charge
/// (<see cref="Resolve"/> never returns more than the order total), so a mistyped value can never over-charge
/// a learner.
/// <para>
/// Append-only by design: this table is both the configuration and its audit trail (docs/security.md —
/// every money-affecting change needs an audit that cannot be edited afterwards). The CURRENT setting is the
/// newest row (<see cref="CHANGED_AT_UTC"/>, ties broken by id); toggling or changing the amount inserts a
/// new row, nothing is ever updated or deleted. There is deliberately no mutator and no delete method.
/// </para>
/// <para>
/// Naming: SCREAMING_SNAKE_CASE class/properties, DB table/columns — same module-wide convention as
/// <see cref="PAYMENT"/> (docs/DECISIONS.md D-17).
/// </para>
/// </summary>
public sealed class PAYMENT_AMOUNT_OVERRIDE
{
    /// <summary>Stripe's PromptPay ceiling is 2,000,000 THB per charge (docs/PAYMENT.md); an override above it
    /// could never be honoured anyway.</summary>
    public const decimal MaxOverrideAmount = 2_000_000m;

    public const int MaxReasonLength = 500;

    private PAYMENT_AMOUNT_OVERRIDE()
    {
    }

    public Guid PAYMENT_AMOUNT_OVERRIDE_ID { get; private set; }

    public bool IS_ENABLED { get; private set; }

    /// <summary>THB, 2 decimals. Always stored, even on a "disable" entry, so the admin form can be prefilled
    /// with the last used value.</summary>
    public decimal OVERRIDE_AMOUNT { get; private set; }

    public string REASON { get; private set; } = string.Empty;

    /// <summary>Conceptual FK to <c>identity.Users.Id</c> — cross-module/schema, never a real DB FK
    /// (same reasoning as <see cref="ORDER.USER_ID"/>).</summary>
    public Guid CHANGED_BY_USER_ID { get; private set; }

    public DateTime CHANGED_AT_UTC { get; private set; }

    public static PAYMENT_AMOUNT_OVERRIDE Create(
        bool isEnabled,
        decimal overrideAmount,
        string reason,
        Guid changedByUserId,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (changedByUserId == Guid.Empty)
        {
            throw new ArgumentException("changedByUserId is required.", nameof(changedByUserId));
        }

        if (overrideAmount <= 0m || overrideAmount > MaxOverrideAmount || decimal.Round(overrideAmount, 2) != overrideAmount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(overrideAmount),
                overrideAmount,
                $"overrideAmount must be greater than 0, at most {MaxOverrideAmount:N0} and have at most 2 decimals.");
        }

        var trimmedReason = reason.Trim();
        if (trimmedReason.Length > MaxReasonLength)
        {
            throw new ArgumentException($"reason must be at most {MaxReasonLength} characters.", nameof(reason));
        }

        return new PAYMENT_AMOUNT_OVERRIDE
        {
            PAYMENT_AMOUNT_OVERRIDE_ID = UuidV7.NewId(),
            IS_ENABLED = isEnabled,
            OVERRIDE_AMOUNT = overrideAmount,
            REASON = trimmedReason,
            CHANGED_BY_USER_ID = changedByUserId,
            CHANGED_AT_UTC = clock.UtcNow,
        };
    }

    /// <summary>
    /// The amount to actually send to Stripe for an order worth <paramref name="orderTotal"/>.
    /// <paramref name="current"/> is the newest entry (or <c>null</c> when no override was ever configured).
    /// Not enabled → the order total, untouched. Enabled → the smaller of the override and the order total
    /// (an override never raises the charge; free orders stay free).
    /// </summary>
    public static decimal Resolve(PAYMENT_AMOUNT_OVERRIDE? current, decimal orderTotal)
    {
        if (current is null || !current.IS_ENABLED || orderTotal <= 0m)
        {
            return orderTotal;
        }

        return Math.Min(current.OVERRIDE_AMOUNT, orderTotal);
    }
}
