using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Orchestrates the <see cref="QUIZ"/> aggregate for its instructor-authoring endpoints. Every method
/// checks <see cref="ICatalogPriceContract.IsInstructorOwnerOfEpisodeAsync"/> — the route policy
/// (<see cref="AuthorizationPolicyNames.InstructorOnly"/>) only proves "some instructor," not "the
/// instructor who owns this specific course" — without this check, any instructor account could read or
/// edit any other instructor's quiz, answer key included.
/// </summary>
public sealed class QuizService(IQuizRepository quizRepository, ICatalogPriceContract catalogPriceContract)
{
    public async Task<Result<QuizResponse>> CreateAsync(Guid callerUserId, CreateQuizRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await catalogPriceContract.IsInstructorOwnerOfEpisodeAsync(request.EpisodeId, callerUserId, cancellationToken).ConfigureAwait(false))
        {
            // NotFound, not Forbidden: a non-owner shouldn't learn whether this episode even exists,
            // let alone whether it already has a quiz — same enumeration-safety reasoning every other
            // ownership check in this codebase uses.
            return Result.Failure<QuizResponse>(DomainError.NotFound("ไม่พบบทเรียนนี้"));
        }

        var existing = await quizRepository.GetByEpisodeIdAsync(request.EpisodeId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result.Failure<QuizResponse>(DomainError.Conflict("มีแบบทดสอบสำหรับบทเรียนนี้อยู่แล้ว"));
        }

        var quiz = QUIZ.Create(request.EpisodeId, request.Title, request.PassingScorePercent, request.MaxAttempts);
        quizRepository.Add(quiz);
        await quizRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(quiz));
    }

    public async Task<Result<QuizResponse>> GetByIdAsync(Guid callerUserId, Guid quizId, CancellationToken cancellationToken)
    {
        var quiz = await quizRepository.GetByIdAsync(quizId, cancellationToken).ConfigureAwait(false);
        if (quiz is null)
        {
            return Result.Failure<QuizResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        if (!await catalogPriceContract.IsInstructorOwnerOfEpisodeAsync(quiz.EPISODE_ID, callerUserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<QuizResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        return Result.Success(ToResponse(quiz));
    }

    public async Task<Result<QuizQuestionResponse>> AddQuestionAsync(Guid callerUserId, Guid quizId, AddQuizQuestionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var quiz = await quizRepository.GetByIdAsync(quizId, cancellationToken).ConfigureAwait(false);
        if (quiz is null)
        {
            return Result.Failure<QuizQuestionResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        if (!await catalogPriceContract.IsInstructorOwnerOfEpisodeAsync(quiz.EPISODE_ID, callerUserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<QuizQuestionResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        var question = quiz.AddQuestion(request.Type, request.Text, request.Explanation, request.Points);
        await quizRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToQuestionResponse(question));
    }

    public async Task<Result<QuizOptionResponse>> AddOptionAsync(Guid callerUserId, Guid quizId, Guid questionId, AddQuizOptionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var quiz = await quizRepository.GetByIdAsync(quizId, cancellationToken).ConfigureAwait(false);
        if (quiz is null)
        {
            return Result.Failure<QuizOptionResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        if (!await catalogPriceContract.IsInstructorOwnerOfEpisodeAsync(quiz.EPISODE_ID, callerUserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<QuizOptionResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        var question = quiz.QUESTIONS.FirstOrDefault(q => q.QUIZ_QUESTION_ID == questionId);
        if (question is null)
        {
            return Result.Failure<QuizOptionResponse>(DomainError.NotFound("ไม่พบคำถามในแบบทดสอบ"));
        }

        var option = question.AddOption(request.Text, request.IsCorrect);
        await quizRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new QuizOptionResponse(option.QUIZ_OPTION_ID, option.TEXT, option.IS_CORRECT, option.SORT_ORDER));
    }

    public async Task<Result<QuizResponse>> ActivateAsync(Guid callerUserId, Guid quizId, CancellationToken cancellationToken)
    {
        var quiz = await quizRepository.GetByIdAsync(quizId, cancellationToken).ConfigureAwait(false);
        if (quiz is null)
        {
            return Result.Failure<QuizResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        if (!await catalogPriceContract.IsInstructorOwnerOfEpisodeAsync(quiz.EPISODE_ID, callerUserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<QuizResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        quiz.Activate();
        await quizRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(quiz));
    }

    public async Task<Result<QuizResponse>> DeactivateAsync(Guid callerUserId, Guid quizId, CancellationToken cancellationToken)
    {
        var quiz = await quizRepository.GetByIdAsync(quizId, cancellationToken).ConfigureAwait(false);
        if (quiz is null)
        {
            return Result.Failure<QuizResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        if (!await catalogPriceContract.IsInstructorOwnerOfEpisodeAsync(quiz.EPISODE_ID, callerUserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<QuizResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        quiz.Deactivate();
        await quizRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(quiz));
    }

    private static QuizResponse ToResponse(QUIZ quiz) =>
        new(
            quiz.QUIZ_ID,
            quiz.EPISODE_ID,
            quiz.TITLE,
            quiz.PASSING_SCORE_PERCENT,
            quiz.MAX_ATTEMPTS,
            quiz.IS_ACTIVE,
            quiz.QUESTIONS.OrderBy(q => q.SORT_ORDER).Select(ToQuestionResponse).ToList());

    private static QuizQuestionResponse ToQuestionResponse(QUIZ_QUESTION q) =>
        new(
            q.QUIZ_QUESTION_ID,
            q.TYPE,
            q.TEXT,
            q.EXPLANATION,
            q.POINTS,
            q.SORT_ORDER,
            q.OPTIONS.OrderBy(o => o.SORT_ORDER).Select(o => new QuizOptionResponse(o.QUIZ_OPTION_ID, o.TEXT, o.IS_CORRECT, o.SORT_ORDER)).ToList());
}
