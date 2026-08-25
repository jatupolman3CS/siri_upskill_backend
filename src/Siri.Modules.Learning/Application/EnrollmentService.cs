using Microsoft.EntityFrameworkCore;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Business logic for <see cref="ENROLLMENT"/>, backing <c>EnrollmentEndpoints</c>.
/// </summary>
public sealed class EnrollmentService(IEnrollmentRepository repository, IClock clock)
{
    public async Task<Result<EnrollmentResponse>> CreateAsync(CreateEnrollmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await repository.GetByUserAndCourseAsync(command.UserId, command.CourseId, cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.STATUS == EnrollmentStatus.Active)
        {
            return Result.Failure<EnrollmentResponse>(DomainError.Conflict("ผู้เรียนมีสิทธิ์การเข้าเรียนคอร์สนี้อยู่แล้ว"));
        }

        var enrollment = ENROLLMENT.Create(
            command.UserId,
            command.CourseId,
            command.OrderId,
            command.Source,
            command.ExpiresAtUtc,
            clock);

        repository.Add(enrollment);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(enrollment));
    }

    public async Task<PagedResult<EnrollmentResponse>> ListAsync(
        Guid? courseId,
        EnrollmentStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = repository.Query();

        if (courseId.HasValue)
        {
            query = query.Where(e => e.COURSE_ID == courseId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(e => e.STATUS == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var entities = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = entities.Select(ToResponse).ToList();
        return PagedResult<EnrollmentResponse>.Create(items, totalCount, page, pageSize);
    }

    public async Task<PagedResult<EnrollmentResponse>> ListForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = repository.Query().Where(e => e.USER_ID == userId);
        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var entities = await query
            .OrderByDescending(e => e.LAST_ACCESSED_AT_UTC ?? e.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = entities.Select(ToResponse).ToList();
        return PagedResult<EnrollmentResponse>.Create(items, totalCount, page, pageSize);
    }

    public async Task<Result<EnrollmentResponse>> GetOwnAsync(Guid userId, Guid enrollmentId, CancellationToken cancellationToken)
    {
        var enrollment = await repository.GetByIdAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != userId)
        {
            return Result.Failure<EnrollmentResponse>(DomainError.NotFound("ไม่พบข้อมูลการลงทะเบียน"));
        }

        return Result.Success(ToResponse(enrollment));
    }

    public async Task<Result<EnrollmentResponse>> UpdateOwnProgressAsync(
        Guid userId,
        Guid enrollmentId,
        UpdateEnrollmentProgressCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var enrollment = await repository.GetByIdAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != userId)
        {
            return Result.Failure<EnrollmentResponse>(DomainError.NotFound("ไม่พบข้อมูลการลงทะเบียน"));
        }

        enrollment.UpdateProgress(command.ProgressPercent, clock);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(enrollment));
    }

    public async Task<Result<EnrollmentResponse>> RevokeAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var enrollment = await repository.GetByIdAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null)
        {
            return Result.Failure<EnrollmentResponse>(DomainError.NotFound("ไม่พบข้อมูลการลงทะเบียน"));
        }

        enrollment.Revoke();
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(enrollment));
    }

    private static EnrollmentResponse ToResponse(ENROLLMENT e) =>
        new(
            e.ENROLLMENT_ID,
            e.USER_ID,
            e.COURSE_ID,
            e.ORDER_ID,
            e.SOURCE,
            e.ENROLLED_AT_UTC,
            e.EXPIRES_AT_UTC,
            e.STATUS,
            e.PROGRESS_PERCENT,
            e.COMPLETED_AT_UTC,
            e.LAST_ACCESSED_AT_UTC);
}
