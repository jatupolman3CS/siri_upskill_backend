using Siri.Modules.Identity.Contracts;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Infrastructure.Contracts;

public sealed class SecurityAuditContract(AppDbContext dbContext, IClock clock) : ISecurityAuditContract
{
    public async Task RecordAuditAsync(
        string eventType,
        Guid? userId,
        string? detail,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var audit = SECURITY_AUDIT.Record(eventType, userId, detail, ipAddress, clock);
        dbContext.SecurityAudits().Add(audit);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
