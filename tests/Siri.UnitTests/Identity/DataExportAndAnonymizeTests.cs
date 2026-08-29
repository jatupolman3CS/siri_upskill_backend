using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.AnonymizeAccount;
using Siri.SharedKernel;

namespace Siri.UnitTests.Identity;

public sealed class DataExportAndAnonymizeTests
{
    private readonly IClock _clock;

    public DataExportAndAnonymizeTests()
    {
        _clock = new FixedClock(new DateTime(2026, 8, 27, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Anonymize_ScrubsPII_SetsStatusDeleted_AndDisablesTwoFactor()
    {
        // Arrange
        var user = USER.Register("alice@example.com", "ALICE@EXAMPLE.COM", "hashed_password", "Alice Smith");
        user.ConfirmEmail(_clock);
        user.EnableTwoFactor();

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.TwoFactorEnabled);

        // Act
        var anonymizedEmail = $"anonymized_{user.Id:N}@deleted.siriupskill.com";
        var unmatchableHash = "random_unmatchable_hash_value_12345";
        user.Anonymize(anonymizedEmail, "Deleted USER", unmatchableHash);

        // Assert
        Assert.Equal(UserStatus.Deleted, user.Status);
        Assert.Equal(anonymizedEmail, user.Email);
        Assert.Equal(anonymizedEmail.ToUpperInvariant(), user.NormalizedEmail);
        Assert.Equal("Deleted USER", user.DisplayName);
        Assert.Null(user.PhoneNumber);
        Assert.Null(user.AvatarUrl);
        Assert.Equal(unmatchableHash, user.PasswordHash);
        Assert.False(user.TwoFactorEnabled);
    }

    [Fact]
    public void Anonymize_WithNullOrEmptyArgs_ThrowsArgumentException()
    {
        var user = USER.Register("bob@example.com", "BOB@EXAMPLE.COM", "hash", "Bob Jones");

        Assert.Throws<ArgumentException>(() => user.Anonymize("", "Deleted USER", "hash"));
        Assert.Throws<ArgumentException>(() => user.Anonymize("anon@deleted.siriupskill.com", "", "hash"));
        Assert.Throws<ArgumentException>(() => user.Anonymize("anon@deleted.siriupskill.com", "Deleted USER", ""));
    }

    [Fact]
    public void AnonymizeAccountValidator_ValidatesConfirmationKeyword()
    {
        var validator = new AnonymizeAccountValidator();

        var validCommand = new AnonymizeAccountCommand(Guid.NewGuid(), "password", "DELETE");
        var result1 = validator.Validate(validCommand);
        Assert.True(result1.IsValid);

        var validCommand2 = new AnonymizeAccountCommand(Guid.NewGuid(), "password", "CONFIRM");
        var result2 = validator.Validate(validCommand2);
        Assert.True(result2.IsValid);

        var invalidCommand = new AnonymizeAccountCommand(Guid.NewGuid(), "password", "NO");
        var result3 = validator.Validate(invalidCommand);
        Assert.False(result3.IsValid);

        var emptyUserIdCommand = new AnonymizeAccountCommand(Guid.Empty, "password", "DELETE");
        var result4 = validator.Validate(emptyUserIdCommand);
        Assert.False(result4.IsValid);
    }

    private sealed class FixedClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
