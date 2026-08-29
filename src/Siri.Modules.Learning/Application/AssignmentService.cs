using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Orchestrates the <see cref="ASSIGNMENT"/> aggregate for its instructor-authoring and learner endpoints.
/// </summary>
public sealed class AssignmentService(
    IAssignmentRepository assignmentRepository,
    ICatalogPriceContract catalogPriceContract,
    IEnrollmentRepository enrollmentRepository)
{
    public async Task<Result<AssignmentResponse>> GetByEpisodeForLearnerAsync(Guid callerUserId, Guid episodeId, CancellationToken cancellationToken)
    {
        var assignment = await assignmentRepository.GetByEpisodeIdAsync(episodeId, cancellationToken).ConfigureAwait(false);
        if (assignment is null)
        {
            return Result.Failure<AssignmentResponse>(DomainError.NotFound("ไม่พบการบ้านสำหรับบทเรียนนี้"));
        }

        var courseId = await catalogPriceContract.GetCourseIdForEpisodeAsync(episodeId, cancellationToken).ConfigureAwait(false);
        if (courseId is null)
        {
            return Result.Failure<AssignmentResponse>(DomainError.NotFound("ไม่พบคอร์สของบทเรียนนี้"));
        }

        var isPreview = await catalogPriceContract.IsEpisodeFreePreviewAsync(episodeId, cancellationToken).ConfigureAwait(false);
        if (!isPreview)
        {
            var enrollment = await enrollmentRepository.GetByUserAndCourseAsync(callerUserId, courseId.Value, cancellationToken).ConfigureAwait(false);
            if (enrollment is null || enrollment.STATUS != EnrollmentStatus.Active)
            {
                return Result.Failure<AssignmentResponse>(DomainError.Forbidden("คุณต้องลงทะเบียนเรียนคอร์สนี้ก่อนเข้าถึงการบ้าน"));
            }
        }

        return Result.Success(ToResponse(assignment));
    }

    public async Task<Result<AssignmentResponse>> CreateAsync(Guid callerUserId, CreateAssignmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!await catalogPriceContract.IsInstructorOwnerOfEpisodeAsync(request.EpisodeId, callerUserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<AssignmentResponse>(DomainError.NotFound("ไม่พบบทเรียนนี้"));
        }

        var existing = await assignmentRepository.GetByEpisodeIdAsync(request.EpisodeId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result.Failure<AssignmentResponse>(DomainError.Conflict("มีการบ้านสำหรับบทเรียนนี้อยู่แล้ว"));
        }

        var assignment = ASSIGNMENT.Create(
            request.EpisodeId,
            request.Title,
            request.Instructions,
            request.DueDays,
            request.MaxFileSizeMb,
            request.AllowedExtensions);

        assignmentRepository.Add(assignment);
        await assignmentRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(assignment));
    }

    public async Task<Result<AssignmentResponse>> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken)
    {
        var assignment = await assignmentRepository.GetByIdAsync(assignmentId, cancellationToken).ConfigureAwait(false);
        if (assignment is null)
        {
            return Result.Failure<AssignmentResponse>(DomainError.NotFound("ไม่พบการบ้าน"));
        }

        return Result.Success(ToResponse(assignment));
    }

    public async Task<Result<AssignmentResponse>> UpdateAsync(Guid callerUserId, Guid assignmentId, UpdateAssignmentRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var assignment = await assignmentRepository.GetByIdAsync(assignmentId, cancellationToken).ConfigureAwait(false);
        if (assignment is null)
        {
            return Result.Failure<AssignmentResponse>(DomainError.NotFound("ไม่พบการบ้าน"));
        }

        if (!await catalogPriceContract.IsInstructorOwnerOfEpisodeAsync(assignment.EPISODE_ID, callerUserId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<AssignmentResponse>(DomainError.NotFound("ไม่พบการบ้าน"));
        }

        assignment.UpdateDetails(
            request.Title,
            request.Instructions,
            request.DueDays,
            request.MaxFileSizeMb,
            request.AllowedExtensions);

        await assignmentRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(assignment));
    }

    private static AssignmentResponse ToResponse(ASSIGNMENT a) =>
        new(
            a.ASSIGNMENT_ID,
            a.EPISODE_ID,
            a.TITLE,
            a.INSTRUCTIONS,
            a.DUE_DAYS,
            a.MAX_FILE_SIZE_MB,
            a.ALLOWED_EXTENSIONS);
}

