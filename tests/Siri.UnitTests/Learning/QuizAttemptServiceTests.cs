using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

public sealed class QuizAttemptServiceTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        public readonly Dictionary<Guid, Guid> EpisodeToCourse = [];

        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(new Dictionary<Guid, CoursePriceInfo>());

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(EpisodeToCourse.TryGetValue(episodeId, out var courseId) ? (Guid?)courseId : null);

        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }


    private sealed class FakeQuizRepository : IQuizRepository
    {
        public readonly Dictionary<Guid, QUIZ> Quizzes = [];

        public Task<QUIZ?> GetByIdAsync(Guid quizId, CancellationToken cancellationToken) =>
            Task.FromResult(Quizzes.TryGetValue(quizId, out var quiz) ? quiz : null);

        public Task<QUIZ?> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(Quizzes.Values.FirstOrDefault(q => q.EPISODE_ID == episodeId));

        public void Add(QUIZ quiz) => Quizzes[quiz.QUIZ_ID] = quiz;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeQuizAttemptRepository : IQuizAttemptRepository
    {
        public readonly Dictionary<Guid, QUIZ_ATTEMPT> Attempts = [];

        public Task<QUIZ_ATTEMPT?> GetByIdAsync(Guid attemptId, CancellationToken cancellationToken) =>
            Task.FromResult(Attempts.TryGetValue(attemptId, out var attempt) ? attempt : null);

        public Task<IReadOnlyList<QUIZ_ATTEMPT>> ListByEnrollmentAndQuizAsync(Guid enrollmentId, Guid quizId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<QUIZ_ATTEMPT>>(Attempts.Values.Where(a => a.ENROLLMENT_ID == enrollmentId && a.QUIZ_ID == quizId).ToList());

        public void Add(QUIZ_ATTEMPT attempt) => Attempts[attempt.QUIZ_ATTEMPT_ID] = attempt;

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
    public async Task StartAsync_WhenQuizIsActive_StartsAttempt()
    {
        var quizRepo = new FakeQuizRepository();
        var attemptRepo = new FakeQuizAttemptRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var catalog = new FakeCatalogPriceContract();
        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var episodeId = Guid.NewGuid();
        catalog.EpisodeToCourse[episodeId] = courseId;
        var quiz = QUIZ.Create(episodeId, "แบบทดสอบ", 80, 3);
        quiz.Activate();
        quizRepo.Add(quiz);

        var service = new QuizAttemptService(attemptRepo, quizRepo, enrollRepo, catalog, clock);

        var result = await service.StartAsync(userId, new StartQuizAttemptRequest(quiz.QUIZ_ID, enrollment.ENROLLMENT_ID), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.AttemptNo);
        Assert.False(result.Value.IsPassed);
        Assert.Equal(now, result.Value.StartedAtUtc);
        Assert.Single(attemptRepo.Attempts);
    }

    [Fact]
    public async Task StartAsync_WhenExceedsMaxAttempts_ReturnsConflict()
    {
        var quizRepo = new FakeQuizRepository();
        var attemptRepo = new FakeQuizAttemptRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var catalog = new FakeCatalogPriceContract();
        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var episodeId = Guid.NewGuid();
        catalog.EpisodeToCourse[episodeId] = courseId;
        var quiz = QUIZ.Create(episodeId, "แบบทดสอบ", 80, 1); // Max 1 attempt
        quiz.Activate();
        quizRepo.Add(quiz);

        var service = new QuizAttemptService(attemptRepo, quizRepo, enrollRepo, catalog, clock);

        var firstResult = await service.StartAsync(userId, new StartQuizAttemptRequest(quiz.QUIZ_ID, enrollment.ENROLLMENT_ID), CancellationToken.None);
        Assert.True(firstResult.IsSuccess);

        var secondResult = await service.StartAsync(userId, new StartQuizAttemptRequest(quiz.QUIZ_ID, enrollment.ENROLLMENT_ID), CancellationToken.None);
        Assert.False(secondResult.IsSuccess);
        Assert.Equal("conflict", secondResult.Error.Code);
    }

    [Fact]
    public async Task AnswerAndSubmit_CalculatesScoreAndPassStatus()
    {
        var quizRepo = new FakeQuizRepository();
        var attemptRepo = new FakeQuizAttemptRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var catalog = new FakeCatalogPriceContract();
        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var episodeId = Guid.NewGuid();
        catalog.EpisodeToCourse[episodeId] = courseId;
        var quiz = QUIZ.Create(episodeId, "แบบทดสอบ", 100, 3);
        var q1 = quiz.AddQuestion(QuizQuestionType.SingleChoice, "ข้อ 1", null, 10);
        var opt1 = q1.AddOption("ก (ถูก)", true);
        q1.AddOption("ข (ผิด)", false);
        quiz.Activate();
        quizRepo.Add(quiz);

        var service = new QuizAttemptService(attemptRepo, quizRepo, enrollRepo, catalog, clock);

        var startResult = await service.StartAsync(userId, new StartQuizAttemptRequest(quiz.QUIZ_ID, enrollment.ENROLLMENT_ID), CancellationToken.None);
        var attemptId = startResult.Value.Id;

        var answerResult = await service.AnswerAsync(userId, attemptId, new SubmitQuizAnswerRequest(q1.QUIZ_QUESTION_ID, [opt1.QUIZ_OPTION_ID]), CancellationToken.None);
        Assert.True(answerResult.IsSuccess);
        // Correctness is hidden during attempt to prevent answer leaking
        Assert.False(answerResult.Value.IsCorrect);

        var submitResult = await service.SubmitAsync(userId, attemptId, CancellationToken.None);
        Assert.True(submitResult.IsSuccess);
        Assert.Equal(100m, submitResult.Value.ScorePercent);
        Assert.True(submitResult.Value.IsPassed);
        Assert.Equal(now, submitResult.Value.SubmittedAtUtc);
        Assert.True(submitResult.Value.Answers[0].IsCorrect);
    }

    [Fact]
    public async Task StartAsync_WhenEnrollmentBelongsToDifferentUser_ReturnsForbidden()
    {
        var quizRepo = new FakeQuizRepository();
        var attemptRepo = new FakeQuizAttemptRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(ownerId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var episodeId = Guid.NewGuid();
        catalog.EpisodeToCourse[episodeId] = courseId;
        var quiz = QUIZ.Create(episodeId, "แบบทดสอบ", 80, 3);
        quiz.Activate();
        quizRepo.Add(quiz);

        var service = new QuizAttemptService(attemptRepo, quizRepo, enrollRepo, catalog, clock);

        var attackerId = Guid.NewGuid();
        var result = await service.StartAsync(attackerId, new StartQuizAttemptRequest(quiz.QUIZ_ID, enrollment.ENROLLMENT_ID), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Empty(attemptRepo.Attempts);
    }

    [Fact]
    public async Task StartAsync_WhenEnrollmentIsForADifferentCourse_ReturnsForbidden()
    {
        // A learner genuinely owns this enrollment (COURSE A) but tries to use it to start a quiz
        // that belongs to COURSE B's episode — proves the enrollment-course-matches-quiz-course check,
        // not just the enrollment-belongs-to-caller check.
        var quizRepo = new FakeQuizRepository();
        var attemptRepo = new FakeQuizAttemptRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var userId = Guid.NewGuid();
        var enrolledCourseId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, enrolledCourseId, null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var otherCourseEpisodeId = Guid.NewGuid();
        catalog.EpisodeToCourse[otherCourseEpisodeId] = Guid.NewGuid(); // a different course than enrolledCourseId
        var quiz = QUIZ.Create(otherCourseEpisodeId, "แบบทดสอบคอร์สอื่น", 80, 3);
        quiz.Activate();
        quizRepo.Add(quiz);

        var service = new QuizAttemptService(attemptRepo, quizRepo, enrollRepo, catalog, clock);

        var result = await service.StartAsync(userId, new StartQuizAttemptRequest(quiz.QUIZ_ID, enrollment.ENROLLMENT_ID), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Empty(attemptRepo.Attempts);
    }

    [Fact]
    public async Task GetByIdAsync_WhenAttemptBelongsToDifferentUser_ReturnsForbidden()
    {
        var quizRepo = new FakeQuizRepository();
        var attemptRepo = new FakeQuizAttemptRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(ownerId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var attempt = QUIZ_ATTEMPT.Start(Guid.NewGuid(), enrollment.ENROLLMENT_ID, 1, clock);
        attemptRepo.Add(attempt);

        var service = new QuizAttemptService(attemptRepo, quizRepo, enrollRepo, catalog, clock);

        var attackerId = Guid.NewGuid();
        var result = await service.GetByIdAsync(attackerId, attempt.QUIZ_ATTEMPT_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task ListAttemptsByQuizAsync_ReturnsAttemptsForUser()
    {
        var quizRepo = new FakeQuizRepository();
        var attemptRepo = new FakeQuizAttemptRepository();
        var enrollRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        catalog.EpisodeToCourse[episodeId] = courseId;

        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollRepo.Add(enrollment);

        var quiz = QUIZ.Create(episodeId, "แบบทดสอบ", 80, 3);
        quiz.Activate();
        quizRepo.Add(quiz);

        var attempt1 = QUIZ_ATTEMPT.Start(quiz.QUIZ_ID, enrollment.ENROLLMENT_ID, 1, clock);
        var attempt2 = QUIZ_ATTEMPT.Start(quiz.QUIZ_ID, enrollment.ENROLLMENT_ID, 2, clock);
        attemptRepo.Add(attempt1);
        attemptRepo.Add(attempt2);

        var service = new QuizAttemptService(attemptRepo, quizRepo, enrollRepo, catalog, clock);

        var result = await service.ListAttemptsByQuizAsync(userId, quiz.QUIZ_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal(1, result.Value[0].AttemptNo);
        Assert.Equal(2, result.Value[1].AttemptNo);
    }
}

