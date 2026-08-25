using FluentValidation;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Format-only checks — no DB access (mirrors
/// <c>Siri.Modules.Catalog.Features.CreateCategory.CreateCategoryValidator</c>'s own doc comment: "format
/// here, uniqueness/existence/business rules in the handler/service"). Unlike the rest of this module's
/// stubbed behavior, validators are real, working code in this scaffold pass — they are pure format checks
/// with no persistence/business-logic dependency, so there is nothing here that is "later task" work the
/// way <c>Application.BannerService</c>'s bodies are.
/// </summary>
public sealed class CreateBannerValidator : AbstractValidator<CreateBannerCommand>
{
    public const int MaxPlacementLength = 100; // matches BANNERS.PLACEMENT's column width (BannerConfiguration)
    public const int MaxUrlLength = 1000; // matches BANNERS.IMAGE_URL/MOBILE_IMAGE_URL/LINK_URL's column width
    public const int MaxTitleLength = 200; // matches BANNERS.TITLE's column width

    public CreateBannerValidator()
    {
        RuleFor(c => c.Placement).NotEmpty().MaximumLength(MaxPlacementLength);
        RuleFor(c => c.ImageUrl).NotEmpty().MaximumLength(MaxUrlLength);
        RuleFor(c => c.MobileImageUrl).MaximumLength(MaxUrlLength);
        RuleFor(c => c.LinkUrl).MaximumLength(MaxUrlLength);
        RuleFor(c => c.Title).NotEmpty().MaximumLength(MaxTitleLength);

        RuleFor(c => c.EndsAtUtc)
            .GreaterThan(c => c.StartsAtUtc!.Value)
                .WithMessage("วันที่สิ้นสุดต้องอยู่หลังวันที่เริ่มต้น")
            .When(c => c.StartsAtUtc is not null && c.EndsAtUtc is not null);
    }
}
