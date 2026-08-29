using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Notification;

public sealed class NotificationRepositoryTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeUserNotificationRepository : IUserNotificationRepository
    {
        public readonly List<USER_NOTIFICATION> Notifications = [];

        public Task<USER_NOTIFICATION?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Notifications.FirstOrDefault(n => n.Id == id));

        public Task<(IReadOnlyList<USER_NOTIFICATION> Items, int TotalCount)> GetByUserIdPagedAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var userNotifs = Notifications.Where(n => n.UserId == userId).ToList();
            return Task.FromResult<(IReadOnlyList<USER_NOTIFICATION>, int)>((userNotifs, userNotifs.Count));
        }

        public Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Notifications.Count(n => n.UserId == userId && n.ReadAtUtc == null));

        public Task AddAsync(USER_NOTIFICATION notification, CancellationToken cancellationToken)
        {
            Notifications.Add(notification);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(USER_NOTIFICATION notification, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeAnnouncementRepository : IAnnouncementRepository
    {
        public readonly List<ANNOUNCEMENT> Announcements = [];

        public Task<ANNOUNCEMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Announcements.FirstOrDefault(a => a.Id == id));

        public Task<IReadOnlyList<ANNOUNCEMENT>> GetByCourseIdAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ANNOUNCEMENT>>(Announcements.Where(a => a.CourseId == courseId).ToList());

        public Task<IReadOnlyList<ANNOUNCEMENT>> GetByInstructorIdAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ANNOUNCEMENT>>(Announcements.Where(a => a.InstructorId == instructorId).ToList());

        public Task AddAsync(ANNOUNCEMENT announcement, CancellationToken cancellationToken)
        {
            Announcements.Add(announcement);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(ANNOUNCEMENT announcement, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task UserNotificationRepository_AddAndGetUnreadCount_WorksProperly()
    {
        var repo = new FakeUserNotificationRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var userId = Guid.NewGuid();
        var n1 = USER_NOTIFICATION.Create(userId, "course_update", "บทเรียนใหม่", "มีบทเรียนใหม่เพิ่มในคอร์ส", "/learn", clock);
        var n2 = USER_NOTIFICATION.Create(userId, "system", "ประกาศระบบ", "ระบบจะทำการอัปเดต", null, clock);

        await repo.AddAsync(n1, CancellationToken.None);
        await repo.AddAsync(n2, CancellationToken.None);

        var unreadCount = await repo.GetUnreadCountAsync(userId, CancellationToken.None);
        Assert.Equal(2, unreadCount);

        n1.MarkRead(clock);
        await repo.UpdateAsync(n1, CancellationToken.None);

        var unreadAfter = await repo.GetUnreadCountAsync(userId, CancellationToken.None);
        Assert.Equal(1, unreadAfter);
    }

    [Fact]
    public async Task AnnouncementRepository_AddAndGetByCourse_ReturnsAnnouncements()
    {
        var repo = new FakeAnnouncementRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var courseId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();
        var ann = ANNOUNCEMENT.Create(courseId, instructorId, "ยินดีต้อนรับ", "ยินดีต้อนรับสู่คอร์สเรียน", false, null, clock);

        await repo.AddAsync(ann, CancellationToken.None);

        var list = await repo.GetByCourseIdAsync(courseId, CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("ยินดีต้อนรับ", list[0].Title);
    }
}
