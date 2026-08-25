using Siri.Modules.Community.Domain;

namespace Siri.Modules.Community.Application.Response;

/// <summary>API-facing projection of <see cref="DISCUSSION"/> — never expose the EF entity itself
/// (.claude/rules/backend.md: "Response ต้องเป็น DTO เสมอ ห้ามคืน EF entity ออก API").</summary>
public sealed record DiscussionResponse(
    Guid Id,
    Guid CourseId,
    Guid? EpisodeId,
    Guid UserId,
    Guid? ParentId,
    string Body,
    bool IsInstructorAnswer,
    DiscussionStatus Status,
    int UpvoteCount,
    DateTime CreatedAtUtc);
