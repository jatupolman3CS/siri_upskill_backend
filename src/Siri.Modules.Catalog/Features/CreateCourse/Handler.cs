using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.CreateCourse;

/// <summary>
/// Creates a new draft course. The caller's own <see cref="InstructorProfile"/> (must exist and be
/// <see cref="InstructorApplicationStatus.Approved"/> — see this class's own reasoning below) supplies
/// <see cref="Course.InstructorId"/>; never accepted from the client. Slug is generated from
/// <see cref="CreateCourseCommand.Title"/> (<see cref="ThaiSlugGenerator"/> + a uniqueness suffix
/// computed from one bulk query, mirroring <c>CreateCategoryHandler</c>'s own sibling-sort-order
/// computation style) — never accepted from the client either, so there is no client-supplied slug field
/// to collide with the generated numbering scheme.
/// <para>
/// <b>Why "Approved", not just "has a profile at all"</b>: <c>InstructorOnly</c> (the group policy)
/// already guarantees the caller holds the Instructor, Admin, or SuperAdmin role. Anyone with the
/// Instructor role specifically got it only through <c>ApproveInstructorApplicationHandler</c>, so they
/// always have an Approved profile by construction. This check's real purpose is the Admin/SuperAdmin
/// case: <c>InstructorOnly</c> lets them reach this endpoint for moderation purposes (same reasoning
/// <c>AuthorizationPolicyExtensions</c> gives throughout), but an admin cannot originate a course "as" an
/// instructor without actually being one themselves — there is no <c>InstructorId</c> to attach it to
/// otherwise.
/// </para>
/// </summary>
public sealed class CreateCourseHandler(AppDbContext dbContext)
{
    private const string FallbackSlugBase = "course";
    private const int MaxSlugBaseLength = 190; // Courses.Slug is nvarchar(200) — leaves room for a "-NN" suffix

    public async Task<Result<CreateCourseResponse>> HandleAsync(Guid userId, CreateCourseCommand command, CancellationToken cancellationToken)
    {
        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || instructorProfile.Status != InstructorApplicationStatus.Approved)
        {
            return Result.Failure<CreateCourseResponse>(DomainError.Forbidden("ต้องเป็นผู้สอนที่ได้รับอนุมัติก่อนถึงจะสร้างคอร์สได้"));
        }

        var categoryExists = await dbContext.Categories()
            .AsNoTracking()
            .AnyAsync(c => c.Id == command.CategoryId, cancellationToken)
            .ConfigureAwait(false);

        if (!categoryExists)
        {
            return Result.Failure<CreateCourseResponse>(DomainError.NotFound("ไม่พบหมวดหมู่ที่ระบุ"));
        }

        var slug = await GenerateUniqueSlugAsync(command.Title, cancellationToken).ConfigureAwait(false);

        var course = Course.Create(slug, command.Title, instructorProfile.Id, command.CategoryId, command.Level, command.Language, command.Price);
        dbContext.Courses().Add(course);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException)
        {
            // Backstop for the narrow race where two concurrent creates land on the exact same generated
            // slug (both read the "existing slugs" snapshot before either committed) — same shape
            // CreateCategoryHandler's own catch gives, but this slug was never user-chosen, so asking the
            // client to just retry the whole request (which will generate a fresh slug) is the honest
            // response, not a slug-specific conflict message.
            return Result.Failure<CreateCourseResponse>(DomainError.Conflict("สร้างคอร์สไม่สำเร็จเนื่องจากมีการสร้างพร้อมกัน กรุณาลองใหม่อีกครั้ง"));
        }

        return ToResponse(course);
    }

    private async Task<string> GenerateUniqueSlugAsync(string title, CancellationToken cancellationToken)
    {
        var baseSlug = ThaiSlugGenerator.GenerateBaseSlug(title);
        if (string.IsNullOrEmpty(baseSlug))
        {
            baseSlug = FallbackSlugBase;
        }
        else if (baseSlug.Length > MaxSlugBaseLength)
        {
            baseSlug = baseSlug[..MaxSlugBaseLength];
        }

        var collidingSlugsList = await dbContext.Courses()
            .AsNoTracking()
            .Where(c => c.Slug == baseSlug || c.Slug.StartsWith(baseSlug + "-"))
            .Select(c => c.Slug)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var collidingSlugs = collidingSlugsList.ToHashSet();

        if (!collidingSlugs.Contains(baseSlug))
        {
            return baseSlug;
        }

        var suffix = 2;
        while (collidingSlugs.Contains($"{baseSlug}-{suffix}"))
        {
            suffix++;
        }

        return $"{baseSlug}-{suffix}";
    }

    private static CreateCourseResponse ToResponse(Course course) =>
        new(course.Id, course.Slug, course.Title, course.InstructorId, course.CategoryId, course.Level, course.Language,
            course.Price, course.Currency, course.Status);
}
