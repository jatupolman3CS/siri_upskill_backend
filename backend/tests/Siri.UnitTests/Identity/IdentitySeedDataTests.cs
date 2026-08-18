using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure.Seeding;

namespace Siri.UnitTests.Identity;

/// <summary>
/// Pure-logic tests for <see cref="IdentitySeedData.BuildUsers"/> (task P0-37) — no database, no
/// hashing, no DI; just asserting the fixed dev/test account list has the shape the task requires.
/// </summary>
public class IdentitySeedDataTests
{
    private static SeedOptions ConfiguredOptions() => new()
    {
        AdminEmail = "real-admin@example.test",
        AdminPassword = "a-real-admin-password-1",
        TestUserPassword = "a-real-test-user-password-1",
    };

    [Fact]
    public void BuildUsers_ReturnsExactlyOneAdminAndTheDocumentedNumberOfLearnersAndInstructors()
    {
        var users = IdentitySeedData.BuildUsers(ConfiguredOptions());

        Assert.Equal(1, users.Count(u => u.RoleId == Role.AdminId));
        Assert.Equal(3, users.Count(u => u.RoleId == Role.LearnerId)); // task range: "2-3 Learner accounts"
        Assert.Equal(2, users.Count(u => u.RoleId == Role.InstructorId)); // task range: "1-2 Instructor accounts"
        Assert.Equal(0, users.Count(u => u.RoleId == Role.SuperAdminId)); // task scope: Admin only, never SuperAdmin
    }

    [Fact]
    public void BuildUsers_EveryEmailIsUniqueAndNonEmpty()
    {
        var users = IdentitySeedData.BuildUsers(ConfiguredOptions());

        Assert.All(users, u => Assert.False(string.IsNullOrWhiteSpace(u.Email)));

        var distinctNormalizedEmails = users.Select(u => u.Email.Trim().ToUpperInvariant()).Distinct().Count();
        Assert.Equal(users.Count, distinctNormalizedEmails);
    }

    [Fact]
    public void BuildUsers_AdminAccount_UsesTheConfiguredEmailAndPassword()
    {
        var options = ConfiguredOptions();

        var users = IdentitySeedData.BuildUsers(options);
        var admin = users.Single(u => u.RoleId == Role.AdminId);

        Assert.Equal(options.AdminEmail, admin.Email);
        Assert.Equal(options.AdminPassword, admin.Password);
    }

    [Fact]
    public void BuildUsers_NonAdminAccounts_UseTheSharedConfiguredTestUserPassword()
    {
        var options = ConfiguredOptions();

        var users = IdentitySeedData.BuildUsers(options);
        var nonAdminAccounts = users.Where(u => u.RoleId != Role.AdminId);

        Assert.All(nonAdminAccounts, u => Assert.Equal(options.TestUserPassword, u.Password));
    }

    /// <summary>
    /// The task's core "nobody could mistake these for real user data" requirement — every seeded
    /// Learner/Instructor account must live on the IANA-reserved-for-testing domain (RFC 2606) this
    /// codebase's own integration tests already use, never a domain a real person could own.
    /// </summary>
    [Fact]
    public void BuildUsers_NonAdminAccounts_UseTheObviouslyFakeExampleTestDomain()
    {
        var users = IdentitySeedData.BuildUsers(ConfiguredOptions());
        var nonAdminAccounts = users.Where(u => u.RoleId != Role.AdminId);

        Assert.All(nonAdminAccounts, u => Assert.EndsWith("@example.test", u.Email, StringComparison.Ordinal));
        Assert.All(nonAdminAccounts, u => Assert.Contains(".seed@", u.Email, StringComparison.Ordinal));
    }

    [Fact]
    public void BuildUsers_EveryDisplayName_IsUnambiguouslyMarkedAsSeedData()
    {
        var users = IdentitySeedData.BuildUsers(ConfiguredOptions());

        Assert.All(users, u => Assert.False(string.IsNullOrWhiteSpace(u.DisplayName)));
        Assert.All(users, u => Assert.Contains("Seed", u.DisplayName, StringComparison.Ordinal));
    }

    [Fact]
    public void BuildUsers_CalledTwice_ReturnsTheSameDataEveryTime()
    {
        var options = ConfiguredOptions();

        var first = IdentitySeedData.BuildUsers(options);
        var second = IdentitySeedData.BuildUsers(options);

        Assert.Equal(first, second);
    }

    [Fact]
    public void BuildUsers_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => IdentitySeedData.BuildUsers(null!));
    }
}
