using Microsoft.EntityFrameworkCore;
using Siri.Modules.Live.Domain;
using Siri.Persistence;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for the Live module's entities on the shared <see cref="AppDbContext"/> —
/// the same zero-<c>DbSet</c>-on-<c>AppDbContext</c> pattern every other module uses (see
/// <c>Siri.Modules.Community.Infrastructure.AppDbContextCommunityExtensions</c>): <see cref="AppDbContext"/>
/// carries no module-owned <c>DbSet&lt;T&gt;</c> properties, to avoid a circular project reference back to
/// every module. Entity configurations are picked up by <c>AppDbContext</c>'s scan of the loaded
/// <c>Siri.Modules.*</c> assemblies, so nothing else needs wiring here.
/// <para>Entity type names are UPPERCASE (D-17); these accessor method names are not.</para>
/// </summary>
public static class AppDbContextLiveExtensions
{
    public static DbSet<INSTRUCTOR_GOOGLE_ACCOUNT> InstructorGoogleAccounts(this AppDbContext context) =>
        context.Set<INSTRUCTOR_GOOGLE_ACCOUNT>();

    public static DbSet<SESSION_MEETING> SessionMeetings(this AppDbContext context) =>
        context.Set<SESSION_MEETING>();

    public static DbSet<SESSION_INVITE> SessionInvites(this AppDbContext context) =>
        context.Set<SESSION_INVITE>();

    public static DbSet<SESSION_JOIN_LOG> SessionJoinLogs(this AppDbContext context) =>
        context.Set<SESSION_JOIN_LOG>();
}
