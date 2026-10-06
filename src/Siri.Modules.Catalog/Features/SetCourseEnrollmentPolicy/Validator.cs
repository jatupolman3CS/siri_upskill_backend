using FluentValidation;

namespace Siri.Modules.Catalog.Features.SetCourseEnrollmentPolicy;

public sealed class SetCourseEnrollmentPolicyCommandValidator : AbstractValidator<SetCourseEnrollmentPolicyCommand>
{
    public SetCourseEnrollmentPolicyCommandValidator()
    {
        // Kind (UTC) is validated at the Handler level, not here — same precedent as
        // CreateLiveSession/UpdateLiveSession's DateTimeKind.Utc check (docs/contracts/
        // P11-11-enrollment-deadline-seat-cap.md §0).
        RuleFor(c => c.MaxSeats).GreaterThan(0).When(c => c.MaxSeats.HasValue)
            .WithMessage("maxSeats must be a positive integer.");
    }
}
