using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Features.ListSessions;

/// <summary>
/// One of the caller's own <see cref="USER_SESSION"/>s, shaped for a "signed-in devices" UI
/// (docs/SECURITY.md §2: "ผู้ใช้ดู/ถอดอุปกรณ์เองได้ที่หน้า 'อุปกรณ์ที่เข้าสู่ระบบ'"). Never returned for
/// any session but the caller's own — see <see cref="ListSessionsHandler"/>'s doc comment for the
/// ownership-scoped query this is always built from.
/// <para>
/// <b>Field-by-field disclosure decisions</b> (task instruction: "make a specific, justified choice,
/// don't leave it ambiguous"):
/// <list type="bullet">
/// <item><see cref="IpAddress"/> — shown in full, not masked. Masking earns its keep when a value could
/// be seen by someone other than its owner; this DTO is only ever returned to the session's own
/// <see cref="Siri.SharedKernel.IUserContext.UserId"/> (see the ownership-scoped query in
/// <see cref="ListSessionsHandler"/>), so that concern does not apply here. security.md's
/// masking/encryption rules target data that is sensitive <em>to someone else</em> seeing it (bank
/// account numbers, tax ids, card numbers) or is a secret in its own right (password, token, OTP) — an
/// IP address is neither, and it is already stored unmasked/unencrypted in <see cref="USER_SESSION"/>/
/// <see cref="SECURITY_AUDIT"/> today with no masking precedent (P0-16/P0-17). The entire reason
/// SECURITY.md calls for this page to exist is so the account owner can notice "that login was NOT from
/// my home/office network" — a masked "203.0.***.***" would defeat exactly that use case.</item>
/// <item><see cref="UserAgent"/> — the raw stored string, not parsed into a friendly "Chrome on
/// Windows"-style label. A real user-agent parser is a nontrivial piece of logic in its own right (a
/// large, constantly-stale lookup table of browser/OS signatures) this task's scope was never asked to
/// build, and getting it subtly wrong would show the user an incorrect device label — worse than no
/// label at all. <see cref="DeviceName"/> already exists as exactly the human-friendly label this field
/// would otherwise approximate (the client supplies it once at login — see
/// <see cref="Login.LoginCommand.DeviceName"/>'s own doc comment); a raw, honest USER-Agent string is
/// strictly more useful to a security-conscious user auditing this page than a possibly-wrong guess, so
/// this task adds no parsing.</item>
/// </list>
/// </para>
/// </summary>
public sealed record SessionSummary(
    Guid SessionId,
    string? DeviceName,
    string? UserAgent,
    string? IpAddress,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    bool IsActive,
    DateTime? RevokedAtUtc,
    bool IsCurrentSession);

/// <summary>GET /api/identity/sessions' response body — every session <see cref="ListSessionsHandler"/>
/// decided to include (active, plus recently-revoked within its recency window — see that class's doc
/// comment), newest-activity first.</summary>
public sealed record ListSessionsResponse(IReadOnlyList<SessionSummary> Sessions);

/// <summary>Hand-written entity-to-DTO mapping — not AutoMapper (backend.md: "ห้ามใช้ AutoMapper —
/// เขียน ToResponse() extension method เอง ชัดเจนกว่าและ debug ได้"). Pure, no I/O, so it is directly
/// unit-testable without a database (see <c>tests/Siri.UnitTests/Identity/SessionSummaryMappingTests.cs</c>).</summary>
public static class UserSessionMappingExtensions
{
    /// <param name="session">The session to map.</param>
    /// <param name="currentSessionId">The caller's own current session id (from
    /// <see cref="ListSessionsCommand.CurrentSessionId"/>), or <c>null</c> if unknown — determines
    /// <see cref="SessionSummary.IsCurrentSession"/>.</param>
    public static SessionSummary ToSummary(this USER_SESSION session, Guid? currentSessionId) =>
        new(
            session.Id,
            session.DeviceName,
            session.UserAgent,
            session.IpAddress,
            session.CreatedAtUtc,
            session.LastSeenAtUtc,
            session.IsActive,
            session.RevokedAtUtc,
            currentSessionId is not null && session.Id == currentSessionId);
}
