using FluentValidation;

namespace Siri.Modules.Catalog.Features.ApplyAsInstructor;

/// <summary>
/// Format-only checks — no DB access (mirrors <c>CreateCategoryValidator</c>'s own doc comment: format
/// here, uniqueness/existence/business rules in the handler).
/// </summary>
public sealed class ApplyAsInstructorValidator : AbstractValidator<ApplyAsInstructorCommand>
{
    public const int MaxDisplayNameLength = 200; // matches InstructorProfiles.DisplayName's column width
    public const int MaxHeadlineLength = 200; // matches InstructorProfiles.Headline's column width
    public const int MaxBioLength = 2000; // matches InstructorProfiles.Bio's column width

    public ApplyAsInstructorValidator()
    {
        RuleFor(c => c.DisplayName).NotEmpty().MaximumLength(MaxDisplayNameLength);
        RuleFor(c => c.Headline).MaximumLength(MaxHeadlineLength);
        RuleFor(c => c.Bio).NotEmpty().MaximumLength(MaxBioLength);
    }
}
