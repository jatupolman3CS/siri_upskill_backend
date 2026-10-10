namespace Siri.Modules.Live.Domain;

/// <summary>
/// What kind of Google account an instructor connected (P11-13 contract section 3) — <b>detected, not asked</b>, from Google's userinfo
/// <c>hd</c> (hosted domain) claim. <c>hd</c> says "Google Workspace", not "can record": a Workspace plan without Meet recording simply yields no file
/// (the import then ends as <c>NoRecording</c> and the instructor uploads by hand). Enum type and members stay PascalCase (D-17); exposed as a string.
/// </summary>
public enum GoogleAccountKind
{
    /// <summary>A consumer Google account (Gmail etc.): <c>hd</c> was read and is absent. Recordings are never imported automatically.</summary>
    Personal,

    /// <summary>A Google Workspace account: <c>hd</c> is present.</summary>
    Workspace,

    /// <summary>The row predates the account-kind check, or the lookup has not succeeded yet. The UI shows "checking…"; capability is manual until it resolves.</summary>
    Unknown,
}
