using FluentValidation;

namespace Siri.Modules.Catalog.Features.AttachEpisodeMedia;

public sealed class AttachEpisodeMediaValidator : AbstractValidator<AttachEpisodeMediaCommand>
{
    public AttachEpisodeMediaValidator()
    {
        RuleFor(x => x.MediaAssetId)
            .NotEmpty().WithMessage("กรุณาระบุรหัสวิดีโอ (MediaAssetId)");

        RuleFor(x => x.DurationSeconds)
            .GreaterThan(0).When(x => x.DurationSeconds.HasValue)
            .WithMessage("ความยาววิดีโอต้องมากกว่า 0 วินาที");
    }
}
