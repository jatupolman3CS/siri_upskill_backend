using Siri.Modules.Identity.Features.Register;
using Siri.Modules.Identity.Infrastructure.Seeding;

namespace Siri.UnitTests.Identity;

/// <summary>
/// Pure-logic tests for <see cref="SeedOptionsGuard.EnsureRealPasswordsConfigured"/> (task P0-37) —
/// proves the seeder can never run with the committed appsettings placeholder, and never with a
/// password weaker than real registration would ever accept.
/// </summary>
public class SeedOptionsGuardTests
{
    private static SeedOptions ValidOptions() => new()
    {
        AdminEmail = "admin@example.test",
        AdminPassword = "a-real-admin-password-1",
        TestUserPassword = "a-real-test-user-password-1",
    };

    [Fact]
    public void EnsureRealPasswordsConfigured_BothPasswordsAreRealAndLongEnough_DoesNotThrow()
    {
        var exception = Record.Exception(() => SeedOptionsGuard.EnsureRealPasswordsConfigured(ValidOptions()));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureRealPasswordsConfigured_AdminPasswordStillThePlaceholder_Throws()
    {
        var options = ValidOptions();
        options.AdminPassword = SeedOptions.PlaceholderPassword;

        var exception = Assert.Throws<InvalidOperationException>(() => SeedOptionsGuard.EnsureRealPasswordsConfigured(options));

        Assert.Contains("Identity:Seed:AdminPassword", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureRealPasswordsConfigured_TestUserPasswordStillThePlaceholder_Throws()
    {
        var options = ValidOptions();
        options.TestUserPassword = SeedOptions.PlaceholderPassword;

        var exception = Assert.Throws<InvalidOperationException>(() => SeedOptionsGuard.EnsureRealPasswordsConfigured(options));

        Assert.Contains("Identity:Seed:TestUserPassword", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureRealPasswordsConfigured_AdminPasswordShorterThanRegisterValidatorFloor_Throws()
    {
        var options = ValidOptions();
        options.AdminPassword = new string('a', RegisterValidator.MinPasswordLength - 1);

        Assert.Throws<InvalidOperationException>(() => SeedOptionsGuard.EnsureRealPasswordsConfigured(options));
    }

    [Fact]
    public void EnsureRealPasswordsConfigured_PasswordAtExactlyTheFloor_DoesNotThrow()
    {
        var options = ValidOptions();
        options.AdminPassword = new string('a', RegisterValidator.MinPasswordLength);
        options.TestUserPassword = new string('b', RegisterValidator.MinPasswordLength);

        var exception = Record.Exception(() => SeedOptionsGuard.EnsureRealPasswordsConfigured(options));

        Assert.Null(exception);
    }

    [Fact]
    public void EnsureRealPasswordsConfigured_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => SeedOptionsGuard.EnsureRealPasswordsConfigured(null!));
    }
}
