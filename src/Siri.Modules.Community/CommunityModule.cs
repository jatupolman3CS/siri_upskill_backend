using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Community.Application;
using Siri.Modules.Community.Application.Response;
using Siri.Modules.Community.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Modules.Community;

/// <summary>
/// Composition root for the Community module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// <para>
/// This module (docs/DECISIONS.md D-17) uses the Repository+Service pattern and UPPERCASE entity naming,
/// unlike Identity/Catalog/Notification's vertical-slice/PascalCase — see <c>.claude/rules/backend.md</c>.
/// Scaffold pass: every <c>Application/*Service.cs</c> method is stubbed, so the module compiles, migrates,
/// and routes correctly, but calling any endpoint below throws at runtime until a later task fills in the
/// real logic.
/// </para>
/// </summary>
public static class CommunityModule
{
    /// <summary>Registers the Community module's services (repositories, application services, validators)
    /// into the container.</summary>
    public static IServiceCollection AddCommunityModule(this IServiceCollection services)
    {
        services.AddScoped<IDiscussionRepository, DiscussionRepository>();
        services.AddScoped<IReportRepository, ReportRepository>();

        services.AddScoped<DiscussionService>();
        services.AddScoped<ReportService>();

        services.AddScoped<IValidator<CreateDiscussionCommand>, CreateDiscussionValidator>();
        services.AddScoped<IValidator<CreateReportCommand>, CreateReportValidator>();

        return services;
    }

    /// <summary>
    /// Maps the Community module's minimal API endpoints onto the host's route builder.
    /// <para>
    /// Default-deny at the top-level group (bare <c>.RequireAuthorization()</c> — any authenticated caller,
    /// same shape every other module's <c>Map*Endpoints</c> follows). Discussions inherit that bare policy:
    /// any authenticated learner may create/list/upvote, and <see cref="DiscussionService.DeleteAsync"/>
    /// enforces caller-owns-the-post itself (no route-level policy can express that). Reports are split
    /// across two separate <c>MapGroup("/reports")</c> calls at the SAME path prefix but different policies
    /// — creating a report is any authenticated caller's action, while the moderation queue
    /// (list-pending/resolve/dismiss) is <see cref="AuthorizationPolicyNames.AdminOnly"/>. This is a
    /// supported ASP.NET Core pattern (each <c>MapGroup</c> call returns an independent builder whose
    /// convention only applies to routes registered through it) — the two groups' route templates never
    /// collide (<c>POST /</c> vs <c>GET /pending</c> etc.).
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapCommunityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/community").WithTags("Community").RequireAuthorization();

        var discussionGroup = group.MapGroup("/discussions");
        discussionGroup.MapCreateDiscussionEndpoint();
        discussionGroup.MapListDiscussionsByEpisodeEndpoint();
        discussionGroup.MapUpvoteDiscussionEndpoint();
        discussionGroup.MapDeleteDiscussionEndpoint();

        var reportGroup = group.MapGroup("/reports");
        reportGroup.MapCreateReportEndpoint();

        var reportAdminGroup = group.MapGroup("/reports").RequireAuthorization(AuthorizationPolicyNames.AdminOnly);
        reportAdminGroup.MapListPendingReportsEndpoint();
        reportAdminGroup.MapResolveReportEndpoint();
        reportAdminGroup.MapDismissReportEndpoint();

        return endpoints;
    }
}
