using Siri.Integrations.Storage;
using Xunit;

namespace Siri.UnitTests.Storage;

public sealed class ContentDispositionHeaderTests
{
    [Fact]
    public void BuildAttachment_PlainName_HasAsciiFilenameAndUtf8Twin()
    {
        Assert.Equal(
            "attachment; filename=\"slides.pdf\"; filename*=UTF-8''slides.pdf",
            ContentDispositionHeader.BuildAttachment("slides.pdf"));
    }

    [Fact]
    public void BuildAttachment_ThaiName_UsesUnderscoreFallbackAndPercentEncodedUtf8()
    {
        var header = ContentDispositionHeader.BuildAttachment("บท1.pdf");

        Assert.StartsWith("attachment; filename=\"__1.pdf\"; filename*=UTF-8''", header);
        Assert.EndsWith(Uri.EscapeDataString("บท1.pdf"), header);
        Assert.All(header, ch => Assert.InRange(ch, ' ', '~'));
    }

    [Fact]
    public void BuildAttachment_QuoteAndBackslash_CannotBreakOutOfTheQuotedString()
    {
        var header = ContentDispositionHeader.BuildAttachment("a\"b\\c.pdf");

        Assert.StartsWith("attachment; filename=\"a_b_c.pdf\";", header);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildAttachment_BlankName_FallsBackToDownload(string? name)
    {
        Assert.Equal(
            "attachment; filename=\"download\"; filename*=UTF-8''download",
            ContentDispositionHeader.BuildAttachment(name!));
    }

    [Fact]
    public void BuildAttachment_VeryLongName_KeepsTheAsciiFallbackShort()
    {
        var header = ContentDispositionHeader.BuildAttachment(new string('x', 500) + ".pdf");
        var fallback = header.Split('"')[1];

        Assert.Equal(100, fallback.Length);
    }
}
