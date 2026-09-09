using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Business logic for <see cref="ENROLLMENT"/>, backing <c>EnrollmentEndpoints</c>.
/// </summary>
public sealed class EnrollmentService(
    IEnrollmentRepository repository,
    ICertificateRepository certificateRepository,
    IClock clock,
    ICourseSummaryReader courseSummaryReader)
{
    public async Task<Result<EnrollmentResponse>> CreateAsync(CreateEnrollmentCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var existing = await repository.GetByUserAndCourseAsync(command.UserId, command.CourseId, cancellationToken).ConfigureAwait(false);
        if (existing is not null && existing.STATUS == EnrollmentStatus.Active &&
            (!existing.EXPIRES_AT_UTC.HasValue || existing.EXPIRES_AT_UTC.Value > clock.UtcNow))
        {
            return Result.Failure<EnrollmentResponse>(DomainError.Conflict("ผู้เรียนมีสิทธิ์การเข้าเรียนคอร์สนี้อยู่แล้ว"));
        }

        if (existing is not null)
        {
            existing.Reactivate(command.OrderId, command.ExpiresAtUtc, clock);
            await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success(await ToResponseAsync(existing, cancellationToken).ConfigureAwait(false));
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

        return Result.Success(await ToResponseAsync(enrollment, cancellationToken).ConfigureAwait(false));
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

        var items = await ToResponsesAsync(entities, cancellationToken).ConfigureAwait(false);
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

        var items = await ToResponsesAsync(entities, cancellationToken).ConfigureAwait(false);
        return PagedResult<EnrollmentResponse>.Create(items, totalCount, page, pageSize);
    }

    public async Task<Result<EnrollmentResponse>> GetOwnAsync(Guid userId, Guid enrollmentId, CancellationToken cancellationToken)
    {
        var enrollment = await repository.GetByIdAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != userId)
        {
            return Result.Failure<EnrollmentResponse>(DomainError.NotFound("ไม่พบข้อมูลการลงทะเบียน"));
        }

        return Result.Success(await ToResponseAsync(enrollment, cancellationToken).ConfigureAwait(false));
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

        if (enrollment.STATUS != EnrollmentStatus.Active || enrollment.EXPIRES_AT_UTC <= clock.UtcNow)
        {
            return Result.Failure<EnrollmentResponse>(DomainError.Forbidden("Enrollment is not active."));
        }

        enrollment.UpdateProgress(command.ProgressPercent, clock);
        await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (enrollment.PROGRESS_PERCENT >= 100m)
        {
            var existingCert = await certificateRepository.GetByEnrollmentIdAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
            if (existingCert is null)
            {
                var serialNo = $"CERT-{clock.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
                var verifyCode = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                var cert = CERTIFICATE.Create(enrollmentId, serialNo, verifyCode, null, clock);
                certificateRepository.Add(cert);
                await certificateRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success(await ToResponseAsync(enrollment, cancellationToken).ConfigureAwait(false));
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

        return Result.Success(await ToResponseAsync(enrollment, cancellationToken).ConfigureAwait(false));
    }

    private async Task<EnrollmentResponse> ToResponseAsync(ENROLLMENT enrollment, CancellationToken cancellationToken) =>
        (await ToResponsesAsync([enrollment], cancellationToken).ConfigureAwait(false))[0];

    private async Task<List<EnrollmentResponse>> ToResponsesAsync(
        IReadOnlyCollection<ENROLLMENT> enrollments,
        CancellationToken cancellationToken)
    {
        var courses = await courseSummaryReader.GetCourseSummariesAsync(
            enrollments.Select(e => e.COURSE_ID), cancellationToken).ConfigureAwait(false);
        return enrollments.Select(enrollment =>
        {
            courses.TryGetValue(enrollment.COURSE_ID, out var course);
            return ToResponse(enrollment, course);
        }).ToList();
    }

    private static EnrollmentResponse ToResponse(ENROLLMENT e, CourseSummaryInfo? course) =>
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
            e.LAST_ACCESSED_AT_UTC,
            course?.Slug,
            course?.Title,
            course?.ThumbnailUrl,
            course?.InstructorName);
}
