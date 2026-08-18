using Siri.Modules.Notification.Infrastructure.Templates;

namespace Siri.UnitTests.Notification;

public class EmailLayoutTests
{
    [Fact]
    public void Render_ValidInput_SubstitutesTitleAndContentIntoShell()
    {
        var html = EmailLayout.Render("หัวข้อทดสอบ", "<p>เนื้อหาทดสอบ</p>");

        Assert.Contains("หัวข้อทดสอบ", html, StringComparison.Ordinal);
        Assert.Contains("<p>เนื้อหาทดสอบ</p>", html, StringComparison.Ordinal);
        Assert.Contains("SIRI UpSkill", html, StringComparison.Ordinal);
        Assert.Contains("<!doctype html>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ValidInput_LeavesNoUnsubstitutedPlaceholderTokens()
    {
        var html = EmailLayout.Render("Title", "<p>Content</p>");

        Assert.DoesNotContain("{{", html, StringComparison.Ordinal);
        Assert.DoesNotContain("}}", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ValidInput_IncludesCurrentYearInFooter()
    {
        var html = EmailLayout.Render("Title", "<p>Content</p>");

        Assert.Contains(DateTime.UtcNow.Year.ToString(), html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "content")]
    [InlineData(null, "content")]
    public void Render_MissingTitle_ThrowsArgumentException(string? title, string content)
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException specifically for
        // null (a subtype of ArgumentException) and plain ArgumentException for "" — ThrowsAny
        // accepts either, matching the actual contract instead of over-specifying it.
        Assert.ThrowsAny<ArgumentException>(() => EmailLayout.Render(title!, content));
    }
}
