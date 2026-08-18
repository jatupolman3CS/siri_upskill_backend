using FluentValidation;
using Siri.Modules.Identity.Features.Register;

namespace Siri.Modules.Identity.Features.Login;

/// <summary>
/// Validates <see cref="LoginCommand"/> — input *shape* only, never "does this account exist" or
/// "is this the right password" (those are the handler's job, and folding either into validation
/// would create a distinguishable-from-"just wrong" 400 response, defeating the anti-enumeration
/// behavior <see cref="LoginHandler"/> is specifically built to guarantee — see its own doc comment).
/// <para>
/// Deliberately does <b>not</b> enforce <see cref="RegisterValidator.MinPasswordLength"/> (or the
/// denylist) here — those are account-<em>creation</em> policy. Rejecting a login attempt for being
/// "too short" would only ever misfire against a genuine account whose password predates a policy
/// change, turning a correct password into a confusing validation error instead of just letting the
/// hash comparison naturally fail it. <see cref="RegisterValidator.MaxPasswordLength"/> is reused
/// as the max-length cap though — that one is a pure request-size/hashing-cost sanity bound, not a
/// creation-time policy, so it applies equally well here.
/// </para>
/// </summary>
public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    private const int MaxDeviceFieldLength = 200; // matches UserSessions.DeviceId/DeviceName column width (UserSessionConfiguration)

    public LoginValidator()
    {
        RuleFor(c => c.Email)
            .NotEmpty()
            .MaximumLength(256) // matches Users.Email's column width (UserConfiguration)
            .EmailAddress();

        RuleFor(c => c.Password)
            .NotEmpty()
            .MaximumLength(RegisterValidator.MaxPasswordLength);

        RuleFor(c => c.DeviceId).MaximumLength(MaxDeviceFieldLength);
        RuleFor(c => c.DeviceName).MaximumLength(MaxDeviceFieldLength);
    }
}
