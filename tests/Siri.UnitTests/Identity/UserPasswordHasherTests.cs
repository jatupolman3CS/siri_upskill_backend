using Microsoft.AspNetCore.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;

namespace Siri.UnitTests.Identity;

public class UserPasswordHasherTests
{
    [Fact]
    public void HashPassword_ThenVerifyPassword_WithCorrectPassword_Succeeds()
    {
        var hasher = new UserPasswordHasher();
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "placeholder", "Student One");

        var hash = hasher.HashPassword(user, "Correct-Horse-Battery-Staple-1");
        var result = hasher.VerifyPassword(user, hash, "Correct-Horse-Battery-Staple-1");

        Assert.NotEqual("Correct-Horse-Battery-Staple-1", hash); // never store the raw password
        Assert.Equal(PasswordVerificationResult.Success, result);
    }

    [Fact]
    public void VerifyPassword_WithWrongPassword_Fails()
    {
        var hasher = new UserPasswordHasher();
        var user = USER.Register("student@example.com", "STUDENT@EXAMPLE.COM", "placeholder", "Student One");
        var hash = hasher.HashPassword(user, "Correct-Horse-Battery-Staple-1");

        var result = hasher.VerifyPassword(user, hash, "Wrong-Password-123");

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }
}
