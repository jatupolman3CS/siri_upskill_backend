using FluentValidation;

namespace Siri.Modules.Media.Application;

/// <summary>Format-only checks — no DB access (mirrors Catalog's own validator convention, e.g.
/// <c>CreateCourseValidator</c>'s own doc comment: format here, existence/uniqueness/business rules in the
/// service).</summary>
public sealed class CreateMediaAssetValidator : AbstractValidator<CreateMediaAssetCommand>
{
    public const int MaxTitleLength = 200;

    public CreateMediaAssetValidator()
    {
        RuleFor(c => c.Title).NotEmpty().MaximumLength(MaxTitleLength);
    }
}

/// <summary>Format-only checks, same reasoning as <see cref="CreateMediaAssetValidator"/>. Deliberately no
/// cross-field rule like "DurationSeconds required when Status is Ready" — that is exactly the kind of
/// business rule this scaffold pass leaves for <see cref="MediaAssetService.UpdateStatusAsync"/> to enforce
/// once de-stubbed.</summary>
public sealed class UpdateMediaAssetStatusValidator : AbstractValidator<UpdateMediaAssetStatusCommand>
{
    public UpdateMediaAssetStatusValidator()
    {
        RuleFor(c => c.Status).IsInEnum();
        RuleFor(c => c.PlaybackId).MaximumLength(200);
        RuleFor(c => c.DurationSeconds).GreaterThan(0).When(c => c.DurationSeconds.HasValue);
        RuleFor(c => c.ThumbnailUrl).MaximumLength(1000);
        RuleFor(c => c.ErrorMessage).MaximumLength(2000);
    }
}
