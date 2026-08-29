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
        services.AddScoped<IFeatureFlagRepository, FeatureFlagRepository>();

        services.AddScoped<BannerService>();
        services.AddScoped<MenuItemService>();
        services.AddScoped<PostService>();
        services.AddScoped<RedirectService>();
        services.AddScoped<FeatureFlagService>();

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
    /// </summary>
    public static IEndpointRouteBuilder MapCmsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/cms").WithTags("Cms").RequireAuthorization();

        // Feature flags (P6-06)
        endpoints.MapFeatureFlagEndpoints();

        // Public blog read
        var publicPosts = group.MapGroup("/posts");
        publicPosts.MapListPublishedPostsEndpoint();
        publicPosts.MapGetPublishedPostEndpoint();

        var publicBanners = group.MapGroup("/banners");
        publicBanners.MapListActiveBannersEndpoint();

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
