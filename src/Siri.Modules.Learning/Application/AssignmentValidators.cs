using FluentValidation;

namespace Siri.Modules.Learning.Application;

/// <summary>Format-only checks — see <see cref="CreateQuizRequestValidator"/>'s own doc comment.</summary>
public sealed class CreateAssignmentRequestValidator : AbstractValidator<CreateAssignmentRequest>
{
    public const int MaxTitleLength = 200; // matches ASSIGNMENTS.TITLE's column width (AssignmentConfiguration)
    public const int MaxInstructionsLength = 4000; // matches ASSIGNMENTS.INSTRUCTIONS's column width
    public const int MaxAllowedExtensionsLength = 500; // matches ASSIGNMENTS.ALLOWED_EXTENSIONS's column width

    public CreateAssignmentRequestValidator()
    {
        RuleFor(r => r.EpisodeId).NotEmpty();
        RuleFor(r => r.Title).NotEmpty().MaximumLength(MaxTitleLength);
        RuleFor(r => r.Instructions).NotEmpty().MaximumLength(MaxInstructionsLength);
        RuleFor(r => r.DueDays).GreaterThan(0).When(r => r.DueDays is not null);
        RuleFor(r => r.MaxFileSizeMb).GreaterThan(0);
        RuleFor(r => r.AllowedExtensions).NotEmpty().MaximumLength(MaxAllowedExtensionsLength);
    }
}

/// <summary>Format-only checks — see <see cref="CreateQuizRequestValidator"/>'s own doc comment. Mirrors
/// <see cref="CreateAssignmentRequestValidator"/>'s rules minus <c>EpisodeId</c> (not part of an update).</summary>
public sealed class UpdateAssignmentRequestValidator : AbstractValidator<UpdateAssignmentRequest>
{
    public UpdateAssignmentRequestValidator()
    {
        RuleFor(r => r.Title).NotEmpty().MaximumLength(CreateAssignmentRequestValidator.MaxTitleLength);
        RuleFor(r => r.Instructions).NotEmpty().MaximumLength(CreateAssignmentRequestValidator.MaxInstructionsLength);
        RuleFor(r => r.DueDays).GreaterThan(0).When(r => r.DueDays is not null);
        RuleFor(r => r.MaxFileSizeMb).GreaterThan(0);
        RuleFor(r => r.AllowedExtensions).NotEmpty().MaximumLength(CreateAssignmentRequestValidator.MaxAllowedExtensionsLength);
    }
}
