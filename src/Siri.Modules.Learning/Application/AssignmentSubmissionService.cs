using Siri.Modules.Catalog.Contracts;
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
    ICatalogPriceContract catalogPriceContract,
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

        var courseId = await catalogPriceContract.GetCourseIdForEpisodeAsync(assignment.EPISODE_ID, cancellationToken).ConfigureAwait(false);
        if (courseId is null || courseId.Value != enrollment.COURSE_ID)
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์ส่งการบ้านนี้"));
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

        var enrollment = await enrollmentRepository.GetByIdAsync(submission.ENROLLMENT_ID, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != callerUserId)
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.NotFound("ไม่พบข้อมูลการส่งการบ้าน"));
        }

        return Result.Success(ToResponse(submission));
    }

    public async Task<Result<AssignmentSubmissionResponse?>> GetMySubmissionByAssignmentAsync(Guid callerUserId, Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await assignmentRepository.GetByIdAsync(assignmentId, cancellationToken).ConfigureAwait(false);
        if (assignment is null)
        {
            return Result.Failure<AssignmentSubmissionResponse?>(DomainError.NotFound("ไม่พบการบ้าน"));
        }

        var courseId = await catalogPriceContract.GetCourseIdForEpisodeAsync(assignment.EPISODE_ID, cancellationToken).ConfigureAwait(false);
        if (courseId is null)
        {
            return Result.Failure<AssignmentSubmissionResponse?>(DomainError.NotFound("ไม่พบคอร์ส"));
        }

        var enrollment = await enrollmentRepository.GetByUserAndCourseAsync(callerUserId, courseId.Value, cancellationToken).ConfigureAwait(false);
        if (enrollment is null)
        {
            return Result.Success<AssignmentSubmissionResponse?>(null);
        }

        var submission = await submissionRepository.GetLatestByEnrollmentAndAssignmentAsync(enrollment.ENROLLMENT_ID, assignmentId, cancellationToken).ConfigureAwait(false);
        return Result.Success(submission is not null ? ToResponse(submission) : null);
    }

    public async Task<Result<PagedResult<AssignmentSubmissionResponse>>> ListByAssignmentAsync(Guid callerUserId, Guid assignmentId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var assignment = await assignmentRepository.GetByIdAsync(assignmentId, cancellationToken).ConfigureAwait(false);
        if (assignment is null)
        {
            return Result.Failure<PagedResult<AssignmentSubmissionResponse>>(DomainError.NotFound("ไม่พบการบ้าน"));
        }

        if (!await catalogPriceContract.IsInstructorOwnerOfEpisodeAsync(assignment.EPISODE_ID, callerUserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<PagedResult<AssignmentSubmissionResponse>>(DomainError.NotFound("ไม่พบการบ้าน"));
        }

        var effectivePageSize = pageSize is <= 0 or > MaxPageSize ? DefaultPageSize : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var pagedSubmissions = await submissionRepository.ListByAssignmentAsync(assignmentId, effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);
        var mappedItems = pagedSubmissions.Items.Select(ToResponse).ToList();

        var result = PagedResult<AssignmentSubmissionResponse>.Create(
            mappedItems,
            pagedSubmissions.TotalCount,
            pagedSubmissions.Page,
            pagedSubmissions.PageSize);

        return Result.Success(result);
    }

    public async Task<Result<AssignmentSubmissionResponse>> GradeAsync(Guid callerUserId, Guid submissionId, GradeAssignmentSubmissionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var submission = await submissionRepository.GetByIdAsync(submissionId, cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.NotFound("ไม่พบข้อมูลการส่งการบ้าน"));
        }

        var assignment = await assignmentRepository.GetByIdAsync(submission.ASSIGNMENT_ID, cancellationToken).ConfigureAwait(false);
        if (assignment is null)
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.NotFound("ไม่พบการบ้าน"));
        }

        if (!await catalogPriceContract.IsInstructorOwnerOfEpisodeAsync(assignment.EPISODE_ID, callerUserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<AssignmentSubmissionResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์ตรวจการบ้านนี้"));
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

