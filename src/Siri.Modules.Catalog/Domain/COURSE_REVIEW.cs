using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

public sealed class COURSE_REVIEW : IAuditable
{
    public Guid Id { get; private set; }
    public Guid CourseId { get; private set; }
    public Guid UserId { get; private set; }
    public int Rating { get; private set; }
    public string? Comment { get; private set; }
    public bool IsPublished { get; private set; } = true;

    // ---- IAuditable ---------------------------------------------------------------------------
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    private COURSE_REVIEW() { }

    public static Result<COURSE_REVIEW> Create(
        Guid courseId,
        Guid userId,
        int rating,
        string? comment,
        DateTime createdAtUtc)
    {
        if (courseId == Guid.Empty)
        {
            return Result.Failure<COURSE_REVIEW>(DomainError.Validation("รหัสคอร์สเรียนไม่ถูกต้อง"));
        }

        if (userId == Guid.Empty)
        {
            return Result.Failure<COURSE_REVIEW>(DomainError.Validation("รหัสผู้ใช้ไม่ถูกต้อง"));
        }

        if (rating < 1 || rating > 5)
        {
            return Result.Failure<COURSE_REVIEW>(DomainError.Validation("คะแนนรีวิวต้องอยู่ระหว่าง 1 ถึง 5 ดาว"));
        }

        return Result.Success(new COURSE_REVIEW
        {
            Id = Guid.NewGuid(),
            CourseId = courseId,
            UserId = userId,
            Rating = rating,
            Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            IsPublished = true,
            CreatedAtUtc = createdAtUtc,
            CreatedBy = userId,
        });
    }

    public void Update(int rating, string? comment, DateTime updatedAtUtc)
    {
        if (rating >= 1 && rating <= 5)
        {
            Rating = rating;
        }

        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        UpdatedAtUtc = updatedAtUtc;
        UpdatedBy = UserId;
    }
}
