using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.ApproveInstructorApplication;

/// <summary>
/// Approves a pending instructor application. Grants the Instructor role through Identity's
/// <see cref="IInstructorRoleGrantor"/> contract (see that interface's own doc comment for why this
/// can't be Catalog's own concern — Roles/UserRoles are Identity's data) and flips
/// <see cref="INSTRUCTOR_PROFILE"/>'s own status, both staged on the same <see cref="AppDbContext"/> and
/// committed in one <c>SaveChangesAsync</c> — atomic, the same "multiple entities, one commit" shape
/// P0-17's SE-03 eviction already established for this codebase.
/// </summary>
public sealed class ApproveInstructorApplicationHandler(AppDbContext dbContext, IClock clock, IInstructorRoleGrantor roleGrantor)
{
    public async Task<Result<ApproveInstructorApplicationResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var profile = await dbContext.InstructorProfiles()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return Result.Failure<ApproveInstructorApplicationResponse>(DomainError.NotFound("ไม่พบใบสมัครนี้"));
        }

        if (profile.Status != InstructorApplicationStatus.Pending)
        {
            return Result.Failure<ApproveInstructorApplicationResponse>(
                DomainError.Conflict("ใบสมัครนี้ถูกดำเนินการไปแล้ว (ไม่ได้อยู่ในสถานะรออนุมัติ)"));
        }

        profile.Approve(clock);
        await roleGrantor.GrantAsync(profile.UserId, cancellationToken).ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ApproveInstructorApplicationResponse(profile.Id, profile.UserId, profile.DisplayName, profile.Status, profile.ApprovedAtUtc);
    }
}
