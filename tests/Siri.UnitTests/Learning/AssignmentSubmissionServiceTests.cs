using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

public sealed class AssignmentSubmissionServiceTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

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

    private sealed class FakeAssignmentSubmissionRepository : IAssignmentSubmissionRepository
    {
        public readonly Dictionary<Guid, ASSIGNMENT_SUBMISSION> Submissions = [];

        public Task<ASSIGNMENT_SUBMISSION?> GetByIdAsync(Guid submissionId, CancellationToken cancellationToken) =>
            Task.FromResult(Submissions.TryGetValue(submissionId, out var sub) ? sub : null);

        public Task<ASSIGNMENT_SUBMISSION?> GetLatestByEnrollmentAndAssignmentAsync(Guid enrollmentId, Guid assignmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Submissions.Values
                .Where(s => s.ENROLLMENT_ID == enrollmentId && s.ASSIGNMENT_ID == assignmentId)
                .OrderByDescending(s => s.SUBMITTED_AT_UTC)
                .FirstOrDefault());

        public Task<PagedResult<ASSIGNMENT_SUBMISSION>> ListByAssignmentAsync(Guid assignmentId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var items = Submissions.Values.Where(s => s.ASSIGNMENT_ID == assignmentId).ToList();
            return Task.FromResult(PagedResult<ASSIGNMENT_SUBMISSION>.Create(items, items.Count, page, pageSize));
        }

        public void Add(ASSIGNMENT_SUBMISSION submission) => Submissions[submission.ASSIGNMENT_SUBMISSION_ID] = submission;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        private readonly Dictionary<Guid, Guid> _episodeOwners = [];
        private readonly Dictionary<Guid, Guid> _episodeCourses = [];

        public void RegisterOwner(Guid episodeId, Guid instructorUserId) => _episodeOwners[episodeId] = instructorUserId;
        public void RegisterCourse(Guid episodeId, Guid courseId) => _episodeCourses[episodeId] = courseId;

        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(new Dictionary<Guid, CoursePriceInfo>());

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);

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

    [Fact]
    public async Task SubmitAsync_CreatesSubmissionWithSubmittedStatus()
    {
        var assignmentRepo = new FakeAssignmentRepository();
        var submissionRepo = new FakeAssignmentSubmissionRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var episodeId = Guid.NewGuid();
        catalog.RegisterCourse(episodeId, courseId);

        var assignment = ASSIGNMENT.Create(episodeId, "การบ้าน 1", "คำอธิบาย", 7, 20, "pdf");
        assignmentRepo.Add(assignment);

        var service = new AssignmentSubmissionService(submissionRepo, assignmentRepo, enrollRepo, catalog, clock);
        var request = new SubmitAssignmentRequest(assignment.ASSIGNMENT_ID, enrollment.ENROLLMENT_ID, "storage/assignments/sub-1.pdf", "โน้ตจากผู้เรียน");

        var result = await service.SubmitAsync(userId, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AssignmentSubmissionStatus.Submitted, result.Value.Status);
        Assert.Equal("storage/assignments/sub-1.pdf", result.Value.StorageKey);
        Assert.Equal("โน้ตจากผู้เรียน", result.Value.Note);
        Assert.Equal(now, result.Value.SubmittedAtUtc);
        Assert.Single(submissionRepo.Submissions);
    }

    [Fact]
    public async Task GradeAsync_GradesSubmissionAndSetsScore()
    {
        var assignmentRepo = new FakeAssignmentRepository();
        var submissionRepo = new FakeAssignmentSubmissionRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var episodeId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, instructorId);

        var assignment = ASSIGNMENT.Create(episodeId, "การบ้าน 1", "คำอธิบาย", 7, 20, "pdf");
        assignmentRepo.Add(assignment);

        var submission = ASSIGNMENT_SUBMISSION.Submit(assignment.ASSIGNMENT_ID, Guid.NewGuid(), "key", null, clock);
        submissionRepo.Add(submission);

        var service = new AssignmentSubmissionService(submissionRepo, assignmentRepo, enrollRepo, catalog, clock);

        var gradeRequest = new GradeAssignmentSubmissionRequest(AssignmentSubmissionStatus.Graded, 95m, "ยอดเยี่ยมมาก");
        var result = await service.GradeAsync(instructorId, submission.ASSIGNMENT_SUBMISSION_ID, gradeRequest, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AssignmentSubmissionStatus.Graded, result.Value.Status);
        Assert.Equal(95m, result.Value.Score);
        Assert.Equal("ยอดเยี่ยมมาก", result.Value.Feedback);
        Assert.Equal(instructorId, result.Value.GradedByUserId);
        Assert.Equal(now, result.Value.GradedAtUtc);
    }

    [Fact]
    public async Task GradeAsync_RejectsSubmissionAndSetsFeedback()
    {
        var assignmentRepo = new FakeAssignmentRepository();
        var submissionRepo = new FakeAssignmentSubmissionRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var episodeId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, instructorId);

        var assignment = ASSIGNMENT.Create(episodeId, "การบ้าน 1", "คำอธิบาย", 7, 20, "pdf");
        assignmentRepo.Add(assignment);

        var submission = ASSIGNMENT_SUBMISSION.Submit(assignment.ASSIGNMENT_ID, Guid.NewGuid(), "key", null, clock);
        submissionRepo.Add(submission);

        var service = new AssignmentSubmissionService(submissionRepo, assignmentRepo, enrollRepo, catalog, clock);

        var rejectRequest = new GradeAssignmentSubmissionRequest(AssignmentSubmissionStatus.Rejected, null, "กรุณาส่งไฟล์ใหม่ให้ตรงตามโจทย์");
        var result = await service.GradeAsync(instructorId, submission.ASSIGNMENT_SUBMISSION_ID, rejectRequest, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AssignmentSubmissionStatus.Rejected, result.Value.Status);
        Assert.Null(result.Value.Score);
        Assert.Equal("กรุณาส่งไฟล์ใหม่ให้ตรงตามโจทย์", result.Value.Feedback);
        Assert.Equal(instructorId, result.Value.GradedByUserId);
        Assert.Equal(now, result.Value.GradedAtUtc);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCallerIsTheOwner_ReturnsSubmission()
    {
        var assignmentRepo = new FakeAssignmentRepository();
        var submissionRepo = new FakeAssignmentSubmissionRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var userId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var submission = ASSIGNMENT_SUBMISSION.Submit(Guid.NewGuid(), enrollment.ENROLLMENT_ID, "key", null, clock);
        submissionRepo.Add(submission);

        var service = new AssignmentSubmissionService(submissionRepo, assignmentRepo, enrollRepo, catalog, clock);

        var result = await service.GetByIdAsync(userId, submission.ASSIGNMENT_SUBMISSION_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(submission.ASSIGNMENT_SUBMISSION_ID, result.Value.Id);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCallerIsNotTheOwner_ReturnsNotFound()
    {
        var assignmentRepo = new FakeAssignmentRepository();
        var submissionRepo = new FakeAssignmentSubmissionRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(ownerId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var submission = ASSIGNMENT_SUBMISSION.Submit(Guid.NewGuid(), enrollment.ENROLLMENT_ID, "key", null, clock);
        submissionRepo.Add(submission);

        var service = new AssignmentSubmissionService(submissionRepo, assignmentRepo, enrollRepo, catalog, clock);

        var attackerId = Guid.NewGuid();
        var result = await service.GetByIdAsync(attackerId, submission.ASSIGNMENT_SUBMISSION_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetMySubmissionByAssignmentAsync_ReturnsLatestSubmission()
    {
        var assignmentRepo = new FakeAssignmentRepository();
        var submissionRepo = new FakeAssignmentSubmissionRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var now = DateTime.UtcNow;
        var clock = new FakeClock(now);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        catalog.RegisterCourse(episodeId, courseId);

        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var assignment = ASSIGNMENT.Create(episodeId, "การบ้าน 1", "คำอธิบาย", 7, 20, "pdf");
        assignmentRepo.Add(assignment);

        var submission = ASSIGNMENT_SUBMISSION.Submit(assignment.ASSIGNMENT_ID, enrollment.ENROLLMENT_ID, "key1", "note1", clock);
        submissionRepo.Add(submission);

        var service = new AssignmentSubmissionService(submissionRepo, assignmentRepo, enrollRepo, catalog, clock);

        var result = await service.GetMySubmissionByAssignmentAsync(userId, assignment.ASSIGNMENT_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal("key1", result.Value.StorageKey);
    }
}

