using Siri.Modules.Catalog.Domain;
using Xunit;

namespace Siri.UnitTests.Catalog;

public sealed class CourseReviewTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    [InlineData(10)]
    public void Create_InvalidRating_ReturnsFailure(int rating)
    {
        var result = COURSE_REVIEW.Create(Guid.NewGuid(), Guid.NewGuid(), rating, "Great course", DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Contains("คะแนนรีวิวต้องอยู่ระหว่าง 1 ถึง 5 ดาว", result.Error.Message);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Create_ValidRating_ReturnsSuccess(int rating)
    {
        var courseId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var result = COURSE_REVIEW.Create(courseId, userId, rating, "  เยี่ยมากๆ  ", now);

        Assert.True(result.IsSuccess);
        Assert.Equal(courseId, result.Value.CourseId);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Equal(rating, result.Value.Rating);
        Assert.Equal("เยี่ยมากๆ", result.Value.Comment);
        Assert.True(result.Value.IsPublished);
    }

    [Fact]
    public void Update_ModifiesRatingAndComment()
    {
        var review = COURSE_REVIEW.Create(Guid.NewGuid(), Guid.NewGuid(), 4, "Good", DateTime.UtcNow).Value;
        var updateTime = DateTime.UtcNow.AddMinutes(5);

        review.Update(5, "Excellent!", updateTime);

        Assert.Equal(5, review.Rating);
        Assert.Equal("Excellent!", review.Comment);
        Assert.Equal(updateTime, review.UpdatedAtUtc);
    }

    [Fact]
    public void Course_UpdateRatingStats_UpdatesProperties()
    {
        var course = COURSE.Create(
            "dotnet-core",
            "C# and .NET 10",
            Guid.NewGuid(),
            Guid.NewGuid(),
            CourseLevel.Beginner,
            CourseLanguage.Thai,
            1200m);

        course.UpdateRatingStats(4.8m, 42);

        Assert.Equal(4.8m, course.RatingAverage);
        Assert.Equal(42, course.RatingCount);
    }
}
