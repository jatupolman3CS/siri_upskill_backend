using FluentValidation;

namespace Siri.Modules.Identity.Features.GoogleLogin;

public sealed class GoogleLoginValidator : AbstractValidator<GoogleLoginCommand>
{
    private const int MaxDeviceFieldLength = 200; // matches UserSessions.DeviceId/DeviceName column width
    private const int MaxIdTokenLength = 4096;

    public GoogleLoginValidator()
    {
        RuleFor(c => c.IdToken).NotEmpty().MaximumLength(MaxIdTokenLength);
        RuleFor(c => c.DeviceId).MaximumLength(MaxDeviceFieldLength);
        RuleFor(c => c.DeviceName).MaximumLength(MaxDeviceFieldLength);
    }
}
