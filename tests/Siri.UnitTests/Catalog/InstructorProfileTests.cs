using Siri.Modules.Catalog.Domain;

namespace Siri.UnitTests.Catalog;

public class InstructorProfileTests
{
    private static INSTRUCTOR_PROFILE CreatePendingApplication() =>
        INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Somchai Dev", "Senior Full-Stack Developer", "สอนพัฒนาเว็บมา 10 ปี");

    [Fact]
    public void Apply_ValidInput_ReturnsPendingProfileWithDefaultRevenueShare()
    {
        var userId = Guid.NewGuid();

        var profile = INSTRUCTOR_PROFILE.Apply(userId, "Somchai Dev", "Senior Full-Stack Developer", "สอนพัฒนาเว็บมา 10 ปี");

        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal(userId, profile.UserId);
        Assert.Equal("Somchai Dev", profile.DisplayName);
        Assert.Equal("Senior Full-Stack Developer", profile.Headline);
        Assert.Equal("สอนพัฒนาเว็บมา 10 ปี", profile.Bio);
        Assert.Equal(INSTRUCTOR_PROFILE.DefaultRevenueSharePercent, profile.RevenueSharePercent);
        Assert.Equal(InstructorApplicationStatus.Pending, profile.Status);
        Assert.Null(profile.ApprovedAtUtc);
    }

    [Fact]
    public void Apply_NullHeadline_Succeeds()
    {
        var profile = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Somchai Dev", null, "สอนพัฒนาเว็บมา 10 ปี");

        Assert.Null(profile.Headline);
    }

    [Theory]
    [InlineData("", "สอนพัฒนาเว็บมา 10 ปี")]
    [InlineData("Somchai Dev", "")]
    public void Apply_MissingRequiredField_ThrowsArgumentException(string displayName, string bio)
    {
        Assert.Throws<ArgumentException>(() => INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), displayName, "Headline", bio));
    }

    [Fact]
    public void Resubmit_FromRejected_ReturnsToPendingAndUpdatesFields()
    {
        var profile = CreatePendingApplication();
        profile.Reject();

        profile.Resubmit("Somchai Dev 2", "Updated headline", "อัปเดตประวัติแล้ว");

        Assert.Equal(InstructorApplicationStatus.Pending, profile.Status);
        Assert.Equal("Somchai Dev 2", profile.DisplayName);
        Assert.Equal("Updated headline", profile.Headline);
        Assert.Equal("อัปเดตประวัติแล้ว", profile.Bio);
    }

    [Fact]
    public void Resubmit_FromPending_ThrowsInvalidOperationException()
    {
        var profile = CreatePendingApplication();

        Assert.Throws<InvalidOperationException>(() => profile.Resubmit("New Name", null, "New bio"));
    }

    [Fact]
    public void Resubmit_FromApproved_ThrowsInvalidOperationException()
    {
        var profile = CreatePendingApplication();
        profile.Approve(new FakeClock(DateTime.UtcNow));

        Assert.Throws<InvalidOperationException>(() => profile.Resubmit("New Name", null, "New bio"));
    }

    [Fact]
    public void Approve_FromPending_SetsApprovedStatusAndApprovedAtUtc()
    {
        var profile = CreatePendingApplication();
        var clock = new FakeClock(new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc));

        profile.Approve(clock);

        Assert.Equal(InstructorApplicationStatus.Approved, profile.Status);
        Assert.Equal(clock.UtcNow, profile.ApprovedAtUtc);
    }

    [Fact]
    public void Approve_AlreadyApproved_ThrowsInvalidOperationException()
    {
        var profile = CreatePendingApplication();
        profile.Approve(new FakeClock(DateTime.UtcNow));

        Assert.Throws<InvalidOperationException>(() => profile.Approve(new FakeClock(DateTime.UtcNow)));
    }

    [Fact]
    public void Approve_FromRejected_ThrowsInvalidOperationException()
    {
        var profile = CreatePendingApplication();
        profile.Reject();

        Assert.Throws<InvalidOperationException>(() => profile.Approve(new FakeClock(DateTime.UtcNow)));
    }

    [Fact]
    public void Reject_FromPending_SetsRejectedStatus()
    {
        var profile = CreatePendingApplication();

        profile.Reject();

        Assert.Equal(InstructorApplicationStatus.Rejected, profile.Status);
    }

    [Fact]
    public void Reject_AlreadyRejected_ThrowsInvalidOperationException()
    {
        var profile = CreatePendingApplication();
        profile.Reject();

        Assert.Throws<InvalidOperationException>(() => profile.Reject());
    }

    [Fact]
    public void Reject_FromApproved_ThrowsInvalidOperationExceptionAndLeavesStatusUnchanged()
    {
        var profile = CreatePendingApplication();
        profile.Approve(new FakeClock(DateTime.UtcNow));

        Assert.Throws<InvalidOperationException>(() => profile.Reject());
        Assert.Equal(InstructorApplicationStatus.Approved, profile.Status); // rejected attempt must not mutate state
    }
}
