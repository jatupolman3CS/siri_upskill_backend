namespace Siri.Modules.Identity.Infrastructure.Seeding;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Identity:Seed") — the email/password
/// for the single seeded Admin account, plus one shared password for every seeded Learner/Instructor
/// test account (task P0-37, see <see cref="IdentitySeedData"/> for the actual account list).
/// <para>
/// Same Options-pattern shape as <see cref="JwtOptions"/>/<see cref="PasswordResetOptions"/>, but
/// deliberately registered <b>without</b> <c>.ValidateDataAnnotations().ValidateOnStart()</c> in
/// <see cref="IdentityModule.AddIdentityModule"/> — every other Options type in this module
/// backs a feature that participates in every normal host startup (JWT auth, concurrent-session
/// enforcement, ...), so failing fast at boot is the right behavior for those. This one only backs
/// the opt-in <c>--seed</c> CLI action (<see cref="IdentitySeeder"/>); eagerly validating it at every
/// <c>dotnet run</c> would force every developer to configure an Admin/test-user password just to
/// start the plain API, even if they never seed. <see cref="SeedOptionsGuard"/> validates real values
/// are present the moment <see cref="IdentitySeeder"/> actually runs instead.
/// </para>
/// </summary>
public sealed class SeedOptions
{
    public const string SectionName = "Identity:Seed";

    /// <summary>
    /// Deliberately not a usable password — too short to pass
    /// <see cref="Features.Register.RegisterValidator.MinPasswordLength"/>, the same "must be
    /// overridden before it works" shape as <c>ConnectionStrings:Default</c>'s own
    /// <c>User Id=CHANGE_ME;Password=CHANGE_ME</c> placeholder (see
    /// <c>Persistence/DependencyInjection/PersistenceServiceCollectionExtensions.cs</c>).
    /// <see cref="SeedOptionsGuard"/> refuses to run while either password field below still equals
    /// this exact value, so an unconfigured checkout can never accidentally seed an Admin account
    /// with a publicly-known, committed-to-source-control password.
    /// </summary>
    public const string PlaceholderPassword = "CHANGE_ME";

    /// <summary>
    /// Not a secret (same reasoning as <see cref="EmailConfirmationOptions.ConfirmEmailUrl"/>/
    /// <see cref="PasswordResetOptions.ResetPasswordUrl"/>) — appsettings*.json may carry a real,
    /// usable placeholder directly. Still obviously fake (<c>@example.test</c>, RFC 2606's
    /// IANA-reserved-for-testing domain — the same one this codebase's own integration tests already
    /// use, see <c>RegisterAndConfirmEmailTests.cs</c>) so a human overriding it for a real deploy is
    /// an intentional, visible choice, not an accident.
    /// </summary>
    public string AdminEmail { get; set; } = "admin@example.test";

    /// <summary>Real value must come from user-secrets/env — see <see cref="PlaceholderPassword"/>.</summary>
    public string AdminPassword { get; set; } = PlaceholderPassword;

    /// <summary>
    /// Shared password for every non-Admin seed account (Learner/Instructor test users) — one shared
    /// value is fine specifically because these are synthetic fixtures whose entire purpose is being
    /// predictable for local dev/manual QA, not real user credentials. Real value must come from
    /// user-secrets/env — see <see cref="PlaceholderPassword"/>.
    /// </summary>
    public string TestUserPassword { get; set; } = PlaceholderPassword;
}
