using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.RejectInstructorApplication;

/// <summary>Rejects a pending instructor application. No role to revoke — a
/// <see cref="InstructorApplicationStatus.Pending"/> application never held the Instructor role in the
/// first place (see <see cref="InstructorProfile.Reject"/>'s own doc comment).</summary>
public sealed class RejectInstructorApplicationHandler(AppDbContext dbContext)
{
    public async Task<Result<RejectInstructorApplicationResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var profile = await dbContext.InstructorProfiles()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return Result.Failure<RejectInstructorApplicationResponse>(DomainError.NotFound("ไม่พบใบสมัครนี้"));
        }

        if (profile.Status != InstructorApplicationStatus.Pending)
        {
            return Result.Failure<RejectInstructorApplicationResponse>(
                DomainError.Conflict("ใบสมัครนี้ถูกดำเนินการไปแล้ว (ไม่ได้อยู่ในสถานะรออนุมัติ)"));
        }

        profile.Reject();

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new RejectInstructorApplicationResponse(profile.Id, profile.UserId, profile.DisplayName, profile.Status);
    }
}
