namespace Siri.Modules.Live.Domain;

/// <summary>
/// Where a <see cref="SESSION_MEETING"/>'s room comes from (docs/contracts/P11-03-live-module-google-meetings.md
/// §2.2). Enum type and members stay PascalCase even though this module's entities are UPPERCASE
/// (docs/DECISIONS.md D-17) — only the property that holds the enum (<see cref="SESSION_MEETING.PROVIDER"/>)
/// is UPPERCASE. Stored as a string (<c>HasConversion&lt;string&gt;()</c>), so renaming a member is a data change.
/// </summary>
public enum MeetingProvider
{
    /// <summary>A Google Calendar event with a Meet conference created on the instructor's own connected
    /// Google account (<see cref="INSTRUCTOR_GOOGLE_ACCOUNT"/>).</summary>
    GoogleMeet,

    /// <summary>The instructor pasted a Meet/Zoom/Teams link themselves (allow-listed host, https only).</summary>
    Manual,

    /// <summary>Dev-only fake provider (<c>Live:Provider=Logging</c>) — never allowed in production
    /// (<c>ProductionConfigurationGuard</c>).</summary>
    Logging,
}
