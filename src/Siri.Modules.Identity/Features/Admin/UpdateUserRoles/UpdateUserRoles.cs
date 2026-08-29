using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.Admin.UpdateUserRoles;

public sealed record UpdateUserRolesCommand(IReadOnlyList<string> Roles);

public sealed class UpdateUserRolesHandler(AppDbContext dbContext, IClock clock)
{
    public async Task<Result> HandleAsync(
        Guid adminUserId,
        Guid targetUserId,
        UpdateUserRolesCommand command,
        CancellationToken cancellationToken)
    {
        if (command.Roles is null || command.Roles.Count == 0)
        {
            return Result.Failure(DomainError.Validation("ต้องระบุ ROLE อย่างน้อย 1 รายการ"));
        }

        var user = await dbContext.Users()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken)
            .ConfigureAwait(false);

        if (user is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบผู้ใช้ที่ระบุ"));
        }

        var availableRoles = await dbContext.Roles()
            .Where(r => command.Roles.Contains(r.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (availableRoles.Count != command.Roles.Count)
        {
            return Result.Failure(DomainError.Validation("มี ROLE บางรายการที่ไม่ถูกต้อง"));
        }

        var currentRoleNames = user.Roles.Select(r => r.Name).ToHashSet();
        var targetRoleNames = command.Roles.ToHashSet();

        // Roles to remove
        var toRemove = user.Roles.Where(r => !targetRoleNames.Contains(r.Name)).ToList();
        foreach (var r in toRemove)
        {
            user.RemoveRole(r);
        }

        // Roles to add
        var toAdd = availableRoles.Where(r => !currentRoleNames.Contains(r.Name)).ToList();
        foreach (var r in toAdd)
        {
            user.AssignRole(r);
        }

        var audit = SECURITY_AUDIT.Record(
            "AdminUpdateUserRoles",
            user.Id,
            $"Roles updated by {adminUserId} to: {string.Join(", ", command.Roles)}",
            null,
            clock);
        dbContext.SecurityAudits().Add(audit);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
