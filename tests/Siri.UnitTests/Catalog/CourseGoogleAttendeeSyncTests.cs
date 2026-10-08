using Siri.Modules.Catalog.Domain;

namespace Siri.UnitTests.Catalog;

/// <summary>Unit tests for <see cref="COURSE.SetGoogleAttendeeSync"/> (task P11-04, docs/contracts/
/// P11-04-live-invites-ics-reminders.md §2.3).</summary>
public class CourseGoogleAttendeeSyncTests
{
    private static COURSE CreateDraftCourse() =>
        COURSE.Create("sync-course", "Sync COURSE", Guid.NewGuid(), Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, 990m);

    [Fact]
    public void NewCourse_HasGoogleAttendeeSyncDisabled()
    {
        Assert.False(CreateDraftCourse().GoogleAttendeeSyncEnabled);
    }

    [Fact]
    public void SetGoogleAttendeeSync_TogglesTheFlag()
    {
        var course = CreateDraftCourse();

        course.SetGoogleAttendeeSync(true);
        Assert.True(course.GoogleAttendeeSyncEnabled);

        course.SetGoogleAttendeeSync(false);
        Assert.False(course.GoogleAttendeeSyncEnabled);
    }

    [Theory]
    [InlineData(DeliveryFormat.OnDemand)]
    [InlineData(DeliveryFormat.Live)]
    [InlineData(DeliveryFormat.Hybrid)]
    public void SetGoogleAttendeeSync_IsNotTiedToDeliveryFormat(DeliveryFormat format)
    {
        var course = CreateDraftCourse();
        course.SetDeliveryFormat(format);

        course.SetGoogleAttendeeSync(true);

        Assert.True(course.GoogleAttendeeSyncEnabled);
    }

    [Fact]
    public void SetGoogleAttendeeSync_ArchivedCourse_ThrowsAndKeepsTheFlag()
    {
        var course = CreateDraftCourse();
        course.SetGoogleAttendeeSync(true);
        course.Archive();

        Assert.Throws<InvalidOperationException>(() => course.SetGoogleAttendeeSync(false));
        Assert.True(course.GoogleAttendeeSyncEnabled);
    }
}
