using Siri.Modules.Identity.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.GetMe;

/// <summary>
/// GET /api/identity/me's response body — the signed-in caller's own public-facing profile, just enough
/// for a header avatar + name. Deliberately omits everything else on <see cref="USER"/> (password hash,
/// phone number, status, 2FA flag, concurrent-session override, ...): this endpoint is polled on every
/// page load, so it discloses the minimum.
/// <list type="bullet">
/// <item><see cref="AvatarUrl"/> — <c>null</c> when the account has no avatar (the UI shows initials).</item>
/// <item><see cref="Roles"/> — the role names exactly as stored (<see cref="ROLE.Name"/>, e.g.
/// <c>Learner</c>, <c>Instructor</c>), sorted ordinally so the output is deterministic. Informational
/// only: the server still decides what each role may do on every request — hiding or showing UI from
/// this list is never an authorization check (security.md).</item>
/// </list>
/// </summary>
public sealed record MeResponse(
    Guid Id,
    string Email,
    string DisplayName,
    string? AvatarUrl,
    IReadOnlyList<string> Roles);

/// <summary>Hand-written entity-to-DTO mapping — not AutoMapper (backend.md). Pure, no I/O, so it is
/// directly unit-testable without a database (see <c>tests/Siri.UnitTests/Identity/GetMeMappingTests.cs</c>).</summary>
public static class UserMeMappingExtensions
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบบัญชีผู้ใช้");

    /// <summary>
    /// Decides what GET /me answers for the row <see cref="GetMeHandler"/> loaded for the caller's id.
    /// A missing row and a <see cref="UserStatus.Deleted"/> one (an anonymized account — PDPA erasure
    /// keeps the row but scrubs its PII, see <see cref="USER.Anonymize"/>) are the same
    /// <see cref="DomainError.NotFound"/>: an access token minted before the erasure is still
    /// cryptographically valid for up to its lifetime, and it must not be able to read the placeholder
    /// identity left behind. Every other status is returned as-is — the caller is signed in as
    /// themselves and this is only their own data (a suspended user's refresh is blocked separately by
    /// <c>RefreshHandler</c>).
    /// </summary>
    public static Result<MeResponse> ToMeResult(this USER? user)
    {
        if (user is null || user.Status == UserStatus.Deleted)
        {
            return Result.Failure<MeResponse>(NotFoundError);
        }

        var roles = user.Roles
            .Select(r => r.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        return Result.Success(new MeResponse(user.Id, user.Email, user.DisplayName, user.AvatarUrl, roles));
    }
}
