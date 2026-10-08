using FluentValidation;

namespace Siri.Modules.Catalog.Features.SetCourseLiveSettings;

public sealed class SetCourseLiveSettingsCommandValidator : AbstractValidator<SetCourseLiveSettingsCommand>
{
    public SetCourseLiveSettingsCommandValidator()
    {
        RuleFor(c => c.GoogleAttendeeSyncEnabled)
            .NotNull()
            .WithMessage("googleAttendeeSyncEnabled is required (true or false).");
    }
}
