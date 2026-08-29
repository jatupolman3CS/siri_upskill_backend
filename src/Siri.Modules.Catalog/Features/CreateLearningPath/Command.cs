using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.CreateLearningPath;

public sealed record CreateLearningPathCommand(
    string Slug,
    string Title,
    string? Description,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<Guid>? CourseIds);

public sealed record LearningPathCourseItemResponse(
    Guid CourseId,
    string CourseTitle,
    string CourseSlug,
    int SortOrder);

public sealed record LearningPathDetailResponse(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<LearningPathCourseItemResponse> Courses,
    DateTime CreatedAtUtc);

public sealed class CreateLearningPathValidator : AbstractValidator<CreateLearningPathCommand>
{
    public CreateLearningPathValidator()
    {
        RuleFor(c => c.Slug).NotEmpty().MaximumLength(200).Matches(@"^[a-z0-9-]+$")
            .WithMessage("Slug ต้องประกอบด้วยตัวพิมพ์เล็ก ตัวเลข และยัติภังค์ (-) เท่านั้น");
        RuleFor(c => c.Title).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Description).MaximumLength(2000);
        RuleFor(c => c.SortOrder).GreaterThanOrEqualTo(0);
    }
}

public sealed class CreateLearningPathHandler(AppDbContext dbContext)
{
    public async Task<Result<LearningPathDetailResponse>> HandleAsync(CreateLearningPathCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var slugExists = await dbContext.LearningPaths()
            .AnyAsync(p => p.Slug == command.Slug.Trim().ToLowerInvariant(), cancellationToken)
            .ConfigureAwait(false);

        if (slugExists)
        {
            return Result.Failure<LearningPathDetailResponse>(DomainError.Conflict("มีเส้นทางการเรียนที่ใช้ slug นี้แล้ว"));
        }

        var path = LEARNING_PATH.Create(command.Slug, command.Title, command.Description, command.SortOrder, command.IsActive);

        if (command.CourseIds is { Count: > 0 })
        {
            var validCourseIds = await dbContext.Courses()
                .Where(c => command.CourseIds.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            path.SetCourses(validCourseIds);
        }

        dbContext.LearningPaths().Add(path);
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
