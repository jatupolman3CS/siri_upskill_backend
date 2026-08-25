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

        public Task<PagedResult<ASSIGNMENT_SUBMISSION>> ListByAssignmentAsync(Guid assignmentId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var items = Submissions.Values.Where(s => s.ASSIGNMENT_ID == assignmentId).ToList();
            return Task.FromResult(PagedResult<ASSIGNMENT_SUBMISSION>.Create(items, items.Count, page, pageSize));
        }

        public void Add(ASSIGNMENT_SUBMISSION submission) => Submissions[submission.ASSIGNMENT_SUBMISSION_ID] = submission;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var userId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var assignment = ASSIGNMENT.Create(Guid.NewGuid(), "การบ้าน 1", "คำอธิบาย", 7, 20, "pdf");
        assignmentRepo.Add(assignment);

        var service = new AssignmentSubmissionService(submissionRepo, assignmentRepo, enrollRepo, clock);
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
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var assignment = ASSIGNMENT.Create(Guid.NewGuid(), "การบ้าน 1", "คำอธิบาย", 7, 20, "pdf");
        assignmentRepo.Add(assignment);

        var submission = ASSIGNMENT_SUBMISSION.Submit(assignment.ASSIGNMENT_ID, Guid.NewGuid(), "key", null, clock);
        submissionRepo.Add(submission);

        var service = new AssignmentSubmissionService(submissionRepo, assignmentRepo, enrollRepo, clock);
        var instructorId = Guid.NewGuid();

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
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var assignment = ASSIGNMENT.Create(Guid.NewGuid(), "การบ้าน 1", "คำอธิบาย", 7, 20, "pdf");
        assignmentRepo.Add(assignment);

        var submission = ASSIGNMENT_SUBMISSION.Submit(assignment.ASSIGNMENT_ID, Guid.NewGuid(), "key", null, clock);
        submissionRepo.Add(submission);

        var service = new AssignmentSubmissionService(submissionRepo, assignmentRepo, enrollRepo, clock);
        var instructorId = Guid.NewGuid();

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
        var clock = new FakeClock(DateTime.UtcNow);

        var userId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var submission = ASSIGNMENT_SUBMISSION.Submit(Guid.NewGuid(), enrollment.ENROLLMENT_ID, "key", null, clock);
        submissionRepo.Add(submission);

        var service = new AssignmentSubmissionService(submissionRepo, assignmentRepo, enrollRepo, clock);

        var result = await service.GetByIdAsync(userId, submission.ASSIGNMENT_SUBMISSION_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(submission.ASSIGNMENT_SUBMISSION_ID, result.Value.Id);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCallerIsNotTheOwner_ReturnsNotFound()
    {
        // This is the learner-facing "check my own submission" endpoint only — grading/instructor
        // review is a completely separate route that never calls this method (see
        // AssignmentSubmissionEndpoints' doc comment) — so any non-owner caller must be rejected
        // unconditionally.
        var assignmentRepo = new FakeAssignmentRepository();
        var submissionRepo = new FakeAssignmentSubmissionRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(ownerId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var submission = ASSIGNMENT_SUBMISSION.Submit(Guid.NewGuid(), enrollment.ENROLLMENT_ID, "key", null, clock);
        submissionRepo.Add(submission);

        var service = new AssignmentSubmissionService(submissionRepo, assignmentRepo, enrollRepo, clock);

        var attackerId = Guid.NewGuid();
        var result = await service.GetByIdAsync(attackerId, submission.ASSIGNMENT_SUBMISSION_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
    }
}
