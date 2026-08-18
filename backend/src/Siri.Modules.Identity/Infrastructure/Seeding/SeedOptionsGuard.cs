using Siri.Modules.Identity.Features.Register;

namespace Siri.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// Validates <see cref="SeedOptions"/>'s two password fields before <see cref="IdentitySeeder"/> is
/// allowed to touch the database. Split out as a pure, side-effect-free function (no DI, no
/// database) specifically so this — the one part of seeding with any real logic worth a unit test —
/// stays unit-testable on its own (backend.md: "Unit test: domain logic ... บังคับ").
/// <para>
/// Rejects two cases, each with the same descriptive-<see cref="InvalidOperationException"/> shape
/// as this codebase's other "missing required config" guard clauses
/// (<c>PersistenceServiceCollectionExtensions</c>/<c>WorkersServiceCollectionExtensions</c>'
/// <c>ConnectionStrings:Default</c> checks): the password is still the unmodified
/// <see cref="SeedOptions.PlaceholderPassword"/> placeholder, or it is shorter than real registration
/// would ever accept (<see cref="RegisterValidator.MinPasswordLength"/>) — reusing that exact
/// constant rather than a second hardcoded number, so the two floors can never silently drift apart.
/// </para>
/// </summary>
public static class SeedOptionsGuard
{
    public static void EnsureRealPasswordsConfigured(SeedOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        CheckPassword(options.AdminPassword, $"{SeedOptions.SectionName}:AdminPassword");
        CheckPassword(options.TestUserPassword, $"{SeedOptions.SectionName}:TestUserPassword");
    }

    private static void CheckPassword(string password, string configKey)
    {
        if (password == SeedOptions.PlaceholderPassword)
        {
            throw new InvalidOperationException(
                $"Missing '{configKey}'. The appsettings placeholder ('{SeedOptions.PlaceholderPassword}') is deliberately not a real password — " +
                $"set it via `dotnet user-secrets set \"{configKey}\" \"<a real password>\"` (run from backend/src/Siri.Api) before running --seed. " +
                "See CLAUDE.md's seed/bootstrap section.");
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < RegisterValidator.MinPasswordLength)
        {
            throw new InvalidOperationException(
                $"'{configKey}' must be at least {RegisterValidator.MinPasswordLength} characters — the same floor real registration enforces " +
                $"(see {nameof(RegisterValidator)}.{nameof(RegisterValidator.MinPasswordLength)}).");
        }
    }
}
