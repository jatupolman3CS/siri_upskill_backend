using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Infrastructure.Bootstrap;

/// <summary>An account that must hold an Approved instructor profile. Plain data (not an Identity
/// type) because Catalog may not reference another module's Domain/Infrastructure — the composition
/// root (<c>Siri.Api</c>) maps Identity's owner accounts into this, the same hand-off
/// <c>CatalogSeeder</c> uses for its instructor user ids.</summary>
public sealed record OwnerInstructorSpec(Guid UserId, string DisplayName);

/// <summary>
/// Makes sure each platform-owner account has an <see cref="InstructorApplicationStatus.Approved"/>
/// <see cref="INSTRUCTOR_PROFILE"/>. <c>CreateCourseHandler</c> requires one even for Admin/SuperAdmin
/// callers (an admin cannot originate a course "as" an instructor without one), so holding the roles
/// alone is not enough to use the instructor studio. Companion to Identity's
/// <c>OwnerAccountBootstrapper</c>, which decides <em>who</em> qualifies; this class never decides that
/// itself. Idempotent — an already-Approved profile is left untouched and nothing is saved.
/// </summary>
public sealed class OwnerInstructorProfileBootstrapper(
    AppDbContext dbContext,
    IClock clock,
    ILogger<OwnerInstructorProfileBootstrapper> logger)
{
    private const string OwnerBio = "Platform owner account.";

    public async Task EnsureApprovedAsync(IReadOnlyCollection<OwnerInstructorSpec> owners, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owners);

        if (owners.Count == 0)
        {
            return;
        }

        var userIds = owners.Select(owner => owner.UserId).ToArray();
        var existingByUserId = await dbContext.InstructorProfiles()
            .Where(profile => userIds.Contains(profile.UserId))
            .ToDictionaryAsync(profile => profile.UserId, cancellationToken)
            .ConfigureAwait(false);

        var changedCount = 0;

        foreach (var owner in owners)
        {
            if (!existingByUserId.TryGetValue(owner.UserId, out var profile))
            {
                profile = INSTRUCTOR_PROFILE.Apply(owner.UserId, owner.DisplayName, headline: null, OwnerBio);
                dbContext.InstructorProfiles().Add(profile);
            }
            else if (profile.Status == InstructorApplicationStatus.Approved)
            {
                continue;
            }
            else if (profile.Status == InstructorApplicationStatus.Rejected)
            {
                // Keep the owner's own text; Rejected must go back through Pending before it can be approved.
                profile.Resubmit(profile.DisplayName, profile.Headline, profile.Bio);
            }

            profile.Approve(clock);
            changedCount++;
        }

        if (changedCount > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation(
            "Owner bootstrap: {ChangedCount} instructor profile(s) approved, {UnchangedCount} already approved.",
            changedCount,
            owners.Count - changedCount);
    }
}
