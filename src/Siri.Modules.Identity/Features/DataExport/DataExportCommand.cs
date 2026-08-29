namespace Siri.Modules.Identity.Features.DataExport;

/// <summary>
/// Command to request personal data export for the authenticated user (P7-04).
/// </summary>
public sealed record DataExportCommand(Guid UserId);
