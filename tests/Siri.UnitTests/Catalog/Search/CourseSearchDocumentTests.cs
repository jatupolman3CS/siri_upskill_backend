using Siri.Modules.Catalog.Application;

namespace Siri.UnitTests.Catalog.Search;

public sealed class CourseSearchDocumentTests
{
    [Fact]
    public void IdFor_IsTypePrefixedAndUsesOnlyCharactersMeilisearchAllowsInDocumentIds()
    {
        var courseId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");

        var id = CourseSearchDocument.IdFor(courseId);

        Assert.Equal("course_0f8fad5b-d9cb-469f-a165-70867728950e", id);
        Assert.Matches("^[A-Za-z0-9_-]+$", id);
    }

    [Fact]
    public void ForCourse_CarriesTheInstructorNameSoSearchingByTeacherWorks()
    {
        var courseId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();

        var document = CourseSearchDocument.ForCourse(
            courseId, "python-basics", "Python เบื้องต้น", "เริ่มต้นเขียนโปรแกรม", "<p>เรียน <b>Python</b></p>",
            instructorId, "ธนกฤต ศรีสุวรรณ", "Senior Developer", categoryId, "โปรแกรมมิ่ง", "Programming");

        Assert.Equal(CourseSearchDocument.IdFor(courseId), document.Id);
        Assert.Equal(CourseSearchDocument.CourseType, document.Type);
        Assert.Equal(courseId, document.CourseId);
        Assert.Equal("ธนกฤต ศรีสุวรรณ", document.InstructorName);
        Assert.Equal("Senior Developer", document.InstructorHeadline);
        Assert.Equal(instructorId, document.InstructorId);
        Assert.Equal(categoryId, document.CategoryId);
        Assert.Equal("เรียน Python", document.Description);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   \n\t ", null)]
    [InlineData("<br/><p> </p>", null)]
    [InlineData("plain text", "plain text")]
    [InlineData("<h1>Title</h1><p>one   two\nthree</p>", "Title one two three")]
    [InlineData("  trimmed  ", "trimmed")]
    public void ToSearchText_StripsTagsAndCollapsesWhitespace(string? html, string? expected) =>
        Assert.Equal(expected, CourseSearchDocument.ToSearchText(html));

    [Fact]
    public void ToSearchText_LongDescription_IsCappedSoTheIndexStaysSmall()
    {
        var text = CourseSearchDocument.ToSearchText(new string('ก', CourseSearchDocument.MaxDescriptionLength * 3));

        Assert.NotNull(text);
        Assert.Equal(CourseSearchDocument.MaxDescriptionLength, text.Length);
    }
}
