using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.Admin.ReactivateUser;

public sealed class ReactivateUserHandler(AppDbContext dbContext, IClock clock)
{
    public async Task<Result> HandleAsync(
        Guid adminUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        var user = await dbContext.Users()
            .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบผู้ใช้ที่ระบุ"));
        }

        if (user.Status != UserStatus.Suspended)
        {
            return Result.Failure(DomainError.Conflict("ผู้ใช้ไม่ได้อยู่ในสถานะระงับการใช้งาน"));
        }

        user.Reactivate();

        var audit = SecurityAudit.Record("AdminReactivateUser", user.Id, $"Reactivated by {adminUserId}", null, clock);
        dbContext.SecurityAudits().Add(audit);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
