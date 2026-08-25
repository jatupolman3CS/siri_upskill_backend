using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Siri.SharedKernel;

/// <summary>Reversible encryption and masking for sensitive financial/identity records (bank account
/// numbers, tax IDs) — see <see cref="DataProtectionOptions"/> for where the key comes from.</summary>
public interface ISensitiveDataProtector
{
    string Encrypt(string plainText);

    string Decrypt(string cipherTextBase64);

    string MaskAccountNumber(string? accountNo);
}

/// <summary>
/// Implementation of <see cref="ISensitiveDataProtector"/>. AES-GCM (authenticated encryption) keyed from
/// <see cref="DataProtectionOptions.EncryptionKeyBase64"/>, resolved through DI/Options — never a literal
/// in source. A key baked into source code protects nothing: anyone who can read this repository could
/// decrypt every protected record regardless of database access, which defeats the point of encrypting
/// them in the first place.
/// </summary>
public sealed class SensitiveDataProtector : ISensitiveDataProtector
{
    private readonly byte[] _key;

    public SensitiveDataProtector(IOptions<DataProtectionOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        byte[] key;
        try
        {
            key = Convert.FromBase64String(options.Value.EncryptionKeyBase64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException(
                "DataProtection:EncryptionKeyBase64 is not valid Base64. Generate one with `openssl rand -base64 32` and set it via dotnet user-secrets.",
                ex);
        }

        if (key.Length != 32)
        {
            throw new InvalidOperationException(
                $"DataProtection:EncryptionKeyBase64 must decode to exactly 32 bytes (AES-256) — got {key.Length}. Generate one with `openssl rand -base64 32`.");
        }

        _key = key;
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        var nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
        RandomNumberGenerator.Fill(nonce);

        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = new byte[plainBytes.Length];

        using var aes = new AesGcm(_key, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Combined: Nonce + Tag + Cipher
        var combined = new byte[nonce.Length + tag.Length + cipherBytes.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, nonce.Length + tag.Length, cipherBytes.Length);

        return Convert.ToBase64String(combined);
    }

    public string Decrypt(string cipherTextBase64)
    {
        if (string.IsNullOrEmpty(cipherTextBase64))
        {
            return string.Empty;
        }

        var combined = Convert.FromBase64String(cipherTextBase64);
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;

        if (combined.Length < nonceSize + tagSize)
        {
            throw new InvalidOperationException("Ciphertext is too short to contain a valid nonce+tag — data may be corrupted or was never encrypted with this key.");
        }

        var nonce = new byte[nonceSize];
        var tag = new byte[tagSize];
        var cipherBytes = new byte[combined.Length - nonceSize - tagSize];

        Buffer.BlockCopy(combined, 0, nonce, 0, nonceSize);
        Buffer.BlockCopy(combined, nonceSize, tag, 0, tagSize);
        Buffer.BlockCopy(combined, nonceSize + tagSize, cipherBytes, 0, cipherBytes.Length);

        var plainBytes = new byte[cipherBytes.Length];
        using var aes = new AesGcm(_key, tagSize);
        aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    public string MaskAccountNumber(string? accountNo)
    {
        if (string.IsNullOrWhiteSpace(accountNo))
        {
            return string.Empty;
        }

        var digits = accountNo.Trim();
        if (digits.Length <= 4)
        {
            return new string('*', digits.Length);
        }

        var lastFour = digits[^4..];
        return $"***-***-{lastFour}";
    }
}
