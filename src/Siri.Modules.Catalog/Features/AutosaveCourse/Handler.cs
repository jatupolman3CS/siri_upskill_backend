using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.Modules.Catalog.Features.AutosaveCourse;

public sealed class AutosaveCourseHandler(
    AppDbContext dbContext,
    IClock clock,
    IMediaAssetContract mediaAssetContract,
    TeachingMaterialStorage materialStorage)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");
    private static readonly DomainError NotDraftError = DomainError.Conflict("แก้ไขได้เฉพาะคอร์สที่ยังเป็นฉบับร่าง (Draft) หรือถูกปฏิเสธ (Rejected) เท่านั้น");

    public async Task<Result<AutosaveCourseResponse>> HandleAsync(
        Guid userId, Guid courseId, AutosaveCourseCommand command, CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .Include(c => c.Outcomes)
            .Include(c => c.Requirements)
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<AutosaveCourseResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<AutosaveCourseResponse>(NotOwnerError);
        }

        if (course.Status is not (CourseStatus.Draft or CourseStatus.Rejected))
        {
            return Result.Failure<AutosaveCourseResponse>(NotDraftError);
        }

        if (course.CategoryId != command.CategoryId)
        {
            var categoryExists = await dbContext.Categories()
                .AsNoTracking()
                .AnyAsync(c => c.Id == command.CategoryId, cancellationToken)
                .ConfigureAwait(false);

            if (!categoryExists)
            {
                return Result.Failure<AutosaveCourseResponse>(DomainError.NotFound("ไม่พบหมวดหมู่ที่ระบุ"));
            }
        }

        // Validate the complete draft before changing any tracked entities, including a trailer
        // and media on newly created episodes. Drafts may retain uploads still being processed.
        var requestedMediaIds = (command.Sections ?? [])
            .SelectMany(section => section.Episodes ?? [])
            .Select(episode => episode.MediaAssetId)
            .Append(command.TrailerMediaAssetId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .Distinct();
        var mediaAssets = new Dictionary<Guid, MediaAssetSummary>();
        foreach (var mediaId in requestedMediaIds)
        {
            var asset = await mediaAssetContract.GetAssetSummaryAsync(mediaId, cancellationToken).ConfigureAwait(false);
            if (asset is null)
            {
                return Result.Failure<AutosaveCourseResponse>(DomainError.NotFound("The selected video was not found."));
            }

            if (asset.UploadedByUserId != userId)
            {
                return Result.Failure<AutosaveCourseResponse>(DomainError.Forbidden("You do not own the selected video."));
            }

            mediaAssets.Add(mediaId, asset);
        }

        // Set EF Core concurrency token to detect mid-air collisions
        dbContext.Entry(course).Property(c => c.RowVersion).OriginalValue = command.RowVersion;
        // Child-only edits must also rotate/check the draft's concurrency token.
        dbContext.Entry(course).Property(c => c.RowVersion).IsModified = true;

        course.UpdateBasicInfo(command.Title, command.Subtitle, command.Description);
        course.SetCategory(command.CategoryId);
        course.SetLevel(command.Level);
        course.SetLanguage(command.Language);
        course.SetThumbnail(command.ThumbnailUrl);
        course.SetPricing(command.Price, command.ComparePrice);
        course.SetAccessDuration(command.AccessDurationDays);
        course.SetSeo(command.SeoTitle, command.SeoDescription);

        course.SetOutcomes(command.Outcomes ?? []);
        course.SetRequirements(command.Requirements ?? []);
        course.SetTrailer(command.TrailerMediaAssetId);

        // Domain-created children already have UUIDs, so EF must be told to insert them.
        foreach (var outcome in course.Outcomes) dbContext.Entry(outcome).State = EntityState.Added;
        foreach (var requirement in course.Requirements) dbContext.Entry(requirement).State = EntityState.Added;
        List<AutosaveSectionIds>? savedSections = command.Sections is null ? null : [];

        // Episodes this save deletes. Their attachment rows are removed by FK cascade, but the files in R2 are not,
        // so their storage keys are collected before the save and the objects deleted after it.
        var removedEpisodeIds = new List<Guid>();

        if (command.Sections != null)
        {
            // 1. Remove deleted sections
            var submittedSectionIds = command.Sections
                .Where(s => s.Id.HasValue && s.Id.Value != Guid.Empty)
                .Select(s => s.Id!.Value)
                .ToHashSet();

            var sectionsToRemove = course.Sections
                .Where(s => !submittedSectionIds.Contains(s.Id))
                .ToList();

            foreach (var sectionToRemove in sectionsToRemove)
            {
                removedEpisodeIds.AddRange(sectionToRemove.Episodes.Select(e => e.Id));
                course.RemoveSection(sectionToRemove.Id);
            }

            // 2. Add or update sections & episodes
            foreach (var sectionItem in command.Sections.OrderBy(s => s.SortOrder))
            {
                COURSE_SECTION section;
                if (sectionItem.Id.HasValue && sectionItem.Id.Value != Guid.Empty &&
                    course.Sections.FirstOrDefault(s => s.Id == sectionItem.Id.Value) is { } existingSection)
                {
                    section = existingSection;
                    section.Rename(sectionItem.Title);
                }
                else
                {
                    section = course.AddSection(sectionItem.Title);
                    dbContext.Entry(section).State = EntityState.Added;
                }

                var savedEpisodes = new List<AutosaveEpisodeIds>();
                if (sectionItem.Episodes != null)
                {
                    var submittedEpisodeIds = sectionItem.Episodes
                        .Where(e => e.Id.HasValue && e.Id.Value != Guid.Empty)
                        .Select(e => e.Id!.Value)
                        .ToHashSet();

                    var episodesToRemove = section.Episodes
                        .Where(e => !submittedEpisodeIds.Contains(e.Id))
                        .ToList();

                    foreach (var epToRemove in episodesToRemove)
                    {
                        removedEpisodeIds.Add(epToRemove.Id);
                        course.RemoveEpisode(epToRemove.Id);
                    }

                    foreach (var epItem in sectionItem.Episodes.OrderBy(e => e.SortOrder))
                    {
                        COURSE_EPISODE targetEp;
                        if (epItem.Id.HasValue && epItem.Id.Value != Guid.Empty &&
                            section.Episodes.FirstOrDefault(e => e.Id == epItem.Id.Value) is { } existingEp)
                        {
                            targetEp = existingEp;
                            existingEp.UpdateDetails(epItem.Title, epItem.Description);
                            if (epItem.IsFreePreview)
                            {
                                existingEp.MarkFreePreview();
                            }
                            else
                            {
                                existingEp.UnmarkFreePreview();
                            }
                        }
                        else
                        {
                            targetEp = course.AddEpisode(section.Id, epItem.Title, epItem.Description, epItem.IsFreePreview);
                            dbContext.Entry(targetEp).State = EntityState.Added;
                        }

                        savedEpisodes.Add(new AutosaveEpisodeIds(targetEp.Id));

                        if (epItem.MediaAssetId is { } mediaId)
                        {
                            // Until processing completes, use the domain's minimum duration.
                            // Client estimates must not replace the provider's measured duration.
                            var duration = mediaAssets[mediaId].DurationSeconds is > 0
                                ? mediaAssets[mediaId].DurationSeconds!.Value
                                : 1;
                            course.AttachEpisodeMedia(targetEp.Id, mediaId, duration);
                        }
                        else if (targetEp.MediaAssetId is not null)
                        {
                            course.RemoveEpisodeMedia(targetEp.Id);
                        }
                    }

                    section.ReorderEpisodes(savedEpisodes.Select(episode => episode.Id).ToArray());
                }

                savedSections!.Add(new AutosaveSectionIds(section.Id, savedEpisodes));
            }

            course.ReorderSections(savedSections!.Select(section => section.Id).ToArray());
        }

        var orphanedStorageKeys = removedEpisodeIds.Count == 0
            ? []
            : await dbContext.EpisodeAttachments()
                .AsNoTracking()
                .Where(a => removedEpisodeIds.Contains(a.EpisodeId))
                .Select(a => a.StorageKey)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        try
        {
            await CourseGraphPersistence.SaveAsync(dbContext, course.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<AutosaveCourseResponse>(
                DomainError.Conflict("ข้อมูลคอร์สนี้ถูกแก้ไขจากที่อื่นแล้ว กรุณารีเฟรชหน้าเว็บก่อนทำการบันทึกอีกครั้ง"));
        }

        await materialStorage.DeleteQuietlyAsync(orphanedStorageKeys, cancellationToken).ConfigureAwait(false);

        return Result.Success(new AutosaveCourseResponse(course.Id, course.RowVersion, course.UpdatedAtUtc ?? clock.UtcNow, savedSections));
    }
}
