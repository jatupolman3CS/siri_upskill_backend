using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Cms.Application;
using Siri.Modules.Cms.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Modules.Cms;

/// <summary>
/// Composition root for the Cms module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// <para>
/// This module (docs/DECISIONS.md D-17) uses the Repository+Service pattern and UPPERCASE entity naming,
/// unlike Identity/Catalog/Notification's vertical-slice/PascalCase — see <c>.claude/rules/backend.md</c>
/// and each <c>Domain/*.cs</c> entity's own doc comment. Scaffold pass: every <c>Application/*Service.cs</c>
/// method is stubbed, so the module compiles and routes correctly, but calling any endpoint below throws at
/// runtime until a later task fills in the real logic — same "compiles/routes, does not yet run" state
/// <c>Siri.Modules.Payout.PayoutModule</c>'s own doc comment describes for the identical reason. In
/// particular: <see cref="Application.PostService"/>'s <c>ContentHtml</c> sanitization gap (its own doc
/// comment) must be closed before any Post endpoint goes live for real.
/// </para>
/// </summary>
public static class CmsModule
{
    /// <summary>Registers the Cms module's services (repositories, application services, validators) into
    /// the container.</summary>
    public static IServiceCollection AddCmsModule(this IServiceCollection services)
    {
        services.AddScoped<IBannerRepository, BannerRepository>();
        services.AddScoped<IMenuItemRepository, MenuItemRepository>();
        services.AddScoped<IPostRepository, PostRepository>();
        services.AddScoped<IRedirectRepository, RedirectRepository>();

        services.AddScoped<BannerService>();
        services.AddScoped<MenuItemService>();
        services.AddScoped<PostService>();
        services.AddScoped<RedirectService>();

        services.AddScoped<IValidator<CreateBannerCommand>, CreateBannerValidator>();
        services.AddScoped<IValidator<UpdateBannerCommand>, UpdateBannerValidator>();
        services.AddScoped<IValidator<ReorderBannersCommand>, ReorderBannersValidator>();

        services.AddScoped<IValidator<CreateMenuItemCommand>, CreateMenuItemValidator>();
        services.AddScoped<IValidator<UpdateMenuItemCommand>, UpdateMenuItemValidator>();

        services.AddScoped<IValidator<CreatePostCommand>, CreatePostValidator>();
        services.AddScoped<IValidator<UpdatePostCommand>, UpdatePostValidator>();
        services.AddScoped<IValidator<ChangePostStatusCommand>, ChangePostStatusValidator>();

        services.AddScoped<IValidator<CreateRedirectCommand>, CreateRedirectValidator>();

        return services;
    }

    /// <summary>
    /// Maps the Cms module's minimal API endpoints onto the host's route builder.
    /// <para>
    /// Default-deny at the top-level group (bare <c>.RequireAuthorization()</c>), same shape every other
    /// module's own <c>Map*Endpoints</c> doc comment establishes (see e.g.
    /// <c>Siri.Modules.Payout.PayoutModule.MapPayoutEndpoints</c>). Banner/MenuItem/Redirect management is
    /// entirely <see cref="AuthorizationPolicyNames.AdminOnly"/> — this module is almost entirely admin
    /// content-management (docs/REQUIREMENTS.md AD-01), with no "owner of their own resource" case the way
    /// Payout's <c>/instructor</c> sub-group has. Post is the one exception: admin-only
    /// create/update/status-change/delete/list, plus a separate public, unauthenticated
    /// <c>/api/cms/posts</c> group for the eventual blog page (<see cref="Application.PostEndpoints"/>'s own
    /// doc comment) — mirrors exactly how <c>Siri.Modules.Catalog.CatalogModule.MapCatalogEndpoints</c>
    /// splits its public course reads from its <c>AdminOnly</c>/<c>InstructorOnly</c> management routes.
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapCmsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cms").WithTags("Cms").RequireAuthorization();

        // Public blog read — .AllowAnonymous() on each mapping method itself (see PostEndpoints' own doc
        // comment), same "opt out per-endpoint, not by skipping the group" shape
        // CatalogModule.MapCatalogEndpoints uses for its own public routes.
        var publicPosts = group.MapGroup("/posts");
        publicPosts.MapListPublishedPostsEndpoint();
        publicPosts.MapGetPublishedPostEndpoint();

        var adminGroup = group.MapGroup("/admin").RequireAuthorization(AuthorizationPolicyNames.AdminOnly);

        var banners = adminGroup.MapGroup("/banners");
        banners.MapCreateBannerEndpoint();
        banners.MapUpdateBannerEndpoint();
        banners.MapReorderBannersEndpoint();
        banners.MapDeleteBannerEndpoint();
        banners.MapListBannersEndpoint();

        var menuItems = adminGroup.MapGroup("/menu-items");
        menuItems.MapCreateMenuItemEndpoint();
        menuItems.MapUpdateMenuItemEndpoint();
        menuItems.MapDeleteMenuItemEndpoint();
        menuItems.MapListMenuItemsEndpoint();

        var posts = adminGroup.MapGroup("/posts");
        posts.MapCreatePostEndpoint();
        posts.MapUpdatePostEndpoint();
        posts.MapChangePostStatusEndpoint();
        posts.MapDeletePostEndpoint();
        posts.MapListPostsEndpoint();

        var redirects = adminGroup.MapGroup("/redirects");
        redirects.MapCreateRedirectEndpoint();
        redirects.MapGetRedirectByFromPathEndpoint();
        redirects.MapDeleteRedirectEndpoint();
        redirects.MapListRedirectsEndpoint();

        return endpoints;
    }
}
