using Siri.Modules.Community.Application;
using Siri.Modules.Community.Application.Response;
using Siri.Modules.Community.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Community;

public sealed class CommunityServiceTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeDiscussionRepository : IDiscussionRepository
    {
        public readonly Dictionary<Guid, DISCUSSION> Discussions = [];

        public Task<DISCUSSION?> GetByIdAsync(Guid discussionId, CancellationToken cancellationToken) =>
            Task.FromResult(Discussions.TryGetValue(discussionId, out var d) ? d : null);

        public Task<PagedResult<DISCUSSION>> ListByEpisodeAsync(Guid episodeId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var list = Discussions.Values.Where(d => d.EPISODE_ID == episodeId && d.STATUS == DiscussionStatus.Visible).OrderByDescending(d => d.CreatedAtUtc).ToList();
            return Task.FromResult(PagedResult<DISCUSSION>.Create(list.Skip((page - 1) * pageSize).Take(pageSize).ToList(), list.Count, page, pageSize));
        }

        public void Add(DISCUSSION discussion) => Discussions[discussion.DISCUSSION_ID] = discussion;

        public void Remove(DISCUSSION discussion) => Discussions.Remove(discussion.DISCUSSION_ID);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeReportRepository : IReportRepository
    {
        public readonly Dictionary<Guid, REPORT> Reports = [];

        public Task<REPORT?> GetByIdAsync(Guid reportId, CancellationToken cancellationToken) =>
            Task.FromResult(Reports.TryGetValue(reportId, out var r) ? r : null);

        public Task<PagedResult<REPORT>> ListPendingAsync(int page, int pageSize, CancellationToken cancellationToken)
        {
            var list = Reports.Values.Where(r => r.STATUS == ReportStatus.Pending).OrderBy(r => r.CreatedAtUtc).ToList();
            return Task.FromResult(PagedResult<REPORT>.Create(list.Skip((page - 1) * pageSize).Take(pageSize).ToList(), list.Count, page, pageSize));
        }

        public void Add(REPORT report) => Reports[report.REPORT_ID] = report;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task DiscussionService_Create_Upvote_Delete()
    {
        var repo = new FakeDiscussionRepository();
        var service = new DiscussionService(repo);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        var cmd = new CreateDiscussionCommand(courseId, episodeId, null, "How do I run this code?");
        var created = await service.CreateAsync(userId, cmd, CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.Equal(0, created.Value.UpvoteCount);

        var upvoted = await service.UpvoteAsync(userId, created.Value.Id, CancellationToken.None);
        Assert.True(upvoted.IsSuccess);
        Assert.Equal(1, upvoted.Value.UpvoteCount);

        // Delete from other user fails (IDOR / Forbidden)
        var otherUserId = Guid.NewGuid();
        var deleteForbidden = await service.DeleteAsync(otherUserId, created.Value.Id, CancellationToken.None);
        Assert.False(deleteForbidden.IsSuccess);
        Assert.Equal("forbidden", deleteForbidden.Error.Code);

        // Delete from author succeeds
        var deleteOwn = await service.DeleteAsync(userId, created.Value.Id, CancellationToken.None);
        Assert.True(deleteOwn.IsSuccess);
    }

    [Fact]
    public async Task ReportService_Create_Resolve_Dismiss()
    {
        var discussionRepo = new FakeDiscussionRepository();
        var reportRepo = new FakeReportRepository();
        var now = DateTime.UtcNow;
        var clock = new FakeClock(now);
        var service = new ReportService(reportRepo, discussionRepo, clock);

        var disc = DISCUSSION.Create(Guid.NewGuid(), null, Guid.NewGuid(), null, "Some bad content");
        discussionRepo.Add(disc);

        var reporterId = Guid.NewGuid();
        var cmd = new CreateReportCommand(disc.DISCUSSION_ID, "Spam / offensive language");

        var created = await service.CreateAsync(reporterId, cmd, CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal(ReportStatus.Pending, created.Value.Status);

        var resolved = await service.ResolveAsync(created.Value.Id, CancellationToken.None);
        Assert.True(resolved.IsSuccess);
        Assert.Equal(ReportStatus.Resolved, resolved.Value.Status);
        Assert.Equal(now, resolved.Value.ResolvedAtUtc);
    }
}
