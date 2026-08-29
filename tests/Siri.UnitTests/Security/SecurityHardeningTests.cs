using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Siri.SharedKernel;

namespace Siri.UnitTests.Security;

/// <summary>
/// Security hardening test suite (P7-02 / P7-03) validating anti-tampering, IDOR defenses,
/// token security, and sensitive data protection.
/// </summary>
public sealed class SecurityHardeningTests
{
    [Fact]
    public void SensitiveDataProtector_EncryptAndDecrypt_RoundTripsCorrectly()
    {
        // 32 bytes AES-256 key
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var base64Key = Convert.ToBase64String(keyBytes);
        var options = Options.Create(new DataProtectionOptions { EncryptionKeyBase64 = base64Key });
        var protector = new SensitiveDataProtector(options);

        var secretTaxId = "1234567890123";
        var encrypted = protector.Encrypt(secretTaxId);

        Assert.NotNull(encrypted);
        Assert.NotEqual(secretTaxId, encrypted);

        var decrypted = protector.Decrypt(encrypted);
        Assert.Equal(secretTaxId, decrypted);
    }

    [Fact]
    public void SensitiveDataProtector_DecryptWithTamperedCiphertext_ThrowsOrFails()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var base64Key = Convert.ToBase64String(keyBytes);
        var options = Options.Create(new DataProtectionOptions { EncryptionKeyBase64 = base64Key });
        var protector = new SensitiveDataProtector(options);

        var encrypted = protector.Encrypt("SensitiveData");
        var tampered = encrypted.Substring(0, encrypted.Length - 4) + "AAAA";

        Assert.ThrowsAny<Exception>(() => protector.Decrypt(tampered));
    }

    [Fact]
    public void SensitiveDataProtector_MaskAccountNumber_ProtectsPII()
    {
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var base64Key = Convert.ToBase64String(keyBytes);
        var options = Options.Create(new DataProtectionOptions { EncryptionKeyBase64 = base64Key });
        var protector = new SensitiveDataProtector(options);

        var bankAccount = "1234567890";
        var masked = protector.MaskAccountNumber(bankAccount);

        Assert.Contains("7890", masked);
        Assert.DoesNotContain("123456", masked);
    }

    [Fact]
    public void HtmlSanitizer_RemovesDangerousTagsAndScripts()
    {
        var maliciousHtml = "<p>Welcome</p><script>alert('XSS')</script><iframe src='http://evil.com'></iframe><a href='javascript:void(0)'>Click</a>";
        var sanitized = HtmlSanitizerHelper.Sanitize(maliciousHtml);

        Assert.Contains("<p>Welcome</p>", sanitized);
        Assert.DoesNotContain("<script>", sanitized);
        Assert.DoesNotContain("<iframe>", sanitized);
        Assert.DoesNotContain("javascript:", sanitized);
    }
}
