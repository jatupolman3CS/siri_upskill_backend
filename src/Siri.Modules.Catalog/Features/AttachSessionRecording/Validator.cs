using FluentValidation;

namespace Siri.Modules.Catalog.Features.AttachSessionRecording;

/// <summary>
/// Format-only rules (no database): <c>MediaAssetId</c> XOR <c>EpisodeId</c>, <c>SectionId</c> only together with a media asset, a title of at most 200
/// characters once trimmed. Existence, ownership and state are the handler's job.
/// </summary>
public sealed class AttachSessionRecordingCommandValidator : AbstractValidator<AttachSessionRecordingCommand>
{
    public const int EpisodeTitleMaxLength = 200;

    public AttachSessionRecordingCommandValidator()
    {
        RuleFor(c => c.MediaAssetId)
            .NotNull().When(c => c.EpisodeId is null)
            .WithMessage("Provide either mediaAssetId or episodeId.");

        RuleFor(c => c.EpisodeId)
            .Null().When(c => c.MediaAssetId is not null)
            .WithMessage("mediaAssetId and episodeId are mutually exclusive — provide only one.");

        RuleFor(c => c.MediaAssetId)
            .NotEqual((Guid?)Guid.Empty)
            .WithMessage("mediaAssetId cannot be an empty id.");

        RuleFor(c => c.EpisodeId)
            .NotEqual((Guid?)Guid.Empty)
            .WithMessage("episodeId cannot be an empty id.");

        RuleFor(c => c.SectionId)
            .Null().When(c => c.EpisodeId is not null)
            .WithMessage("sectionId can only be used together with mediaAssetId.");

        RuleFor(c => c.SectionId)
            .NotEqual((Guid?)Guid.Empty)
            .WithMessage("sectionId cannot be an empty id.");

        RuleFor(c => c.EpisodeTitle)
            .Must(title => title is null || title.Trim().Length <= EpisodeTitleMaxLength)
            .WithMessage($"episodeTitle cannot exceed {EpisodeTitleMaxLength} characters.");
    }
}
