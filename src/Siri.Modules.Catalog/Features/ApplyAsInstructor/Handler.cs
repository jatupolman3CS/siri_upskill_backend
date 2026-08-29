using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.ApplyAsInstructor;

/// <summary>
/// Submits — or, after a prior rejection, re-submits (see <see cref="INSTRUCTOR_PROFILE.Resubmit"/>'s own
/// doc comment) — an application to become an instructor. <c>InstructorProfiles.UserId</c>'s unique index
/// enforces "one profile per user, ever"; this handler checks first for a friendly 409 instead of always
/// relying on the DB round-trip to fail, but also catches the still-possible race (two concurrent applies
/// from the same account) the same way <c>CreateCategoryHandler</c> does for <c>CATEGORY.Slug</c>.
/// </summary>
public sealed class ApplyAsInstructorHandler(AppDbContext dbContext)
{
    private static readonly DomainError AlreadyPendingError =
        DomainError.Conflict("มีใบสมัครเป็นผู้สอนที่รออนุมัติอยู่แล้ว");

    private static readonly DomainError AlreadyApprovedError =
        DomainError.Conflict("บัญชีนี้เป็นผู้สอนอยู่แล้ว");

    public async Task<Result<ApplyAsInstructorResponse>> HandleAsync(
        Guid userId, ApplyAsInstructorCommand command, CancellationToken cancellationToken)
    {
        var existing = await dbContext.InstructorProfiles()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        INSTRUCTOR_PROFILE profile;

        if (existing is null)
        {
            profile = INSTRUCTOR_PROFILE.Apply(userId, command.DisplayName, command.Headline, command.Bio);
            dbContext.InstructorProfiles().Add(profile);
        }
        else if (existing.Status == InstructorApplicationStatus.Rejected)
        {
            existing.Resubmit(command.DisplayName, command.Headline, command.Bio);
            profile = existing;
        }
        else
        {
            var error = existing.Status == InstructorApplicationStatus.Pending ? AlreadyPendingError : AlreadyApprovedError;
            return Result.Failure<ApplyAsInstructorResponse>(error);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            if (existing is not null)
            {
                throw; // Resubmit path (update, not insert) — not the create-race this guard exists for; let the global handler log and 500 it
            }

            var nowExists = await dbContext.InstructorProfiles()
                .AsNoTracking()
                .AnyAsync(p => p.UserId == userId, cancellationToken)
                .ConfigureAwait(false);

            if (!nowExists)
            {
                throw; // not the expected race — let the global exception handler log and 500 it
            }

            return Result.Failure<ApplyAsInstructorResponse>(AlreadyPendingError);
        }

        return ToResponse(profile);
    }

    private static ApplyAsInstructorResponse ToResponse(INSTRUCTOR_PROFILE profile) =>
        new(profile.Id, profile.UserId, profile.DisplayName, profile.Headline, profile.Bio,
            profile.RevenueSharePercent, profile.Status, profile.ApprovedAtUtc);
}
