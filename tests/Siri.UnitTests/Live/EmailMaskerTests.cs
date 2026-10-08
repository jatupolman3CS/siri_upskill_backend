using Siri.Modules.Live.Application;

namespace Siri.UnitTests.Live;

/// <summary>The roster shows an instructor a masked e-mail only (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md section 4.4).</summary>
public class EmailMaskerTests
{
    [Theory]
    [InlineData("alice@gmail.com", "a***@g***.com")]
    [InlineData("Bob.Smith@Example.org", "B***@E***.org")]
    [InlineData("x@y.io", "x***@y***.io")]
    [InlineData("first.last+tag@mail.example.co.th", "f***@m***.th")]
    [InlineData("  padded@hotmail.com  ", "p***@h***.com")]
    public void Mask_KeepsTheFirstCharacterOfTheLocalPartAndOfTheDomainAndTheLastLabelOnly(string email, string expected)
    {
        Assert.Equal(expected, EmailMasker.Mask(email));
    }

    [Theory]
    [InlineData("alice@gmail.com")]
    [InlineData("a.very.long.address@subdomain.example.com")]
    public void Mask_NeverContainsMoreThanTheFirstCharacterOfTheLocalPart(string email)
    {
        var masked = EmailMasker.Mask(email)!;

        Assert.DoesNotContain(email, masked);
        Assert.DoesNotContain(email.Split('@')[0], masked); // the local part never appears in full
        Assert.StartsWith(email[0] + "***@", masked);
        Assert.Equal(1, masked.Count(c => c == '@'));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Mask_BlankInput_IsNull(string? email)
    {
        Assert.Null(EmailMasker.Mask(email));
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("@gmail.com")]
    [InlineData("alice@")]
    public void Mask_ImplausibleInput_GivesOnlyStars_NeverEchoingIt(string email)
    {
        Assert.Equal("***", EmailMasker.Mask(email));
    }

    [Fact]
    public void Mask_ASingleLabelHost_IsMaskedAsAWhole()
    {
        Assert.Equal("a***@l***", EmailMasker.Mask("a@localhost"));
    }

    [Fact]
    public void Mask_DoesNotSplitASurrogatePairOrAThaiCombiningSequence()
    {
        // A supplementary-plane character (surrogate pair) and a Thai consonant with a tone mark must each survive as one whole character.
        Assert.Equal("\U0001D4D0***@g***.com", EmailMasker.Mask("\U0001D4D0lice@gmail.com"));
        Assert.Equal("ก่***@g***.com", EmailMasker.Mask("ก่อนหน้า@gmail.com"));
    }

    [Fact]
    public void Mask_UsesTheLastAtSign_SoAQuotedLocalPartCannotLeakThePrefix()
    {
        Assert.Equal("\"***@e***.com", EmailMasker.Mask("\"weird@name\"@example.com"));
    }
}
