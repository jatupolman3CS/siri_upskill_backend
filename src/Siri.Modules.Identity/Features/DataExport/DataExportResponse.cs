namespace Siri.Modules.Identity.Features.DataExport;

/// <summary>
/// Root container of user personal data exported per PDPA Right to Data Portability (P7-04).
/// </summary>
public sealed record DataExportResponse(
    DateTime ExportedAtUtc,
    UserExportDto Profile,
    IReadOnlyList<UserSessionExportDto> Sessions,
    IReadOnlyList<SecurityAuditExportDto> SecurityAudits);

public sealed record UserExportDto(
    Guid Id,
    string Email,
    string DisplayName,
    string? PhoneNumber,
    string? AvatarUrl,
    string Status,
    bool TwoFactorEnabled,
    DateTime CreatedAtUtc,
    IReadOnlyList<string> Roles);

public sealed record UserSessionExportDto(
    Guid Id,
    string DeviceId,
    string? DeviceName,
    string? UserAgent,
    string? IpAddress,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    bool IsActive);

public sealed record SecurityAuditExportDto(
    string EventType,
    DateTime OccurredAtUtc,
    string? IpAddress);
