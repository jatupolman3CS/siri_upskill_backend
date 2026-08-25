using System.Text.Json;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Orchestrates the <see cref="Domain.QUIZ_ATTEMPT"/> aggregate for the learner-facing quiz-taking endpoints.
/// </summary>
public sealed class QuizAttemptService(
    IQuizAttemptRepository attemptRepository,
    IQuizRepository quizRepository,
    IEnrollmentRepository enrollmentRepository,
    ICatalogPriceContract catalogPriceContract,
    IClock clock)
{
    public async Task<Result<QuizAttemptResponse>> StartAsync(Guid callerUserId, StartQuizAttemptRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (callerUserId == Guid.Empty)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.Forbidden("User must be authenticated."));
        }

        var enrollment = await enrollmentRepository.GetByIdAsync(request.EnrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != callerUserId)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์ทำแบบทดสอบนี้"));
        }

        var quiz = await quizRepository.GetByIdAsync(request.QuizId, cancellationToken).ConfigureAwait(false);
        if (quiz is null)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        // The caller genuinely owns request.EnrollmentId (checked above) — but that alone doesn't prove
        // it's an enrollment *in the course this quiz belongs to*. Without this, a learner enrolled in
        // course A could pass their own real EnrollmentId against course B's QuizId and take a quiz for
        // content they never bought.
        var quizCourseId = await catalogPriceContract.GetCourseIdForEpisodeAsync(quiz.EPISODE_ID, cancellationToken).ConfigureAwait(false);
        if (quizCourseId is null || quizCourseId.Value != enrollment.COURSE_ID)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์ทำแบบทดสอบนี้"));
        }

        if (!quiz.IS_ACTIVE)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.Conflict("แบบทดสอบยังไม่เปิดให้ทำ"));
        }

        var previousAttempts = await attemptRepository.ListByEnrollmentAndQuizAsync(request.EnrollmentId, request.QuizId, cancellationToken).ConfigureAwait(false);
        if (previousAttempts.Count >= quiz.MAX_ATTEMPTS)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.Conflict($"คุณได้ทำแบบทดสอบครบจำนวนครั้งสูงสุดแล้ว ({quiz.MAX_ATTEMPTS} ครั้ง)"));
        }

        var attemptNo = previousAttempts.Count + 1;
        var attempt = QUIZ_ATTEMPT.Start(request.QuizId, request.EnrollmentId, attemptNo, clock);

        attemptRepository.Add(attempt);
        await attemptRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(attempt));
    }

    public async Task<Result<QuizAttemptResponse>> GetByIdAsync(Guid callerUserId, Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await attemptRepository.GetByIdAsync(attemptId, cancellationToken).ConfigureAwait(false);
        if (attempt is null)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.NotFound("ไม่พบข้อมูลการทำแบบทดสอบ"));
        }

        var enrollment = await enrollmentRepository.GetByIdAsync(attempt.ENROLLMENT_ID, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != callerUserId)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์เข้าถึงผลการทำแบบทดสอบนี้"));
        }

        return Result.Success(ToResponse(attempt));
    }

    public async Task<Result<QuizAttemptAnswerResponse>> AnswerAsync(Guid callerUserId, Guid attemptId, SubmitQuizAnswerRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var attempt = await attemptRepository.GetByIdAsync(attemptId, cancellationToken).ConfigureAwait(false);
        if (attempt is null)
        {
            return Result.Failure<QuizAttemptAnswerResponse>(DomainError.NotFound("ไม่พบข้อมูลการทำแบบทดสอบ"));
        }

        var enrollment = await enrollmentRepository.GetByIdAsync(attempt.ENROLLMENT_ID, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != callerUserId)
        {
            return Result.Failure<QuizAttemptAnswerResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์ตอบแบบทดสอบนี้"));
        }

        if (attempt.SUBMITTED_AT_UTC.HasValue)
        {
            return Result.Failure<QuizAttemptAnswerResponse>(DomainError.Conflict("แบบทดสอบนี้ถูกส่งแล้ว"));
        }

        var quiz = await quizRepository.GetByIdAsync(attempt.QUIZ_ID, cancellationToken).ConfigureAwait(false);
        if (quiz is null)
        {
            return Result.Failure<QuizAttemptAnswerResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        var question = quiz.QUESTIONS.FirstOrDefault(q => q.QUIZ_QUESTION_ID == request.QuestionId);
        if (question is null)
        {
            return Result.Failure<QuizAttemptAnswerResponse>(DomainError.NotFound("ไม่พบคำถาม"));
        }

        var correctOptionIds = question.OPTIONS.Where(o => o.IS_CORRECT).Select(o => o.QUIZ_OPTION_ID).ToHashSet();
        var selectedSet = request.SelectedOptionIds.ToHashSet();
        var isCorrect = correctOptionIds.SetEquals(selectedSet);

        var json = JsonSerializer.Serialize(request.SelectedOptionIds);
        var answer = attempt.AddAnswer(request.QuestionId, json, isCorrect);
        await attemptRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Hide correctness while attempt is in-progress to prevent answer sniffing
        var responseIsCorrect = attempt.SUBMITTED_AT_UTC.HasValue && isCorrect;
        return Result.Success(new QuizAttemptAnswerResponse(answer.QUIZ_ATTEMPT_ANSWER_ID, answer.QUESTION_ID, request.SelectedOptionIds, responseIsCorrect));
    }

    public async Task<Result<QuizAttemptResponse>> SubmitAsync(Guid callerUserId, Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await attemptRepository.GetByIdAsync(attemptId, cancellationToken).ConfigureAwait(false);
        if (attempt is null)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.NotFound("ไม่พบข้อมูลการทำแบบทดสอบ"));
        }

        var enrollment = await enrollmentRepository.GetByIdAsync(attempt.ENROLLMENT_ID, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != callerUserId)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์ส่งแบบทดสอบนี้"));
        }

        if (attempt.SUBMITTED_AT_UTC.HasValue)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.Conflict("แบบทดสอบนี้ถูกส่งแล้ว"));
        }

        var quiz = await quizRepository.GetByIdAsync(attempt.QUIZ_ID, cancellationToken).ConfigureAwait(false);
        if (quiz is null)
        {
            return Result.Failure<QuizAttemptResponse>(DomainError.NotFound("ไม่พบแบบทดสอบ"));
        }

        var totalQuestions = quiz.QUESTIONS.Count;
        var correctCount = attempt.ANSWERS.Count(a => a.IS_CORRECT);
        var scorePercent = totalQuestions > 0 ? Math.Round((decimal)correctCount / totalQuestions * 100m, 2) : 0m;
        var isPassed = scorePercent >= quiz.PASSING_SCORE_PERCENT;

        attempt.Submit(scorePercent, isPassed, clock);
        await attemptRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(attempt));
    }

    private static QuizAttemptResponse ToResponse(QUIZ_ATTEMPT a) =>
        new(
            a.QUIZ_ATTEMPT_ID,
            a.QUIZ_ID,
            a.ENROLLMENT_ID,
            a.ATTEMPT_NO,
            a.SCORE_PERCENT,
            a.IS_PASSED,
            a.STARTED_AT_UTC,
            a.SUBMITTED_AT_UTC,
            a.ANSWERS.Select(ans =>
            {
                var optionIds = JsonSerializer.Deserialize<List<Guid>>(ans.SELECTED_OPTION_IDS) ?? [];
                // Only reveal isCorrect after attempt submission
                var isCorrect = a.SUBMITTED_AT_UTC.HasValue && ans.IS_CORRECT;
                return new QuizAttemptAnswerResponse(ans.QUIZ_ATTEMPT_ANSWER_ID, ans.QUESTION_ID, optionIds, isCorrect);
            }).ToList());
}
