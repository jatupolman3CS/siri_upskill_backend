using FluentValidation;

namespace Siri.Modules.Catalog.Features.SetCourseDeliveryFormat;

public sealed class SetCourseDeliveryFormatCommandValidator : AbstractValidator<SetCourseDeliveryFormatCommand>
{
    public SetCourseDeliveryFormatCommandValidator()
    {
        RuleFor(c => c.DeliveryFormat).IsInEnum().WithMessage("Invalid delivery format.");
    }
}
