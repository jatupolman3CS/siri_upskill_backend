using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CreateLearningPath;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UpdateLearningPath;

public sealed record UpdateLearningPathCommand(
    string Slug,
    string Title,
    string? Description,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<Guid>? CourseIds);

public sealed class UpdateLearningPathValidator : AbstractValidator<UpdateLearningPathCommand>
{
    public UpdateLearningPathValidator()
    {
        RuleFor(c => c.Slug).NotEmpty().MaximumLength(200).Matches(@"^[a-z0-9-]+$")
            .WithMessage("Slug ต้องประกอบด้วยตัวพิมพ์เล็ก ตัวเลข และยัติภังค์ (-) เท่านั้น");
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateLearningPathHandler(AppDbContext dbContext)
{
    public async Task<Result<LearningPathDetailResponse>> HandleAsync(Guid id, UpdateLearningPathCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var path = await dbContext.LearningPaths()
            .Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (path is null)
        {
            return Result.Failure<LearningPathDetailResponse>(DomainError.NotFound("ไม่พบเส้นทางการเรียนที่ระบุ"));
        }

        var normalizedSlug = command.Slug.Trim().ToLowerInvariant();
        var slugConflict = await dbContext.LearningPaths()
            .AnyAsync(p => p.Slug == normalizedSlug && p.Id != id, cancellationToken)
            .ConfigureAwait(false);

        if (slugConflict)
        {
            return Result.Failure<LearningPathDetailResponse>(DomainError.Conflict("มีเส้นทางการเรียนอื่นที่ใช้ slug นี้แล้ว"));
        }

        path.Update(command.Slug, command.Title, command.Description, command.SortOrder, command.IsActive);

        if (command.CourseIds is not null)
        {
            var validCourseIds = await dbContext.Courses()
                .Where(c => command.CourseIds.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            path.SetCourses(validCourseIds);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var courseDetails = await (
            from item in dbContext.LearningPathItems().AsNoTracking()
            join course in dbContext.Courses().AsNoTracking() on item.CourseId equals course.Id
            where item.PathId == path.Id
            orderby item.SortOrder
            select new LearningPathCourseItemResponse(course.Id, course.Title, course.Slug, item.SortOrder)
        ).ToListAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new LearningPathDetailResponse(
            path.Id,
            path.Slug,
            path.Title,
            path.Description,
            path.SortOrder,
            path.IsActive,
            courseDetails,
            path.CreatedAtUtc));
    }
}
