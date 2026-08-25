using FluentValidation;

namespace Siri.Modules.Cms.Application;

/// <summary>Format-only checks — no DB access. See <see cref="CreateBannerValidator"/>'s own doc comment
/// for why validators are real code in this scaffold pass, unlike the rest of this module's stubbed
/// behavior.</summary>
public sealed class UpdateMenuItemValidator : AbstractValidator<UpdateMenuItemCommand>
{
    public UpdateMenuItemValidator()
    {
        RuleFor(c => c.Label).NotEmpty().MaximumLength(CreateMenuItemValidator.MaxLabelLength);
        RuleFor(c => c.Url).NotEmpty().MaximumLength(CreateMenuItemValidator.MaxUrlLength);
    }
}
