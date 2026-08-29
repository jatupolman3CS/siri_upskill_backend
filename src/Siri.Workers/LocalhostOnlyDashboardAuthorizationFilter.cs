using System.Net;
using Hangfire.Dashboard;

namespace Siri.Workers;

/// <summary>
/// Guard for the Hangfire dashboard (task P0-23, tightened by P0-22). Authorizes a request if EITHER
/// of two checks passes:
/// <list type="bullet">
/// <item>the request originates from localhost (the original P0-23 check, unchanged); or</item>
/// <item>the request carries a valid JWT bearer token whose role claims include "Admin" or
/// "SuperAdmin" (task P0-22's addition). This works without any extra wiring here because
/// <c>Siri.Api/Program.cs</c> calls <c>app.UseAuthentication()</c> before
/// <c>app.UseHangfireDashboard(...)</c> in the middleware pipeline — JwtBearer authentication runs
/// for every request that reaches this filter, dashboard or not, so <c>HttpContext.User</c> is
/// already populated from any <c>Authorization: Bearer ...</c> header by the time
/// <see cref="Authorize"/> below runs; this filter only needs to read it back, not validate a token
/// itself.</item>
/// </list>
/// The literal role-name strings below intentionally duplicate
/// <c>Siri.Modules.Identity.Domain.Role.AdminName</c>/<c>SuperAdminName</c> rather than adding a new
/// <c>Siri.Workers</c> → <c>Siri.Modules.Identity</c> project reference just for two constants —
/// <c>Siri.Workers</c> has never depended on a module's <c>Domain</c> before (it only references
/// <c>Siri.Persistence</c> + <c>Siri.Modules.Notification</c>, for the recurring-job types it
/// registers), and these exact strings are already effectively load-bearing constants shared across a
/// couple of places in this codebase (e.g. <c>RoleConfiguration</c>'s seed data,
/// <c>AccessTokenGenerator</c>'s claims) via comments rather than a shared reference. If they ever
/// drift, the practical failure mode is "dashboard access denied even with the right role" (fails
/// closed), not an authorization bypass.
/// <para>
/// <b>Why this shape, and not a full cookie-based admin login for the dashboard:</b> Hangfire's
/// dashboard is reached by plain browser navigation (a GET request), which does not naturally carry a
/// JWT bearer token the way an API client does — this codebase has no admin frontend/login-session UI
/// yet that could set an auth cookie for dashboard access, and building one just for this is
/// disproportionate scope for this task (it belongs with a future admin frontend, if/when one exists).
/// The role-claim branch above is therefore mainly useful today for anyone testing the dashboard via a
/// script/API client that already holds a real access token (e.g. curling <c>/hangfire</c> with an
/// <c>Authorization</c> header) — and it is exactly the check a future admin-frontend session
/// mechanism can plug straight into once one exists, since it only depends on
/// <c>HttpContext.User</c> already being populated, regardless of how that principal got there.
/// </para>
/// <para>
/// <b>Known remaining gap (not closed by this task):</b> a real person sitting at a browser, reaching
/// this dashboard over the network from a non-localhost machine, has no way to present a bearer token
/// through plain navigation — so pure browser-based remote admin access to the dashboard is still
/// <em>not</em> gated by role. Today it is protected only by the localhost check (or by not exposing
/// the port publicly at the reverse-proxy/firewall level — see <c>DEPLOYMENT.md</c>). Fully closing
/// this needs a real admin session mechanism (cookie-based login for the dashboard, or a signed
/// short-lived link) that does not exist yet in this codebase.
/// </para>
/// </summary>
public sealed class LocalhostOnlyDashboardAuthorizationFilter : IDashboardAuthorizationFilter
{
    private const string AdminRoleName = Siri.SharedKernel.RoleNames.Admin;
    private const string SuperAdminRoleName = Siri.SharedKernel.RoleNames.SuperAdmin;

    public bool Authorize(DashboardContext context)
    {
        if (IsFromLocalhost(context))
        {
            return true;
        }

        var user = context.GetHttpContext().User;

        return user.Identity?.IsAuthenticated == true &&
            (user.IsInRole(AdminRoleName) || user.IsInRole(SuperAdminRoleName));
    }

    private static bool IsFromLocalhost(DashboardContext context)
    {
        // Fail closed: an unparsable/unknown remote address is never treated as "local".
        var remoteIp = context.Request.RemoteIpAddress;
        return !string.IsNullOrEmpty(remoteIp) && IPAddress.TryParse(remoteIp, out var ip) && IPAddress.IsLoopback(ip);
    }
}
