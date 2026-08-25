using Siri.Modules.Community.Domain;

namespace Siri.Modules.Community.Application.Response;

/// <summary>API-facing projection of <see cref="REPORT"/> — never expose the EF entity itself
/// (.claude/rules/backend.md: "Response ต้องเป็น DTO เสมอ ห้ามคืน EF entity ออก API").</summary>
public sealed record ReportResponse(
    Guid Id,
    Guid DiscussionId,
    Guid ReportedByUserId,
    string Reason,
    ReportStatus Status,
    DateTime? ResolvedAtUtc,
    DateTime CreatedAtUtc);
