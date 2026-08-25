namespace Siri.Modules.Community.Domain;

/// <summary>
/// Moderation outcome of a <see cref="REPORT"/>. Enum type and its members stay PascalCase even though
/// this module otherwise uses UPPERCASE naming (docs/DECISIONS.md D-17) — only the property that holds
/// the enum (<see cref="REPORT.STATUS"/>) is UPPERCASE, never the enum type/members themselves.
/// </summary>
public enum ReportStatus
{
    /// <summary>Reported, not yet reviewed by an admin. The only status <see cref="REPORT.Create"/>
    /// ever starts a new report at.</summary>
    Pending,

    /// <summary>An admin reviewed the report and acted on it (e.g. hid the discussion post).</summary>
    Resolved,

    /// <summary>An admin reviewed the report and found no issue with the discussion post.</summary>
    Dismissed,
}
