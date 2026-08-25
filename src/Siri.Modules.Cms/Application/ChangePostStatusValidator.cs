using FluentValidation;

namespace Siri.Modules.Cms.Application;

/// <summary>Format-only check — no DB access. Only confirms <see cref="ChangePostStatusCommand.Status"/> is
/// a defined <see cref="Domain.PostStatus"/> member; which transitions are actually legal from the post's
/// current status is business logic for <see cref="PostService.ChangeStatusAsync"/>, not this validator —
/// see <see cref="ChangePostStatusCommand"/>'s own doc comment.</summary>
public sealed class ChangePostStatusValidator : AbstractValidator<ChangePostStatusCommand>
{
    public ChangePostStatusValidator()
    {
        RuleFor(c => c.Status).IsInEnum();
    }
}
