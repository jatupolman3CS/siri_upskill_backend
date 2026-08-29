using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CreateLearningPath;
using Siri.Modules.Catalog.Features.UpdateLearningPath;
using Xunit;

namespace Siri.UnitTests.Catalog;

public sealed class LearningPathTests
{
    [Fact]
    public void LearningPath_Create_SetsPropertiesCorrectly()
    {
        var path = LEARNING_PATH.Create("fullstack-developer", "Fullstack Developer", "Roadmap to become a fullstack developer", 1, true);

        Assert.NotEqual(Guid.Empty, path.Id);
        Assert.Equal("fullstack-developer", path.Slug);
        Assert.Equal("Fullstack Developer", path.Title);
        Assert.Equal("Roadmap to become a fullstack developer", path.Description);
        Assert.Equal(1, path.SortOrder);
        Assert.True(path.IsActive);
        Assert.Empty(path.Items);
    }

    [Fact]
    public void LearningPath_SetCourses_OrdersSequentially()
    {
        var path = LEARNING_PATH.Create("backend-mastery", "Backend Mastery", null, 2);
        var course1 = Guid.NewGuid();
        var course2 = Guid.NewGuid();
        var course3 = Guid.NewGuid();

        path.SetCourses([course1, course2, course3, course1]); // course1 duplicate should be deduplicated

        Assert.Equal(3, path.Items.Count);
        var items = path.Items.OrderBy(i => i.SortOrder).ToList();
        Assert.Equal(course1, items[0].CourseId);
        Assert.Equal(1, items[0].SortOrder);
        Assert.Equal(course2, items[1].CourseId);
        Assert.Equal(2, items[1].SortOrder);
        Assert.Equal(course3, items[2].CourseId);
        Assert.Equal(3, items[2].SortOrder);
    }

    [Fact]
    public void LearningPath_Update_UpdatesProperties()
    {
        var path = LEARNING_PATH.Create("ai-engineer", "AI Engineer", "Old desc", 1);
        path.Update("ai-ml-engineer", "AI & ML Engineer", "New desc", 3, false);

        Assert.Equal("ai-ml-engineer", path.Slug);
        Assert.Equal("AI & ML Engineer", path.Title);
        Assert.Equal("New desc", path.Description);
        Assert.Equal(3, path.SortOrder);
        Assert.False(path.IsActive);
    }

    [Theory]
    [InlineData("valid-slug-123", true)]
    [InlineData("invalid slug with spaces", false)]
    [InlineData("INVALID_UPPERCASE", false)]
    [InlineData("", false)]
    public void CreateLearningPathValidator_ValidatesSlugFormat(string slug, bool expectedValid)
    {
        var validator = new CreateLearningPathValidator();
        var command = new CreateLearningPathCommand(slug, "Title", "Desc", 1, true, null);
        var result = validator.Validate(command);

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("valid-slug-123", true)]
    [InlineData("invalid slug with spaces", false)]
    [InlineData("INVALID_UPPERCASE", false)]
    [InlineData("", false)]
    public void UpdateLearningPathValidator_ValidatesSlugFormat(string slug, bool expectedValid)
    {
        var validator = new UpdateLearningPathValidator();
        var command = new UpdateLearningPathCommand(slug, "Title", "Desc", 1, true, null);
        var result = validator.Validate(command);

        Assert.Equal(expectedValid, result.IsValid);
    }
}
