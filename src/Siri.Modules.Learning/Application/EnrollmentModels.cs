using FluentValidation;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>Request payload for POST /api/learning/admin/enrollments (ops/backfill/gift — see
/// <c>EnrollmentEndpoints</c>'s own doc comment for why this is admin-facing rather than something a
/// learner calls directly). Records/DTOs in this module are NOT uppercased — see
/// <see cref="IEnrollmentRepository"/>'s own doc comment for the naming-exception reasoning (D-17's
/// UPPERCASE convention is entity classes/properties and DB tables/columns only).</summary>
public sealed record CreateEnrollmentCommand(
    Guid UserId,
    Guid CourseId,
    Guid? OrderId,
    EnrollmentSource Source,
    DateTime? ExpiresAtUtc);

/// <summary>Request payload for PUT /api/learning/enrollments/{id}/progress — the only field a learner can
/// write themselves; everything else about an enrollment is either server-computed or admin-controlled.
/// </summary>
public sealed record UpdateEnrollmentProgressCommand(decimal ProgressPercent);

public sealed record EnrollmentResponse(
    Guid Id,
    Guid UserId,
    Guid CourseId,
    Guid? OrderId,
    EnrollmentSource Source,
    DateTime EnrolledAtUtc,
    DateTime? ExpiresAtUtc,
    EnrollmentStatus Status,
    decimal ProgressPercent,
    DateTime? CompletedAtUtc,
    DateTime? LastAccessedAtUtc);

/// <summary>Format-only checks — no DB access (mirrors
/// <c>Siri.Modules.Payout.Application.CreateRevenueSplitValidator</c>'s own doc comment: format here,
/// existence/uniqueness/business rules in the service). No future-date check on
/// <see cref="CreateEnrollmentCommand.ExpiresAtUtc"/> here deliberately — that is a relative-to-now check,
/// and validators in this codebase are registered without an <c>IClock</c> (format-only, no DB/clock
/// access) — a real implementation should do that comparison in <see cref="EnrollmentService"/>, which
/// does get a real clock once implemented. <see cref="CreateEnrollmentCommand.Source"/> only gets a format
/// check (<c>IsInEnum</c>) — nothing here special-cases <see cref="EnrollmentSource.Corporate"/>; it is a
/// structurally valid enum value today even though no real workflow produces it yet (see that enum's own
/// doc comment).</summary>
public sealed class CreateEnrollmentValidator : AbstractValidator<CreateEnrollmentCommand>
{
    public CreateEnrollmentValidator()
    {
        RuleFor(c => c.UserId).NotEmpty();
        RuleFor(c => c.CourseId).NotEmpty();
        RuleFor(c => c.Source).IsInEnum();
    }
}

public sealed class UpdateEnrollmentProgressValidator : AbstractValidator<UpdateEnrollmentProgressCommand>
{
    public UpdateEnrollmentProgressValidator()
    {
        RuleFor(c => c.ProgressPercent).InclusiveBetween(0, 100);
    }
}
