using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.Modules.Catalog.Features.AttachEpisodeMedia;

public sealed class AttachEpisodeMediaHandler(AppDbContext dbContext, IMediaAssetContract mediaAssetContract)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");
    private static readonly DomainError NotDraftError = DomainError.Conflict("แก้ไขได้เฉพาะคอร์สที่ยังเป็นฉบับร่าง (Draft) หรือถูกปฏิเสธ (Rejected) เท่านั้น");

    public async Task<Result<EpisodeMediaResponse>> AttachAsync(
        Guid userId,
        Guid courseId,
        Guid sectionId,
        Guid episodeId,
        AttachEpisodeMediaCommand command,
        CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<EpisodeMediaResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<EpisodeMediaResponse>(NotOwnerError);
        }

        if (course.Status is not (CourseStatus.Draft or CourseStatus.Rejected))
        {
            return Result.Failure<EpisodeMediaResponse>(NotDraftError);
        }

        var section = course.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null)
        {
            return Result.Failure<EpisodeMediaResponse>(DomainError.NotFound("ไม่พบส่วนนี้"));
        }

        var episode = section.Episodes.FirstOrDefault(e => e.Id == episodeId);
        if (episode is null)
        {
            return Result.Failure<EpisodeMediaResponse>(DomainError.NotFound("ไม่พบบทเรียนนี้"));
        }

        // Verify media asset ownership & existence via cross-module contract
        var assetSummary = await mediaAssetContract.GetAssetSummaryAsync(command.MediaAssetId, cancellationToken)
            .ConfigureAwait(false);

        if (assetSummary is null)
        {
            return Result.Failure<EpisodeMediaResponse>(DomainError.NotFound("ไม่พบไฟล์วิดีโอที่ระบุ"));
        }

        if (assetSummary.UploadedByUserId != userId)
        {
            return Result.Failure<EpisodeMediaResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์ใช้งานไฟล์วิดีโอนี้"));
        }

        var duration = command.DurationSeconds is > 0
            ? command.DurationSeconds.Value
            : (assetSummary.DurationSeconds.GetValueOrDefault() > 0 ? assetSummary.DurationSeconds.GetValueOrDefault() : 1);

        course.AttachEpisodeMedia(episodeId, command.MediaAssetId, duration);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new EpisodeMediaResponse(
            episode.Id,
            section.Id,
            course.Id,
            episode.MediaAssetId,
            episode.DurationSeconds,
            episode.Status));
    }

    public async Task<Result<EpisodeMediaResponse>> RemoveAsync(
        Guid userId,
        Guid courseId,
        Guid sectionId,
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<EpisodeMediaResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<EpisodeMediaResponse>(NotOwnerError);
        }

        if (course.Status is not (CourseStatus.Draft or CourseStatus.Rejected))
        {
            return Result.Failure<EpisodeMediaResponse>(NotDraftError);
        }

        var section = course.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null)
        {
            return Result.Failure<EpisodeMediaResponse>(DomainError.NotFound("ไม่พบส่วนนี้"));
        }

        var episode = section.Episodes.FirstOrDefault(e => e.Id == episodeId);
        if (episode is null)
        {
            return Result.Failure<EpisodeMediaResponse>(DomainError.NotFound("ไม่พบบทเรียนนี้"));
        }

        course.RemoveEpisodeMedia(episodeId);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new EpisodeMediaResponse(
            episode.Id,
            section.Id,
            course.Id,
            episode.MediaAssetId,
            episode.DurationSeconds,
            episode.Status));
    }
}
