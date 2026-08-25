using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Orchestrates the <see cref="Domain.ASSIGNMENT_SUBMISSION"/> aggregate.
/// </summary>
public sealed class AssignmentSubmissionService(
    IAssignmentSubmissionRepository submissionRepository,
    IAssignmentRepository assignmentRepository,
    IEnrollmentRepository enrollmentRepository,
    IClock clock)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<Result<AssignmentSubmissionResponse>> SubmitAsync(Guid callerUserId, SubmitAssignmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (callerUserId == Guid.Empty)
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.Forbidden("User must be authenticated."));
        }

        var enrollment = await enrollmentRepository.GetByIdAsync(request.EnrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != callerUserId)
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์ส่งการบ้านนี้"));
        }

        var assignment = await assignmentRepository.GetByIdAsync(request.AssignmentId, cancellationToken).ConfigureAwait(false);
        if (assignment is null)
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.NotFound("ไม่พบการบ้าน"));
        }

        var submission = ASSIGNMENT_SUBMISSION.Submit(
            request.AssignmentId,
            request.EnrollmentId,
            request.StorageKey,
            request.Note,
            clock);

        submissionRepository.Add(submission);
        await submissionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(submission));
    }

    public async Task<Result<AssignmentSubmissionResponse>> GetByIdAsync(Guid callerUserId, Guid submissionId, CancellationToken cancellationToken)
    {
        var submission = await submissionRepository.GetByIdAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.NotFound("ไม่พบข้อมูลการส่งการบ้าน"));
        }

        // This is the learner-facing "check my own submission" endpoint (AssignmentSubmissionEndpoints'
        // learnerGroup, plain .RequireAuthorization() — instructor review/grading is a completely
        // separate route on instructorGroup and never calls this method), so ownership is unconditional:
        // no caller other than the submission's own enrollment owner has any legitimate reason to reach
        // this branch. NotFound (not Forbidden) matches this codebase's ownership-check convention
        // (Enrollment/EpisodeProgress/QuizAttemptService) — a non-owner gets the same response as a
        // nonexistent id.
        var enrollment = await enrollmentRepository.GetByIdAsync(submission.ENROLLMENT_ID, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != callerUserId)
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.NotFound("ไม่พบข้อมูลการส่งการบ้าน"));
        }

        return Result.Success(ToResponse(submission));
    }

    public async Task<PagedResult<AssignmentSubmissionResponse>> ListByAssignmentAsync(Guid assignmentId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > MaxPageSize ? DefaultPageSize : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var pagedSubmissions = await submissionRepository.ListByAssignmentAsync(assignmentId, effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);
        var mappedItems = pagedSubmissions.Items.Select(ToResponse).ToList();

        return PagedResult<AssignmentSubmissionResponse>.Create(
            mappedItems,
            pagedSubmissions.TotalCount,
            pagedSubmissions.Page,
            pagedSubmissions.PageSize);
    }

    public async Task<Result<AssignmentSubmissionResponse>> GradeAsync(Guid callerUserId, Guid submissionId, GradeAssignmentSubmissionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var submission = await submissionRepository.GetByIdAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.NotFound("ไม่พบข้อมูลการส่งการบ้าน"));
        }

        if (request.Status == AssignmentSubmissionStatus.Graded)
        {
            var score = request.Score ?? 0m;
            submission.Grade(score, request.Feedback, callerUserId, clock);
        }
        else if (request.Status == AssignmentSubmissionStatus.Rejected)
        {
            submission.Reject(request.Feedback, callerUserId, clock);
        }
        else
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.Validation("สถานะการตรวจไม่ถูกต้อง"));
        }

        await submissionRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(submission));
    }

    private static AssignmentSubmissionResponse ToResponse(ASSIGNMENT_SUBMISSION s) =>
        new(
            s.ASSIGNMENT_SUBMISSION_ID,
            s.ASSIGNMENT_ID,
            s.ENROLLMENT_ID,
            s.STORAGE_KEY,
            s.NOTE,
            s.SUBMITTED_AT_UTC,
            s.STATUS,
            s.SCORE,
            s.FEEDBACK,
            s.GRADED_BY_USER_ID,
            s.GRADED_AT_UTC);
}
