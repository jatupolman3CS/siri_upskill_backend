namespace Siri.Modules.Community.Domain;

/// <summary>
/// Moderation state of a <see cref="DISCUSSION"/> post. Enum type and its members stay PascalCase even
/// though this module otherwise uses UPPERCASE naming (docs/DECISIONS.md D-17) — only the property that
/// holds the enum (<see cref="DISCUSSION.STATUS"/>) is UPPERCASE, never the enum type/members themselves.
/// </summary>
public enum DiscussionStatus
{
    /// <summary>Normal, publicly visible post.</summary>
    Visible,

    /// <summary>Hidden by moderation — excluded from the Q&amp;A tab, but not deleted (see
    /// <see cref="DISCUSSION"/>'s own doc comment for the distinction from <c>IsDeleted</c>).</summary>
    Hidden,

    /// <summary>Flagged for review — still visible, pending a moderation decision.</summary>
    Flagged,
}
