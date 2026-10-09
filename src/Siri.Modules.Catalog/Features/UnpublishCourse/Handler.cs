using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Notification.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UnpublishCourse;

public sealed class UnpublishCourseHandler(
    AppDbContext dbContext,
    ISecurityAuditContract securityAudit,
    IUserContactReader userContactReader,
    IEmailOutbox emailOutbox,
    IOutputCacheStore outputCacheStore,
    CourseSearchIndexer searchIndexer)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotPublishedError = DomainError.Conflict("ระงับหรือยกเลิกการเผยแพร่ได้เฉพาะคอร์สที่เผยแพร่อยู่เท่านั้น");

    public async Task<Result<UnpublishCourseResponse>> HandleAsync(
        Guid courseId,
        Guid? adminUserId,
        string? ipAddress,
        UnpublishCourseCommand command,
        CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<UnpublishCourseResponse>(NotFoundError);
        }

        if (course.Status != CourseStatus.Published)
        {
            return Result.Failure<UnpublishCourseResponse>(NotPublishedError);
        }

        course.Unpublish(command.Reason);

        // 1. Record security audit
        await securityAudit.RecordAuditAsync(
            "course.unpublished",
            adminUserId,
            $"CourseId={course.Id}, Title={course.Title}, Reason={command.Reason}",
            ipAddress,
            cancellationToken).ConfigureAwait(false);

        // 2. Lookup instructor email and enqueue notification outbox message
        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == course.InstructorId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is not null)
        {
            var contacts = await userContactReader.GetUsersContactInfoAsync(
                [instructorProfile.UserId],
                cancellationToken).ConfigureAwait(false);

            if (contacts.TryGetValue(instructorProfile.UserId, out var contact) && !string.IsNullOrWhiteSpace(contact.Email))
            {
                var bodyHtml = $@"<div style=""font-family: sans-serif; line-height: 1.6;"">
<h2>แจ้งเตือนการระงับการเผยแพร่คอร์สเรียน</h2>
<p>เรียน อาจารย์ {System.Net.WebUtility.HtmlEncode(instructorProfile.DisplayName)},</p>
<p>คอร์สเรียน <strong>{System.Net.WebUtility.HtmlEncode(course.Title)}</strong> ของท่าน ถูกระงับการเผยแพร่บนระบบ SIRI UpSkill</p>
<p><strong>เหตุผล:</strong> {System.Net.WebUtility.HtmlEncode(command.Reason)}</p>
<p>หากท่านมีข้อสงสัยหรือต้องการปรับปรุงข้อมูลเพื่อขอรับการพิจารณาใหม่ กรุณาติดต่อทีมงานผู้ดูแลระบบ</p>
</div>";

                emailOutbox.Enqueue(
                    toEmail: contact.Email,
                    subject: $"[SIRI UpSkill] แจ้งเตือนการระงับการเผยแพร่คอร์ส: {course.Title}",
                    bodyHtml: bodyHtml,
                    templateKey: "course-unpublished-notification");
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Unpublishing moves a course from publicly-visible back to not-visible — the same class of
        // visibility change ApproveCourseHandler evicts the output cache for (see that handler's own doc
        // comment, which names this exact case as the "whichever future handler moves visibility" gap).
        // Without this, a freshly-unpublished course could still be served to new visitors out of the
        // 5-minute output cache.
        await outputCacheStore.EvictByTagAsync(CourseOutputCache.Tag, cancellationToken).ConfigureAwait(false);

        // Drop it from the search index too. Not needed for correctness (the search re-checks Status = Published in PostgreSQL, so a stale
        // entry is never returned) but it stops the engine ranking/returning an ineligible course. Best effort — logs instead of throwing.
        await searchIndexer.SyncCourseAsync(course.Id, cancellationToken).ConfigureAwait(false);

        return new UnpublishCourseResponse(course.Id, course.Status, command.Reason);
    }
}
