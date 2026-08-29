using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Features;

public static class EpisodeAccessHelper
{
    public static async Task<bool> CanUserAccessEpisodeAsync(
        AppDbContext dbContext,
        IEpisodeAccessReader episodeAccessReader,
        COURSE_EPISODE episode,
        Guid? userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(episodeAccessReader);
        ArgumentNullException.ThrowIfNull(episode);

        if (isAdmin)
        {
            return true;
        }

        if (episode.IsFreePreview)
        {
            return true;
        }

        if (userId is { } uid && uid != Guid.Empty)
        {
            var instructorProfile = await dbContext.InstructorProfiles()
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == uid, cancellationToken)
                .ConfigureAwait(false);

            var course = await dbContext.Courses()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == episode.CourseId, cancellationToken)
                .ConfigureAwait(false);

            if (instructorProfile is not null && course is not null && course.InstructorId == instructorProfile.Id)
            {
                return true;
            }

            return await episodeAccessReader.CanUserAccessEpisodeAsync(uid, episode.Id, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }
}
