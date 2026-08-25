using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.Admin.SuspendUser;

public sealed record SuspendUserCommand(string Reason);

public sealed class SuspendUserHandler(AppDbContext dbContext, IClock clock)
{
    public async Task<Result> HandleAsync(
        Guid adminUserId,
        Guid targetUserId,
        SuspendUserCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Reason))
        {
            return Result.Failure(DomainError.Validation("ต้องระบุเหตุผลในการระงับบัญชี"));
        }

        var user = await dbContext.Users()
            .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบผู้ใช้ที่ระบุ"));
        }

        if (user.Id == adminUserId)
        {
            return Result.Failure(DomainError.Conflict("ไม่สามารถระงับบัญชีของตัวเองได้"));
        }

        user.Suspend(command.Reason);

        var audit = SecurityAudit.Record("AdminSuspendUser", user.Id, $"Suspended by {adminUserId}: {command.Reason}", null, clock);
        dbContext.SecurityAudits().Add(audit);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
