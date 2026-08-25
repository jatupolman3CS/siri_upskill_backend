using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.AutosaveCourse;

public sealed class AutosaveCourseHandler(AppDbContext dbContext, IClock clock)
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

        // Set EF Core concurrency token to detect mid-air collisions
        dbContext.Entry(course).Property(c => c.RowVersion).OriginalValue = command.RowVersion;

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
                course.RemoveSection(sectionToRemove.Id);
            }

            // 2. Add or update sections & episodes
            foreach (var sectionItem in command.Sections.OrderBy(s => s.SortOrder))
            {
                CourseSection section;
                if (sectionItem.Id.HasValue && sectionItem.Id.Value != Guid.Empty &&
                    course.Sections.FirstOrDefault(s => s.Id == sectionItem.Id.Value) is { } existingSection)
                {
                    section = existingSection;
                    section.Rename(sectionItem.Title);
                }
                else
                {
                    section = course.AddSection(sectionItem.Title);
                }

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
                        section.RemoveEpisode(epToRemove.Id);
                    }

                    foreach (var epItem in sectionItem.Episodes.OrderBy(e => e.SortOrder))
                    {
                        if (epItem.Id.HasValue && epItem.Id.Value != Guid.Empty &&
                            section.Episodes.FirstOrDefault(e => e.Id == epItem.Id.Value) is { } existingEp)
                        {
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
                            section.AddEpisode(epItem.Title, epItem.Description, epItem.IsFreePreview);
                        }
                    }
                }
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure<AutosaveCourseResponse>(
                DomainError.Conflict("ข้อมูลคอร์สนี้ถูกแก้ไขจากที่อื่นแล้ว กรุณารีเฟรชหน้าเว็บก่อนทำการบันทึกอีกครั้ง"));
        }

        return Result.Success(new AutosaveCourseResponse(course.Id, course.RowVersion, course.UpdatedAtUtc ?? clock.UtcNow));
    }
}
