using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Business logic for <see cref="CERTIFICATE"/>, backing <c>CertificateEndpoints</c>.
/// <para>
/// Real data only: the learner name and course title printed on a certificate come from the account
/// (<see cref="IUserContactReader"/>) and the course (<see cref="ICatalogPriceContract"/>); if either cannot
/// be resolved the operation fails with a clear <see cref="DomainError"/> rather than printing a generic
/// stand-in, and the QR code's verify URL is built from <see cref="CertificateOptions.PublicBaseUrl"/> (never
/// a hardcoded domain).
/// </para>
/// </summary>
public sealed class CertificateService(
    ICertificateRepository certificateRepository,
    IEnrollmentRepository enrollmentRepository,
    ICatalogPriceContract catalogPriceContract,
    IUserContactReader userContactReader,
    IOptions<CertificateOptions> certificateOptions,
    IClock clock)
{
    /// <summary>Ends in the shared <c>_not_configured</c> suffix, so it maps to HTTP 503.</summary>
    public const string PublicUrlNotConfiguredCode = "certificate.public_url_not_configured";

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

        var identity = await ResolveLearnerAndCourseAsync(enrollment, cancellationToken).ConfigureAwait(false);
        if (identity.IsFailure)
        {
            return Result.Failure<CertificateDetailResponse>(identity.Error);
        }

        var verifyUrl = $"/certificates/verify/{cert.VERIFY_CODE}";

        return Result.Success(new CertificateDetailResponse(
            cert.CERTIFICATE_ID,
            cert.ENROLLMENT_ID,
            cert.SERIAL_NO,
            cert.VERIFY_CODE,
            cert.ISSUED_AT_UTC,
            cert.PDF_STORAGE_KEY,
            cert.REVOKED_AT_UTC,
            identity.Value.LearnerName,
            identity.Value.CourseTitle,
            verifyUrl));
    }

    public async Task<Result<CertificateDetailResponse>> GetByEnrollmentIdAsync(Guid userId, Guid enrollmentId, CancellationToken cancellationToken)
    {
        var enrollment = await enrollmentRepository.GetByIdAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != userId)
        {
            return Result.Failure<CertificateDetailResponse>(DomainError.NotFound("ไม่พบข้อมูลการลงทะเบียน"));
        }

        var cert = await certificateRepository.GetByEnrollmentIdAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (cert is null)
        {
            if (enrollment.PROGRESS_PERCENT >= 100m)
            {
                var issueResult = await CreateAsync(new IssueCertificateCommand(enrollmentId, null), cancellationToken).ConfigureAwait(false);
                if (issueResult.IsSuccess)
                {
                    return await GetOwnAsync(userId, issueResult.Value.Id, cancellationToken).ConfigureAwait(false);
                }
            }
            return Result.Failure<CertificateDetailResponse>(DomainError.NotFound("ยังไม่มีใบประกาศนียบัตรสำหรับการลงทะเบียนนี้"));
        }

        return await GetOwnAsync(userId, cert.CERTIFICATE_ID, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<CertificateDetailResponse>> GetByCourseIdAsync(Guid userId, Guid courseId, CancellationToken cancellationToken)
    {
        var enrollment = await enrollmentRepository.GetByUserAndCourseAsync(userId, courseId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null)
        {
            return Result.Failure<CertificateDetailResponse>(DomainError.NotFound("ไม่พบข้อมูลการลงทะเบียนเรียนคอร์สนี้"));
        }

        return await GetByEnrollmentIdAsync(userId, enrollment.ENROLLMENT_ID, cancellationToken).ConfigureAwait(false);
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

        return await RenderPdfAsync(cert, enrollment, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Result<(byte[] Bytes, string FileName)>> GeneratePdfByVerifyCodeAsync(
        string verifyCode,
        CancellationToken cancellationToken)
    {
        var cert = await certificateRepository.GetByVerifyCodeAsync(verifyCode, cancellationToken).ConfigureAwait(false);
        if (cert is null)
        {
            return Result.Failure<(byte[], string)>(DomainError.NotFound("ไม่พบใบประกาศนียบัตร"));
        }

        var enrollment = await enrollmentRepository.GetByIdAsync(cert.ENROLLMENT_ID, cancellationToken).ConfigureAwait(false);
        if (enrollment is null)
        {
            return Result.Failure<(byte[], string)>(DomainError.NotFound("ไม่พบข้อมูลการลงทะเบียนเรียน"));
        }

        return await RenderPdfAsync(cert, enrollment, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<(byte[] Bytes, string FileName)>> RenderPdfAsync(
        CERTIFICATE cert,
        ENROLLMENT enrollment,
        CancellationToken cancellationToken)
    {
        var data = await BuildPdfDataAsync(cert, enrollment, cancellationToken).ConfigureAwait(false);
        if (data.IsFailure)
        {
            return Result.Failure<(byte[] Bytes, string FileName)>(data.Error);
        }

        var bytes = CertificatePdfGenerator.GeneratePdf(data.Value);
        return Result.Success((bytes, $"Certificate-{cert.SERIAL_NO}.pdf"));
    }

    /// <summary>Everything printed on the certificate PDF, before rendering — kept separate from the
    /// renderer so the real learner name / course title / QR URL can be asserted without parsing PDF
    /// bytes.</summary>
    internal async Task<Result<CertificatePdfData>> BuildPdfDataAsync(
        CERTIFICATE cert,
        ENROLLMENT enrollment,
        CancellationToken cancellationToken)
    {
        var identity = await ResolveLearnerAndCourseAsync(enrollment, cancellationToken).ConfigureAwait(false);
        if (identity.IsFailure)
        {
            return Result.Failure<CertificatePdfData>(identity.Error);
        }

        var baseUrl = certificateOptions.Value.GetNormalizedPublicBaseUrl();
        if (baseUrl is null)
        {
            return Result.Failure<CertificatePdfData>(new DomainError(
                PublicUrlNotConfiguredCode,
                "Certificate public URL is not configured."));
        }

        return Result.Success(new CertificatePdfData(
            identity.Value.LearnerName,
            identity.Value.CourseTitle,
            cert.SERIAL_NO,
            cert.VERIFY_CODE,
            cert.ISSUED_AT_UTC,
            $"{baseUrl}/certificates/verify/{cert.VERIFY_CODE}"));
    }

    /// <summary>
    /// The learner name (real display name, falling back to the real email) and the real course title. A
    /// certificate is a legal-style document, so an unresolvable learner or course fails the operation
    /// instead of printing a generic stand-in.
    /// </summary>
    private async Task<Result<(string LearnerName, string CourseTitle)>> ResolveLearnerAndCourseAsync(
        ENROLLMENT enrollment,
        CancellationToken cancellationToken)
    {
        var contact = await userContactReader.GetUserContactInfoAsync(enrollment.USER_ID, cancellationToken).ConfigureAwait(false);
        var learnerName = !string.IsNullOrWhiteSpace(contact.DisplayName) ? contact.DisplayName.Trim() : contact.Email?.Trim();
        if (string.IsNullOrWhiteSpace(learnerName))
        {
            return Result.Failure<(string LearnerName, string CourseTitle)>(
                DomainError.NotFound("ไม่พบข้อมูลผู้เรียนสำหรับใบประกาศนียบัตรนี้"));
        }

        var titles = await catalogPriceContract.GetCourseTitlesAsync([enrollment.COURSE_ID], cancellationToken).ConfigureAwait(false);
        if (!titles.TryGetValue(enrollment.COURSE_ID, out var courseTitle) || string.IsNullOrWhiteSpace(courseTitle))
        {
            return Result.Failure<(string LearnerName, string CourseTitle)>(
                DomainError.NotFound("ไม่พบข้อมูลคอร์สสำหรับใบประกาศนียบัตรนี้"));
        }

        return Result.Success((learnerName, courseTitle.Trim()));
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

            // This lookup is PUBLIC (anyone holding a verify code): never fall back to the learner's email —
            // an unresolvable display name is reported as null, not replaced with something else.
            learnerName = string.IsNullOrWhiteSpace(contact.DisplayName) ? null : contact.DisplayName;
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
