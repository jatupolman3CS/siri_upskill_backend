using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Business logic for <see cref="CERTIFICATE"/>, backing <c>CertificateEndpoints</c>.
/// </summary>
public sealed class CertificateService(
    ICertificateRepository certificateRepository,
    IEnrollmentRepository enrollmentRepository,
    ICatalogPriceContract catalogPriceContract,
    IUserContactReader userContactReader,
    IClock clock)
{
    public async Task<Result<CertificateResponse>> CreateAsync(IssueCertificateCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var enrollment = await enrollmentRepository.GetByIdAsync(command.EnrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null)
        {
            return Result.Failure<CertificateResponse>(DomainError.NotFound("ไม่พบข้อมูลการลงทะเบียนเรียน"));
        }

        var existing = await certificateRepository.GetByEnrollmentIdAsync(command.EnrollmentId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result.Failure<CertificateResponse>(DomainError.Conflict("มีการออกใบประกาศนียบัตรสำหรับการลงทะเบียนนี้แล้ว"));
        }

        var serialNo = $"CERT-{clock.UtcNow:yyyy}-{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        var verifyCode = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

        var cert = CERTIFICATE.Create(command.EnrollmentId, serialNo, verifyCode, command.PdfStorageKey, clock);
        certificateRepository.Add(cert);
        await certificateRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(cert));
    }

    public async Task<PagedResult<CertificateResponse>> ListAsync(Guid? enrollmentId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = certificateRepository.Query();
        if (enrollmentId.HasValue)
        {
            query = query.Where(c => c.ENROLLMENT_ID == enrollmentId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var entities = await query
            .OrderByDescending(c => c.ISSUED_AT_UTC)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = entities.Select(ToResponse).ToList();
        return PagedResult<CertificateResponse>.Create(items, totalCount, page, pageSize);
    }

    public async Task<PagedResult<CertificateResponse>> ListForUserAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var userEnrollmentIds = await enrollmentRepository.Query()
            .Where(e => e.USER_ID == userId)
            .Select(e => e.ENROLLMENT_ID)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var query = certificateRepository.Query().Where(c => userEnrollmentIds.Contains(c.ENROLLMENT_ID));
        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var entities = await query
            .OrderByDescending(c => c.ISSUED_AT_UTC)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var items = entities.Select(ToResponse).ToList();
        return PagedResult<CertificateResponse>.Create(items, totalCount, page, pageSize);
    }

    public async Task<Result<CertificateDetailResponse>> GetOwnAsync(Guid userId, Guid certificateId, CancellationToken cancellationToken)
    {
        var cert = await certificateRepository.GetByIdAsync(certificateId, cancellationToken).ConfigureAwait(false);
        if (cert is null)
        {
            return Result.Failure<CertificateDetailResponse>(DomainError.NotFound("ไม่พบใบประกาศนียบัตร"));
        }

        var enrollment = await enrollmentRepository.GetByIdAsync(cert.ENROLLMENT_ID, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != userId)
        {
            return Result.Failure<CertificateDetailResponse>(DomainError.NotFound("ไม่พบใบประกาศนียบัตร"));
        }

        var contact = await userContactReader.GetUserContactInfoAsync(enrollment.USER_ID, cancellationToken).ConfigureAwait(false);
        var titles = await catalogPriceContract.GetCourseTitlesAsync([enrollment.COURSE_ID], cancellationToken).ConfigureAwait(false);

        var learnerName = contact.DisplayName ?? contact.Email ?? "ผู้เรียน Siri UpSkill";
        titles.TryGetValue(enrollment.COURSE_ID, out var courseTitle);
        courseTitle ??= "คอร์สเรียนออนไลน์";

        var verifyUrl = $"/certificates/verify/{cert.VERIFY_CODE}";

        return Result.Success(new CertificateDetailResponse(
            cert.CERTIFICATE_ID,
            cert.ENROLLMENT_ID,
            cert.SERIAL_NO,
            cert.VERIFY_CODE,
            cert.ISSUED_AT_UTC,
            cert.PDF_STORAGE_KEY,
            cert.REVOKED_AT_UTC,
            learnerName,
            courseTitle,
            verifyUrl));
    }

    public async Task<Result<(byte[] Bytes, string FileName)>> GeneratePdfAsync(
        Guid certificateId,
        Guid? requestedUserId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var cert = await certificateRepository.GetByIdAsync(certificateId, cancellationToken).ConfigureAwait(false);
        if (cert is null)
        {
            return Result.Failure<(byte[], string)>(DomainError.NotFound("ไม่พบใบประกาศนียบัตร"));
        }

        var enrollment = await enrollmentRepository.GetByIdAsync(cert.ENROLLMENT_ID, cancellationToken).ConfigureAwait(false);
        if (enrollment is null)
        {
            return Result.Failure<(byte[], string)>(DomainError.NotFound("ไม่พบข้อมูลการลงทะเบียนเรียน"));
        }

        if (!isAdmin && (requestedUserId is null || enrollment.USER_ID != requestedUserId.Value))
        {
            return Result.Failure<(byte[], string)>(DomainError.Forbidden("คุณไม่มีสิทธิ์เข้าถึงใบประกาศนียบัตรนี้"));
        }

        var contact = await userContactReader.GetUserContactInfoAsync(enrollment.USER_ID, cancellationToken).ConfigureAwait(false);
        var titles = await catalogPriceContract.GetCourseTitlesAsync([enrollment.COURSE_ID], cancellationToken).ConfigureAwait(false);

        var learnerName = contact.DisplayName ?? contact.Email ?? "ผู้เรียน Siri UpSkill";
        titles.TryGetValue(enrollment.COURSE_ID, out var courseTitle);
        courseTitle ??= "คอร์สเรียนออนไลน์";

        var verifyUrl = $"https://siriupskill.com/certificates/verify/{cert.VERIFY_CODE}";

        var pdfData = new CertificatePdfData(
            learnerName,
            courseTitle,
            cert.SERIAL_NO,
            cert.VERIFY_CODE,
            cert.ISSUED_AT_UTC,
            verifyUrl);

        var bytes = CertificatePdfGenerator.GeneratePdf(pdfData);
        var fileName = $"Certificate-{cert.SERIAL_NO}.pdf";

        return Result.Success((bytes, fileName));
    }

    public async Task<Result<CertificateResponse>> RevokeAsync(Guid certificateId, CancellationToken cancellationToken)
    {
        var cert = await certificateRepository.GetByIdAsync(certificateId, cancellationToken).ConfigureAwait(false);
        if (cert is null)
        {
            return Result.Failure<CertificateResponse>(DomainError.NotFound("ไม่พบใบประกาศนียบัตร"));
        }

        cert.Revoke(clock);
        await certificateRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ToResponse(cert));
    }

    public async Task<Result<CertificateVerificationResponse>> VerifyByCodeAsync(string verifyCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(verifyCode))
        {
            return Result.Failure<CertificateVerificationResponse>(DomainError.NotFound("ไม่พบรหัสตรวจสอบใบประกาศนียบัตร"));
        }

        var cert = await certificateRepository.GetByVerifyCodeAsync(verifyCode.Trim(), cancellationToken).ConfigureAwait(false);
        if (cert is null)
        {
            return Result.Failure<CertificateVerificationResponse>(DomainError.NotFound("ไม่พบข้อมูลใบประกาศนียบัตรสำหรับรหัสที่ระบุ"));
        }

        var enrollment = await enrollmentRepository.GetByIdAsync(cert.ENROLLMENT_ID, cancellationToken).ConfigureAwait(false);

        string? learnerName = null;
        string? courseTitle = null;

        if (enrollment is not null)
        {
            var contact = await userContactReader.GetUserContactInfoAsync(enrollment.USER_ID, cancellationToken).ConfigureAwait(false);
            var titles = await catalogPriceContract.GetCourseTitlesAsync([enrollment.COURSE_ID], cancellationToken).ConfigureAwait(false);

            learnerName = contact.DisplayName ?? contact.Email;
            titles.TryGetValue(enrollment.COURSE_ID, out courseTitle);
        }

        var isValid = cert.REVOKED_AT_UTC is null;
        return Result.Success(new CertificateVerificationResponse(
            cert.SERIAL_NO,
            cert.VERIFY_CODE,
            cert.ISSUED_AT_UTC,
            isValid,
            cert.REVOKED_AT_UTC,
            learnerName,
            courseTitle));
    }

    private static CertificateResponse ToResponse(CERTIFICATE c) =>
        new(
            c.CERTIFICATE_ID,
            c.ENROLLMENT_ID,
            c.SERIAL_NO,
            c.VERIFY_CODE,
            c.ISSUED_AT_UTC,
            c.PDF_STORAGE_KEY,
            c.REVOKED_AT_UTC);
}
