using Microsoft.Extensions.Options;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>
/// The single source of the rules for an online-room URL (P11-03 contract section 6.4) — used for the instructor's pasted
/// link <em>and</em> for the URL Google returns, so both pass the same allow-list before anything is stored. A meeting link
/// is a capability URL that learners are redirected to, so it is treated as untrusted input: it must not be able to carry
/// <c>javascript:</c>/<c>data:</c> schemes (XSS), point at an arbitrary host (open redirect / SSRF-style abuse) or hide its
/// real host behind user-info (<c>https://meet.google.com@evil.com</c>).
/// <para>
/// Accepted: <c>https</c> only, default port only, no user-info, host equal to an allowed host <em>or a sub-domain of it</em>
/// (<c>us02web.zoom.us</c>; never a mere suffix such as <c>evilzoom.us</c>). The result is the normalised absolute URL:
/// lower-cased scheme/host, fragment dropped, everything else percent-escaped by <see cref="Uri"/>.
/// </para>
/// </summary>
public sealed class MeetingLinkValidator(IOptions<LiveOptions> options)
{
    public const int MaxLength = 500;

    public const string InvalidReason = "live.meeting_link_invalid";

    public const string HostNotAllowedReason = "live.meeting_link_host_not_allowed";

    public Result<string> Validate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Invalid("กรุณาระบุลิงก์ห้องประชุม");
        }

        var trimmed = input.Trim();

        if (trimmed.Length > MaxLength)
        {
            return Invalid($"ลิงก์ห้องประชุมต้องยาวไม่เกิน {MaxLength} ตัวอักษร");
        }

        // No whitespace/control characters anywhere (a tab/newline inside the URL is how scheme-filter bypasses work),
        // and no backslash (parsers disagree on whether it is a path separator).
        if (trimmed.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || trimmed.Contains('\\', StringComparison.Ordinal))
        {
            return Invalid("ลิงก์ห้องประชุมมีอักขระที่ไม่อนุญาต");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return Invalid("ลิงก์ห้องประชุมต้องเป็น URL แบบ https เท่านั้น");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            return Invalid("ลิงก์ห้องประชุมต้องไม่มีชื่อผู้ใช้/รหัสผ่านใน URL");
        }

        if (!uri.IsDefaultPort)
        {
            return Invalid("ลิงก์ห้องประชุมต้องไม่ระบุพอร์ต");
        }

        var host = uri.IdnHost.ToLowerInvariant();
        if (!IsAllowedHost(host))
        {
            return Result.Failure<string>(
                DomainError.Validation($"ลิงก์ห้องประชุมต้องมาจาก {string.Join(", ", options.Value.GetEffectiveAllowedMeetingHosts())} เท่านั้น")
                    .WithReason(HostNotAllowedReason));
        }

        return Result.Success(uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped));
    }

    /// <summary>Exact match with an allowed host, or a true sub-domain of it (<c>.{allowed}</c> suffix) — never a bare
    /// substring/suffix match, so <c>evilzoom.us</c> and <c>zoom.us.evil.com</c> are rejected. A trailing-dot host is rejected.</summary>
    internal bool IsAllowedHost(string host)
    {
        if (host.Length == 0 || host.EndsWith('.'))
        {
            return false;
        }

        foreach (var allowed in options.Value.GetEffectiveAllowedMeetingHosts())
        {
            if (string.Equals(host, allowed, StringComparison.Ordinal)
                || host.EndsWith("." + allowed, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static Result<string> Invalid(string message) =>
        Result.Failure<string>(DomainError.Validation(message).WithReason(InvalidReason));
}
