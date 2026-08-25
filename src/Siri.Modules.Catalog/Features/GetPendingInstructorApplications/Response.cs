namespace Siri.Modules.Catalog.Features.GetPendingInstructorApplications;

/// <summary>Lean projection for the admin review list — no <c>RevenueSharePercent</c>/<c>Status</c>/
/// <c>ApprovedAtUtc</c> (every row here is implicitly <c>Pending</c> with no <c>ApprovedAtUtc</c> yet,
/// since the handler already filters to that status; database.md: "Projection ไปเป็น DTO ตรง ๆ ดีกว่าดึง
/// entity มาทั้งก้อนแล้ว map").</summary>
public sealed record InstructorApplicationSummary(
    Guid Id,
    Guid UserId,
    string DisplayName,
    string? Headline,
    string Bio,
    DateTime CreatedAtUtc);
