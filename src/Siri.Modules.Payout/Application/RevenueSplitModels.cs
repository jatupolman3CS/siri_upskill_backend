using FluentValidation;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>Request payload for POST /api/payout/admin/revenue-splits. Binds from the JSON request body.
/// Records/DTOs in this module are NOT uppercased — see <see cref="IRevenueSplitRepository"/>'s own doc
/// comment for the naming-exception reasoning (D-17's UPPERCASE convention is entity classes/properties
/// and DB tables/columns only).</summary>
public sealed record CreateRevenueSplitCommand(
    Guid OrderItemId,
    Guid InstructorId,
    decimal GrossAmount,
    decimal PaymentFeeAmount,
    decimal PlatformFeeAmount,
    decimal InstructorAmount,
    string PeriodKey);

public sealed record RevenueSplitResponse(
    Guid Id,
    Guid OrderItemId,
    Guid InstructorId,
    decimal GrossAmount,
    decimal PaymentFeeAmount,
    decimal PlatformFeeAmount,
    decimal InstructorAmount,
    string PeriodKey,
    RevenueSplitStatus Status,
    DateTime CreatedAtUtc);

/// <summary>Format-only checks — no DB access (mirrors Catalog's <c>CreateCategoryValidator</c>'s own doc
/// comment: format here, existence/uniqueness/business rules in the service).</summary>
public sealed class CreateRevenueSplitValidator : AbstractValidator<CreateRevenueSplitCommand>
{
    /// <summary><c>'YYYY-MM'</c> — matches <c>REVENUE_SPLIT.PERIOD_KEY</c>'s <c>char(7)</c> column
    /// (<c>RevenueSplitConfiguration</c>).</summary>
    private const string PeriodKeyPattern = @"^\d{4}-(0[1-9]|1[0-2])$";

    public CreateRevenueSplitValidator()
    {
        RuleFor(c => c.OrderItemId).NotEmpty();
        RuleFor(c => c.InstructorId).NotEmpty();
        RuleFor(c => c.GrossAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.PaymentFeeAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.PlatformFeeAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.InstructorAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.PeriodKey)
            .NotEmpty()
            .Matches(PeriodKeyPattern)
                .WithMessage("PeriodKey ต้องอยู่ในรูปแบบ YYYY-MM (เช่น 2026-08)");
    }
}
