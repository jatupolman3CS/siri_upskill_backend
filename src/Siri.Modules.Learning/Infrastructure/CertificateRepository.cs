using Microsoft.EntityFrameworkCore;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure;

public sealed class CertificateRepository(AppDbContext dbContext) : ICertificateRepository
{
    public Task<CERTIFICATE?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Certificates().FirstOrDefaultAsync(c => c.CERTIFICATE_ID == id, cancellationToken);

    public Task<CERTIFICATE?> GetByEnrollmentIdAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
        dbContext.Certificates().FirstOrDefaultAsync(c => c.ENROLLMENT_ID == enrollmentId, cancellationToken);

    public Task<CERTIFICATE?> GetByVerifyCodeAsync(string verifyCode, CancellationToken cancellationToken) =>
        dbContext.Certificates().FirstOrDefaultAsync(c => c.VERIFY_CODE == verifyCode, cancellationToken);

    public IQueryable<CERTIFICATE> Query() => dbContext.Certificates().AsNoTracking();

    public void Add(CERTIFICATE certificate) => dbContext.Certificates().Add(certificate);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
