using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Data access for <see cref="CERTIFICATE"/>, consumed by <see cref="CertificateService"/>. Interface
/// name/members are NOT uppercased — see <see cref="IEnrollmentRepository"/>'s own doc comment for the
/// naming-exception reasoning.
/// </summary>
public interface ICertificateRepository
{
    /// <summary>Tracked lookup by primary key — <c>null</c> if no such row exists.</summary>
    Task<CERTIFICATE?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked lookup by the unique <see cref="CERTIFICATE.ENROLLMENT_ID"/> — supports idempotent
    /// issuance (has a certificate already been issued for this enrollment?).</summary>
    Task<CERTIFICATE?> GetByEnrollmentIdAsync(Guid enrollmentId, CancellationToken cancellationToken);

    /// <summary>Untracked (<c>AsNoTracking</c>) lookup by the public <see cref="CERTIFICATE.VERIFY_CODE"/>
    /// — backs the <c>AllowAnonymous</c> public verify endpoint
    /// (<c>CertificateEndpoints.MapCertificateEndpoints</c>'s <c>/verify/{verifyCode}</c> route). Untracked
    /// because a public, unauthenticated read has no business holding change-tracking entries open.
    /// </summary>
    Task<CERTIFICATE?> GetByVerifyCodeAsync(string verifyCode, CancellationToken cancellationToken);

    /// <summary>Untracked (<c>AsNoTracking</c>) query source for read scenarios — the caller composes its
    /// own filtering/paging/projection.</summary>
    IQueryable<CERTIFICATE> Query();

    /// <summary>Stages a new row for insertion — does not persist until <see cref="SaveChangesAsync"/>.
    /// </summary>
    void Add(CERTIFICATE certificate);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
