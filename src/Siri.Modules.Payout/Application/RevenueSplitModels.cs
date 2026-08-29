using FluentValidation;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>Request payload for POST /api/payout/admin/revenue-splits. Binds from the JSON request body.</summary>
public sealed record CreateRevenueSplitCommand(
    Guid OrderItemId,
    Guid InstructorId,
    decimal GrossAmount,
    decimal PaymentFeeAmount,
    decimal PlatformFeeAmount,
    decimal InstructorAmount,
    decimal RevenueSharePercent,
    string PeriodKey);

public sealed record RevenueSplitResponse(
    Guid Id,
    Guid OrderItemId,
    Guid InstructorId,
    decimal GrossAmount,
    decimal PaymentFeeAmount,
    decimal PlatformFeeAmount,
    decimal InstructorAmount,
    decimal RevenueSharePercent,
    string PeriodKey,
    RevenueSplitStatus Status,
    DateTime CreatedAtUtc);

/// <summary>Format-only checks — no DB access.</summary>
public sealed class CreateRevenueSplitValidator : AbstractValidator<CreateRevenueSplitCommand>
{
    private const string PeriodKeyPattern = @"^\d{4}-(0[1-9]|1[0-2])$";

    public CreateRevenueSplitValidator()
    {
        RuleFor(c => c.OrderItemId).NotEmpty();
        RuleFor(c => c.InstructorId).NotEmpty();
        RuleFor(c => c.GrossAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.PaymentFeeAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.PlatformFeeAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.InstructorAmount).GreaterThanOrEqualTo(0);
        RuleFor(c => c.RevenueSharePercent).InclusiveBetween(0, 100);
        RuleFor(c => c.PeriodKey)
            .NotEmpty()
            .Matches(PeriodKeyPattern)
                .WithMessage("PeriodKey ต้องอยู่ในรูปแบบ YYYY-MM (เช่น 2026-08)");
    }
}
