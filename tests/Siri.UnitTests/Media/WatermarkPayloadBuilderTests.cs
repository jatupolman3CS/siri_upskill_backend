using System.Globalization;
using Siri.Modules.Media.Application;
using Xunit;

namespace Siri.UnitTests.Media;

/// <summary>SE-02 / Q8: the watermark payload names the viewer (name + email + timestamp) and is always one safe line.</summary>
public sealed class WatermarkPayloadBuilderTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 8, 7, 6, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.Parse("0199c4a2-7b1e-7f3a-9c1d-2a4b6c8d0e1f");

    private const string Stamp = "2026-10-09 08:07:06 UTC";

    [Fact]
    public void ForUser_NameAndEmail_UsesNameEmailTimestampOrder()
    {
        var payload = WatermarkPayloadBuilder.ForUser(UserId, "Jane Doe", "jane@example.com", Now);

        Assert.Equal($"Jane Doe · jane@example.com · {Stamp}", payload);
    }

    [Fact]
    public void ForUser_ThaiDisplayName_IsKeptVerbatim()
    {
        var payload = WatermarkPayloadBuilder.ForUser(UserId, "สมชาย ใจดี", "somchai@example.com", Now);

        Assert.Equal($"สมชาย ใจดี · somchai@example.com · {Stamp}", payload);
    }

    [Fact]
    public void ForUser_NoName_UsesEmailOnly()
    {
        Assert.Equal($"jane@example.com · {Stamp}", WatermarkPayloadBuilder.ForUser(UserId, null, "jane@example.com", Now));
        Assert.Equal($"jane@example.com · {Stamp}", WatermarkPayloadBuilder.ForUser(UserId, "  \t ", "jane@example.com", Now));
    }

    [Fact]
    public void ForUser_NoEmail_UsesNameAndUserId()
    {
        Assert.Equal($"Jane Doe · {UserId} · {Stamp}", WatermarkPayloadBuilder.ForUser(UserId, "Jane Doe", null, Now));
        Assert.Equal($"Jane Doe · {UserId} · {Stamp}", WatermarkPayloadBuilder.ForUser(UserId, "Jane Doe", "", Now));
    }

    [Fact]
    public void ForUser_NeitherNameNorEmail_FallsBackToTheLegacyUserIdForm()
    {
        Assert.Equal($"SIRI UpSkill · {UserId} · {Stamp}", WatermarkPayloadBuilder.ForUser(UserId, null, null, Now));
        Assert.Equal($"SIRI UpSkill · {UserId} · {Stamp}", WatermarkPayloadBuilder.ForUser(UserId, " ", "\r\n", Now));
    }

    [Fact]
    public void ForGuest_IsNeutralLabelAndTimestampOnly()
    {
        var payload = WatermarkPayloadBuilder.ForGuest(Now);

        Assert.Equal($"SIRI UpSkill · Guest · {Stamp}", payload);
    }

    // Code points are passed as numbers so no raw U+2028/U+2029/bidi character ever sits in this source file.
    [Theory]
    [InlineData(0x0A)]   // LF
    [InlineData(0x0D)]   // CR
    [InlineData(0x09)]   // TAB
    [InlineData(0x00)]   // NUL
    [InlineData(0x7F)]   // DEL
    [InlineData(0x85)]   // NEL
    [InlineData(0x2028)] // line separator
    [InlineData(0x2029)] // paragraph separator
    [InlineData(0x200E)] // LRM
    [InlineData(0x200F)] // RLM
    [InlineData(0x202E)] // right-to-left override
    [InlineData(0x2066)] // left-to-right isolate
    [InlineData(0x2069)] // pop directional isolate
    [InlineData(0xB7)]   // the field separator itself: a display name must not be able to forge fields
    public void ForUser_NameWithUnsafeCharacter_IsReplacedByASingleSpace(int codePoint)
    {
        var name = "Jane" + (char)codePoint + "Doe";

        var payload = WatermarkPayloadBuilder.ForUser(UserId, name, "jane@example.com", Now);

        Assert.Equal($"Jane Doe · jane@example.com · {Stamp}", payload);
        Assert.DoesNotContain(payload, c => char.IsControl(c));
    }

    [Fact]
    public void ForUser_NameWithLeadingTrailingAndRepeatedUnsafeCharacters_IsTrimmedAndCollapsed()
    {
        var rlo = ((char)0x202E).ToString();
        var lineSeparator = ((char)0x2028).ToString();
        var name = rlo + "  Jane " + lineSeparator + "\r\n\t  Doe " + rlo;

        var payload = WatermarkPayloadBuilder.ForUser(UserId, name, "jane@example.com", Now);

        Assert.Equal($"Jane Doe · jane@example.com · {Stamp}", payload);
    }

    [Fact]
    public void ForUser_NameTryingToForgeAnotherField_CannotInjectTheSeparator()
    {
        var payload = WatermarkPayloadBuilder.ForUser(UserId, "Mallory · victim@example.com", "mallory@example.com", Now);

        // Exactly two separators remain (name | email | timestamp), so a parser splitting on " · " sees three fields.
        Assert.Equal(2, payload.Split(" · ").Length - 1);
        Assert.Equal($"Mallory victim@example.com · mallory@example.com · {Stamp}", payload);
    }

    [Fact]
    public void ForUser_EmailWithNewlines_IsSanitisedToASingleLine()
    {
        var payload = WatermarkPayloadBuilder.ForUser(UserId, "Jane Doe", "jane@example.com\r\nBcc: attacker@example.com", Now);

        Assert.Equal($"Jane Doe · jane@example.com Bcc: attacker@example.com · {Stamp}", payload);
        Assert.DoesNotContain('\n', payload);
        Assert.DoesNotContain('\r', payload);
    }

    [Fact]
    public void ForUser_ExactlyAtTheCap_IsNotTruncated()
    {
        // 120 total - 26 (" · " + "yyyy-MM-dd HH:mm:ss UTC") = 94 chars for "name · email".
        var email = "a@example.com";                                   // 13
        var name = new string('N', 94 - email.Length - 3);             // 78
        var payload = WatermarkPayloadBuilder.ForUser(UserId, name, email, Now);

        Assert.Equal(WatermarkPayloadBuilder.MaxLength, payload.Length);
        Assert.StartsWith(name + " · " + email, payload);
        Assert.DoesNotContain('…', payload);
    }

    [Fact]
    public void ForUser_LongName_IsShortenedFirstAndKeepsEmailAndTimestamp()
    {
        var payload = WatermarkPayloadBuilder.ForUser(UserId, new string('N', 500), "jane@example.com", Now);

        Assert.Equal(WatermarkPayloadBuilder.MaxLength, payload.Length);
        Assert.Contains(" · jane@example.com · ", payload);
        Assert.EndsWith(Stamp, payload);
        Assert.Contains('…', payload);
    }

    [Fact]
    public void ForUser_LongEmail_IsCappedAndDropsNameButKeepsTimestamp()
    {
        var payload = WatermarkPayloadBuilder.ForUser(UserId, "Jane Doe", new string('e', 400) + "@example.com", Now);

        Assert.True(payload.Length <= WatermarkPayloadBuilder.MaxLength, $"payload was {payload.Length} chars");
        Assert.EndsWith($" · {Stamp}", payload);
        Assert.DoesNotContain("Jane Doe", payload);
    }

    [Fact]
    public void ForUser_LongNameWithoutEmail_KeepsFullUserIdAndTimestamp()
    {
        var payload = WatermarkPayloadBuilder.ForUser(UserId, new string('N', 500), null, Now);

        Assert.True(payload.Length <= WatermarkPayloadBuilder.MaxLength);
        Assert.Contains($" · {UserId} · {Stamp}", payload);
    }

    [Fact]
    public void ForUser_TruncationNeverSplitsASurrogatePair()
    {
        // The emoji is a surrogate pair; place it so the naive cut point would land between its two halves.
        var emoji = char.ConvertFromUtf32(0x1F600);
        var email = "jane@example.com";                                // 16 -> name budget = 94 - 16 - 3 = 75
        var name = new string('N', 73) + emoji + new string('N', 20);  // pair occupies chars 73..74, naive cut keeps 74 chars

        var payload = WatermarkPayloadBuilder.ForUser(UserId, name, email, Now);

        Assert.True(payload.Length <= WatermarkPayloadBuilder.MaxLength);
        for (var i = 0; i < payload.Length; i++)
        {
            if (char.IsHighSurrogate(payload[i]))
            {
                Assert.True(i + 1 < payload.Length && char.IsLowSurrogate(payload[i + 1]), "dangling high surrogate");
            }
            else if (char.IsLowSurrogate(payload[i]))
            {
                Assert.True(i > 0 && char.IsHighSurrogate(payload[i - 1]), "dangling low surrogate");
            }
        }
    }

    [Fact]
    public void ForUser_AndForGuest_UseInvariantCultureForTheTimestamp()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            // th-TH would otherwise render the Buddhist-era year (2569) for "yyyy".
            CultureInfo.CurrentCulture = new CultureInfo("th-TH");

            Assert.EndsWith(Stamp, WatermarkPayloadBuilder.ForUser(UserId, "Jane Doe", "jane@example.com", Now));
            Assert.EndsWith(Stamp, WatermarkPayloadBuilder.ForGuest(Now));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
