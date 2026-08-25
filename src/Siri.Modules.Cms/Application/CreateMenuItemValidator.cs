using FluentValidation;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Format-only checks — no DB access (mirrors <see cref="CreateBannerValidator"/>'s own doc comment: format
/// here, existence/business rules — e.g. whether <see cref="CreateMenuItemCommand.ParentId"/> actually
/// exists — in the service). Unlike the rest of this module's stubbed behavior, validators are real,
/// working code in this scaffold pass.
/// </summary>
public sealed class CreateMenuItemValidator : AbstractValidator<CreateMenuItemCommand>
{
    public const int MaxLabelLength = 100; // matches MENU_ITEMS.LABEL's column width (MenuItemConfiguration)
    public const int MaxUrlLength = 1000; // matches MENU_ITEMS.URL's column width

    public CreateMenuItemValidator()
    {
        RuleFor(c => c.Label).NotEmpty().MaximumLength(MaxLabelLength);
        RuleFor(c => c.Url).NotEmpty().MaximumLength(MaxUrlLength);
    }
}
