using Siri.Modules.Catalog.Infrastructure;

namespace Siri.UnitTests.Catalog;

public class ThaiSlugGeneratorTests
{
    [Fact]
    public void GenerateBaseSlug_ThaiWithoutLeadingVowelsOrToneMarks_MapsEachCharacterInOrder()
    {
        // มานะ = ม(m) + า(a) + น(n) + ะ(a) — no leading vowels, no tone marks, so this is
        // unambiguous under the documented "writing order, not phonetic order" simplification.
        var slug = ThaiSlugGenerator.GenerateBaseSlug("มานะ");

        Assert.Equal("mana", slug);
    }

    [Fact]
    public void GenerateBaseSlug_ToneMarks_AreStrippedNotEmitted()
    {
        // น้ำ = น(n) + ้(mai tho, no output) + ำ(am)
        var slug = ThaiSlugGenerator.GenerateBaseSlug("น้ำ");

        Assert.Equal("nam", slug);
    }

    [Fact]
    public void GenerateBaseSlug_ThaiDigits_MapToArabicDigits()
    {
        var slug = ThaiSlugGenerator.GenerateBaseSlug("๑๒๓");

        Assert.Equal("123", slug);
    }

    [Fact]
    public void GenerateBaseSlug_AsciiTitle_LowercasesAndHyphenatesWithoutTransliteration()
    {
        var slug = ThaiSlugGenerator.GenerateBaseSlug("React");

        Assert.Equal("react", slug);
    }

    [Fact]
    public void GenerateBaseSlug_MixedThaiAndAscii_KeepsAsciiWordRecognizable()
    {
        var slug = ThaiSlugGenerator.GenerateBaseSlug("คอร์ส React เบื้องต้น");

        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", slug);
        Assert.Contains("react", slug);
    }

    [Fact]
    public void GenerateBaseSlug_MultipleSpacesAndPunctuation_CollapseToSingleHyphenWithNoTrailingHyphen()
    {
        var slug = ThaiSlugGenerator.GenerateBaseSlug("Web  Development!!");

        Assert.Equal("web-development", slug);
    }

    [Fact]
    public void GenerateBaseSlug_OnlyUnmappableCharacters_ReturnsEmptyString()
    {
        var slug = ThaiSlugGenerator.GenerateBaseSlug("!!!");

        Assert.Equal(string.Empty, slug);
    }

    [Fact]
    public void GenerateBaseSlug_Result_NeverContainsUppercaseOrInvalidCharacters()
    {
        var slug = ThaiSlugGenerator.GenerateBaseSlug("การพัฒนาเว็บไซต์ด้วย React และ Node.js");

        Assert.DoesNotMatch("[^a-z0-9-]", slug);
        Assert.DoesNotContain("--", slug);
        Assert.False(slug.StartsWith('-'));
        Assert.False(slug.EndsWith('-'));
    }
}
