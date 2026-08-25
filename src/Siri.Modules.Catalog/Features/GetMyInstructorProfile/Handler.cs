using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetMyInstructorProfile;

/// <summary>The caller's own instructor application/profile — <c>IUserContext.UserId</c>-scoped only, so
/// this can never return anyone else's profile (there is no id parameter to guess or tamper with in the
/// first place, not just a check against one).</summary>
public sealed class GetMyInstructorProfileHandler(AppDbContext dbContext)
{
    public async Task<Result<InstructorProfileResponse>> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (profile is null)
        {
            return Result.Failure<InstructorProfileResponse>(DomainError.NotFound("ยังไม่ได้สมัครเป็นผู้สอน"));
        }

        return new InstructorProfileResponse(
            profile.Id, profile.UserId, profile.DisplayName, profile.Headline, profile.Bio,
            profile.RevenueSharePercent, profile.Status, profile.ApprovedAtUtc);
    }
}
