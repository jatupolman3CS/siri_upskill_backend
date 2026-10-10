using Siri.Modules.Catalog.Features;
using Xunit;

namespace Siri.UnitTests.Catalog;

public sealed class AttachmentFileNamesTests
{
    [Theory]
    [InlineData("slides.pdf", "slides.pdf")]
    [InlineData("  slides.pdf  ", "slides.pdf")]
    [InlineData("C:\\Users\\me\\Desktop\\slides.pdf", "slides.pdf")]
    [InlineData("../../etc/passwd.txt", "passwd.txt")]
    [InlineData("..\\..\\evil.pdf", "evil.pdf")]
    [InlineData("a<b>c:d\"e|f?g*h.pdf", "abcdefgh.pdf")]
    [InlineData("two   spaces here.pdf", "two spaces here.pdf")]
    [InlineData("เอกสารประกอบการสอน บทที่ 1.pdf", "เอกสารประกอบการสอน บทที่ 1.pdf")]
    public void Sanitize_ReturnsSafeDisplayName(string raw, string expected)
    {
        Assert.Equal(expected, AttachmentFileNames.Sanitize(raw));
    }

    [Fact]
    public void Sanitize_StripsControlCharacters()
    {
        Assert.Equal("ab.pdf", AttachmentFileNames.Sanitize("a\u0000b\r\n.pdf"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("....")]
    [InlineData("<>:\"|?*")]
    public void Sanitize_ReturnsNullWhenNothingUsableIsLeft(string? raw)
    {
        Assert.Null(AttachmentFileNames.Sanitize(raw));
    }

    [Fact]
    public void Sanitize_TruncatesTheBaseNameButKeepsTheExtension()
    {
        var raw = new string('a', 400) + ".pdf";

        var result = AttachmentFileNames.Sanitize(raw);

        Assert.NotNull(result);
        Assert.Equal(AttachmentFileNames.MaxLength, result.Length);
        Assert.EndsWith(".pdf", result);
    }
}
