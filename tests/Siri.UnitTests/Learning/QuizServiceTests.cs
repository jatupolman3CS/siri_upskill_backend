using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

public sealed class QuizServiceTests
{
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

    /// <summary>Owner is whichever instructor id is registered against an episode id — defaults to
    /// "no owner" (false) for any episode not explicitly registered, so tests that want the ownership
    /// check to pass must opt in via <see cref="RegisterOwner"/>.</summary>
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
    public async Task CreateAsync_CreatesQuizWithQuestions()
    {
        var repo = new FakeQuizRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var instructorId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, instructorId);
        var service = new QuizService(repo, catalog, enrollments);

        var request = new CreateQuizRequest(episodeId, "บททดสอบท้ายบท", 80, 3);

        var result = await service.CreateAsync(instructorId, request, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("บททดสอบท้ายบท", result.Value.Title);
        Assert.Equal(80, result.Value.PassingScorePercent);
        Assert.Equal(3, result.Value.MaxAttempts);
        Assert.False(result.Value.IsActive);
        Assert.Single(repo.Quizzes);
    }

    [Fact]
    public async Task CreateAsync_WhenCallerDoesNotOwnEpisode_ReturnsNotFound()
    {
        var repo = new FakeQuizRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var episodeId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, Guid.NewGuid()); // owned by someone else
        var service = new QuizService(repo, catalog, enrollments);

        var attackerId = Guid.NewGuid();
        var request = new CreateQuizRequest(episodeId, "บททดสอบท้ายบท", 80, 3);

        var result = await service.CreateAsync(attackerId, request, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(repo.Quizzes);
    }

    [Fact]
    public async Task AddQuestionAndOption_AddsToQuizHierarchy()
    {
        var repo = new FakeQuizRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var instructorId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, instructorId);
        var service = new QuizService(repo, catalog, enrollments);

        var createResult = await service.CreateAsync(instructorId, new CreateQuizRequest(episodeId, "Quiz 1", 70, 2), CancellationToken.None);
        var quizId = createResult.Value.Id;

        var qResult = await service.AddQuestionAsync(instructorId, quizId, new AddQuizQuestionRequest(QuizQuestionType.SingleChoice, "ข้อใดถูกต้อง?", "คำอธิบาย", 10), CancellationToken.None);
        Assert.True(qResult.IsSuccess);
        var questionId = qResult.Value.Id;

        var optResult = await service.AddOptionAsync(instructorId, quizId, questionId, new AddQuizOptionRequest("ตัวเลือก ก (ถูกต้อง)", true), CancellationToken.None);
        Assert.True(optResult.IsSuccess);
        Assert.True(optResult.Value.IsCorrect);

        var quiz = repo.Quizzes[quizId];
        Assert.Single(quiz.QUESTIONS);
        Assert.Single(quiz.QUESTIONS.First().OPTIONS);
    }

    [Fact]
    public async Task AddQuestionAsync_WhenCallerDoesNotOwnQuiz_ReturnsNotFound()
    {
        var repo = new FakeQuizRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var ownerId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, ownerId);
        var service = new QuizService(repo, catalog, enrollments);

        var createResult = await service.CreateAsync(ownerId, new CreateQuizRequest(episodeId, "Quiz 1", 70, 2), CancellationToken.None);
        var quizId = createResult.Value.Id;

        var attackerId = Guid.NewGuid();
        var result = await service.AddQuestionAsync(attackerId, quizId, new AddQuizQuestionRequest(QuizQuestionType.SingleChoice, "ข้อใดถูกต้อง?", "คำอธิบาย", 10), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(repo.Quizzes[quizId].QUESTIONS);
    }

    [Fact]
    public async Task ActivateAndDeactivate_UpdatesActiveStatus()
    {
        var repo = new FakeQuizRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var instructorId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, instructorId);
        var service = new QuizService(repo, catalog, enrollments);

        var createResult = await service.CreateAsync(instructorId, new CreateQuizRequest(episodeId, "Quiz 1", 70, 2), CancellationToken.None);
        var quizId = createResult.Value.Id;

        var activateResult = await service.ActivateAsync(instructorId, quizId, CancellationToken.None);
        Assert.True(activateResult.IsSuccess);
        Assert.True(activateResult.Value.IsActive);

        var deactivateResult = await service.DeactivateAsync(instructorId, quizId, CancellationToken.None);
        Assert.True(deactivateResult.IsSuccess);
        Assert.False(deactivateResult.Value.IsActive);
    }

    [Fact]
    public async Task ActivateAsync_WhenCallerDoesNotOwnQuiz_ReturnsNotFound()
    {
        var repo = new FakeQuizRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var ownerId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, ownerId);
        var service = new QuizService(repo, catalog, enrollments);

        var createResult = await service.CreateAsync(ownerId, new CreateQuizRequest(episodeId, "Quiz 1", 70, 2), CancellationToken.None);
        var quizId = createResult.Value.Id;

        var attackerId = Guid.NewGuid();
        var result = await service.ActivateAsync(attackerId, quizId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
        Assert.False(repo.Quizzes[quizId].IS_ACTIVE);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCallerIsOwner_ReturnsQuiz()
    {
        var repo = new FakeQuizRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var instructorId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, instructorId);
        var service = new QuizService(repo, catalog, enrollments);

        var createResult = await service.CreateAsync(instructorId, new CreateQuizRequest(episodeId, "Quiz 1", 70, 2), CancellationToken.None);
        var quizId = createResult.Value.Id;

        var result = await service.GetByIdAsync(instructorId, quizId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(quizId, result.Value.Id);
    }

    [Fact]
    public async Task GetByIdAsync_WhenCallerIsNotEpisodeOwner_ReturnsNotFound()
    {
        var repo = new FakeQuizRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var ownerId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        catalog.RegisterOwner(episodeId, ownerId);
        var service = new QuizService(repo, catalog, enrollments);

        var createResult = await service.CreateAsync(ownerId, new CreateQuizRequest(episodeId, "Quiz 1", 70, 2), CancellationToken.None);
        var quizId = createResult.Value.Id;

        var attackerId = Guid.NewGuid();
        var result = await service.GetByIdAsync(attackerId, quizId, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetByEpisodeForLearnerAsync_HidesIsCorrectAndReturnsLearnerQuiz()
    {
        var repo = new FakeQuizRepository();
        var catalog = new FakeCatalogPriceContract();
        var enrollments = new FakeEnrollmentRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var instructorId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var learnerUserId = Guid.NewGuid();

        catalog.RegisterOwner(episodeId, instructorId);
        catalog.RegisterCourse(episodeId, courseId);
        var service = new QuizService(repo, catalog, enrollments);

        var createResult = await service.CreateAsync(instructorId, new CreateQuizRequest(episodeId, "Final Quiz", 80, 2), CancellationToken.None);
        var quizId = createResult.Value.Id;

        var qResult = await service.AddQuestionAsync(instructorId, quizId, new AddQuizQuestionRequest(QuizQuestionType.SingleChoice, "2+2 = ?", "คำอธิบายข้อนี้", 10), CancellationToken.None);
        var questionId = qResult.Value.Id;

        await service.AddOptionAsync(instructorId, quizId, questionId, new AddQuizOptionRequest("4", true), CancellationToken.None);
        await service.AddOptionAsync(instructorId, quizId, questionId, new AddQuizOptionRequest("5", false), CancellationToken.None);
        await service.ActivateAsync(instructorId, quizId, CancellationToken.None);

        // Not enrolled or preview -> Forbidden
        var forbiddenResult = await service.GetByEpisodeForLearnerAsync(learnerUserId, episodeId, CancellationToken.None);
        Assert.False(forbiddenResult.IsSuccess);
        Assert.Equal("forbidden", forbiddenResult.Error.Code);

        // Active enrollment
        var enrollment = ENROLLMENT.Create(learnerUserId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollments.Add(enrollment);

        var successResult = await service.GetByEpisodeForLearnerAsync(learnerUserId, episodeId, CancellationToken.None);
        Assert.True(successResult.IsSuccess);
        Assert.Equal("Final Quiz", successResult.Value.Title);
        Assert.Single(successResult.Value.Questions);
        Assert.Equal(2, successResult.Value.Questions[0].Options.Count);
        // Learner response model doesn't expose IsCorrect property
    }
}

