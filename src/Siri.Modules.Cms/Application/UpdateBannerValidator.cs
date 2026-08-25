using FluentValidation;

namespace Siri.Modules.Cms.Application;

/// <summary>Format-only checks — no DB access. See <see cref="CreateBannerValidator"/>'s own doc comment
/// for why validators are real code in this scaffold pass, unlike the rest of this module.</summary>
public sealed class UpdateBannerValidator : AbstractValidator<UpdateBannerCommand>
{
    public UpdateBannerValidator()
    {
        RuleFor(c => c.Placement).NotEmpty().MaximumLength(CreateBannerValidator.MaxPlacementLength);
        RuleFor(c => c.ImageUrl).NotEmpty().MaximumLength(CreateBannerValidator.MaxUrlLength);
        RuleFor(c => c.MobileImageUrl).MaximumLength(CreateBannerValidator.MaxUrlLength);
        RuleFor(c => c.LinkUrl).MaximumLength(CreateBannerValidator.MaxUrlLength);
        RuleFor(c => c.Title).NotEmpty().MaximumLength(CreateBannerValidator.MaxTitleLength);

        RuleFor(c => c.EndsAtUtc)
            .GreaterThan(c => c.StartsAtUtc!.Value)
                .WithMessage("วันที่สิ้นสุดต้องอยู่หลังวันที่เริ่มต้น")
            .When(c => c.StartsAtUtc is not null && c.EndsAtUtc is not null);
    }
}
