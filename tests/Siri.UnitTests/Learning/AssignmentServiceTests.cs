using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

public sealed class AssignmentServiceTests
{
    private sealed class FakeAssignmentRepository : IAssignmentRepository
    {
        public readonly Dictionary<Guid, ASSIGNMENT> Assignments = [];

        public Task<ASSIGNMENT?> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Assignments.TryGetValue(assignmentId, out var assignment) ? assignment : null);

        public Task<ASSIGNMENT?> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(Assignments.Values.FirstOrDefault(a => a.EPISODE_ID == episodeId));

        public void Add(ASSIGNMENT assignment) => Assignments[assignment.ASSIGNMENT_ID] = assignment;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        private readonly Dictionary<Guid, Guid> _episodeOwners = [];
        private readonly Dictionary<Guid, Guid> _episodeCourses = [];
        private readonly HashSet<Guid> _freePreviews = [];

        public void RegisterOwner(Guid episodeId, Guid instructorUserId) => _episodeOwners[episodeId] = instructorUserId;
        public void RegisterCourse(Guid episodeId, Guid courseId) => _episodeCourses[episodeId] = courseId;
        public void RegisterFreePreview(Guid episodeId) => _freePreviews.Add(episodeId);

        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(new Dictionary<Guid, CoursePriceInfo>());

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(_freePreviews.Contains(episodeId));

        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(_episodeCourses.TryGetValue(episodeId, out var cid) ? (Guid?)cid : null);

        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) =>
            Task.FromResult(_episodeOwners.TryGetValue(episodeId, out var owner) && owner == instructorUserId);

        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }

    private sealed class FakeEnrollmentRepository : IEnrollmentRepository
    {
        public readonly Dictionary<Guid, ENROLLMENT> Enrollments = [];

        public Task<ENROLLMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Enrollments.TryGetValue(id, out var e) ? e : null);

        public Task<ENROLLMENT?> GetByUserAndCourseAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(Enrollments.Values.FirstOrDefault(e => e.USER_ID == userId && e.COURSE_ID == courseId));

        public IQueryable<ENROLLMENT> Query() => Enrollments.Values.AsQueryable();

        public void Add(ENROLLMENT enrollment) => Enrollments[enrollment.ENROLLMENT_ID] = enrollment;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    [Fact]
    public async Task CreateAsync_CreatesAssignment_WhenCallerIsInstructorOwner()
    {
        var repo = new FakeAssignmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var instructorId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, instructorId);

        var service = new AssignmentService(repo, catalog, enrollments);

        var request = new CreateAssignmentRequest(episodeId, "การบ้าน 1", "ส่งไฟล์ PDF", 7, 20, "pdf,zip");

        var result = await service.CreateAsync(instructorId, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("การบ้าน 1", result.Value.Title);
        Assert.Equal("ส่งไฟล์ PDF", result.Value.Instructions);
        Assert.Equal(7, result.Value.DueDays);
        Assert.Equal(20, result.Value.MaxFileSizeMb);
        Assert.Equal("pdf,zip", result.Value.AllowedExtensions);
        Assert.Single(repo.Assignments);
    }

    [Fact]
    public async Task CreateAsync_ReturnsNotFound_WhenCallerDoesNotOwnEpisode()
    {
        var repo = new FakeAssignmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var episodeId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, Guid.NewGuid()); // someone else

        var service = new AssignmentService(repo, catalog, enrollments);

        var request = new CreateAssignmentRequest(episodeId, "การบ้าน 1", "ส่งไฟล์ PDF", 7, 20, "pdf,zip");

        var result = await service.CreateAsync(Guid.NewGuid(), request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task UpdateAsync_UpdatesDetails()
    {
        var repo = new FakeAssignmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var instructorId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, instructorId);

        var service = new AssignmentService(repo, catalog, enrollments);

        var createResult = await service.CreateAsync(instructorId, new CreateAssignmentRequest(episodeId, "การบ้าน", "คำอธิบายเดิม", 5, 10, "pdf"), CancellationToken.None);
        var assignmentId = createResult.Value.Id;

        var updateRequest = new UpdateAssignmentRequest("การบ้าน (แก้ไข)", "คำอธิบายใหม่", 14, 50, "pdf,docx,zip");
        var updateResult = await service.UpdateAsync(instructorId, assignmentId, updateRequest, CancellationToken.None);

        Assert.True(updateResult.IsSuccess);
        Assert.Equal("การบ้าน (แก้ไข)", updateResult.Value.Title);
        Assert.Equal("คำอธิบายใหม่", updateResult.Value.Instructions);
        Assert.Equal(14, updateResult.Value.DueDays);
        Assert.Equal(50, updateResult.Value.MaxFileSizeMb);
    }

    [Fact]
    public async Task GetByEpisodeForLearnerAsync_ReturnsAssignmentForEnrolledLearner()
    {
        var repo = new FakeAssignmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var instructorId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var learnerUserId = Guid.NewGuid();

        catalog.RegisterOwner(episodeId, instructorId);
        catalog.RegisterCourse(episodeId, courseId);

        var service = new AssignmentService(repo, catalog, enrollments);
        await service.CreateAsync(instructorId, new CreateAssignmentRequest(episodeId, "การบ้านประจำบท", "โจทย์", 7, 20, "pdf"), CancellationToken.None);

        // Not enrolled -> Forbidden
        var forbiddenResult = await service.GetByEpisodeForLearnerAsync(learnerUserId, episodeId, CancellationToken.None);
        Assert.False(forbiddenResult.IsSuccess);
        Assert.Equal("forbidden", forbiddenResult.Error.Code);

        // Enrolled -> Success
        var enrollment = ENROLLMENT.Create(learnerUserId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollments.Add(enrollment);

        var successResult = await service.GetByEpisodeForLearnerAsync(learnerUserId, episodeId, CancellationToken.None);
        Assert.True(successResult.IsSuccess);
        Assert.Equal("การบ้านประจำบท", successResult.Value.Title);
    }
}

