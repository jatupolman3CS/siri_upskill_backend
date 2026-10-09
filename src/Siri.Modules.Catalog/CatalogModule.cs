using System.Net.Http.Headers;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Features;
using Siri.Modules.Catalog.Features.ApplyAsInstructor;
using Siri.Modules.Catalog.Features.ApproveCourse;
using Siri.Modules.Catalog.Features.ApproveInstructorApplication;
using Siri.Modules.Catalog.Features.AttachEpisodeMedia;
using Siri.Modules.Catalog.Features.AutosaveCourse;
using Siri.Modules.Catalog.Features.CancelLiveSession;
using Siri.Modules.Catalog.Features.CreateCategory;
using Siri.Modules.Catalog.Features.CreateCourse;
using Siri.Modules.Catalog.Features.CreateCourseEpisode;
using Siri.Modules.Catalog.Features.CreateCourseReview;
using Siri.Modules.Catalog.Features.CreateCourseSection;
using Siri.Modules.Catalog.Features.CreateLiveSession;
using Siri.Modules.Catalog.Features.DeleteCategory;
using Siri.Modules.Catalog.Features.DeleteCourse;
using Siri.Modules.Catalog.Features.DeleteCourseEpisode;
using Siri.Modules.Catalog.Features.DeleteCourseSection;
using Siri.Modules.Catalog.Features.GetAdminCategoryTree;
using Siri.Modules.Catalog.Features.GetCategoryTree;
using Siri.Modules.Catalog.Features.GetCourse;
using Siri.Modules.Catalog.Features.GetCourseBuilder;
using Siri.Modules.Catalog.Features.GetCourseDetail;
using Siri.Modules.Catalog.Features.GetCourseReviews;
using Siri.Modules.Catalog.Features.GetCoursesSitemapPage;
using Siri.Modules.Catalog.Features.GetMyCourses;
using Siri.Modules.Catalog.Features.GetMyInstructorProfile;
using Siri.Modules.Catalog.Features.GetPendingCourseReviews;
using Siri.Modules.Catalog.Features.GetPendingInstructorApplications;
using Siri.Modules.Catalog.Features.GetRobotsTxt;
using Siri.Modules.Catalog.Features.GetSitemapIndex;
using Siri.Modules.Catalog.Features.RejectCourse;
using Siri.Modules.Catalog.Features.RejectInstructorApplication;
using Siri.Modules.Catalog.Features.ReorderCategories;
using Siri.Modules.Catalog.Features.ReorderCourseEpisodes;
using Siri.Modules.Catalog.Features.ReorderCourseSections;
using Siri.Modules.Catalog.Features.SearchCourses;
using Siri.Modules.Catalog.Features.SetCourseDeliveryFormat;
using Siri.Modules.Catalog.Features.SetCourseEnrollmentPolicy;
using Siri.Modules.Catalog.Features.SubmitCourseForReview;
using Siri.Modules.Catalog.Features.UnpublishCourse;
using Siri.Modules.Catalog.Features.UpdateCategory;
using Siri.Modules.Catalog.Features.UpdateCourse;
using Siri.Modules.Catalog.Features.UpdateCourseEpisode;
using Siri.Modules.Catalog.Features.UpdateCourseSection;
using Siri.Modules.Catalog.Features.UpdateLiveSession;
using Siri.Modules.Catalog.Features.Wishlist;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Catalog.Infrastructure.Search;
using Siri.Modules.Catalog.Infrastructure.Seeding;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog;

/// <summary>
/// Composition root for the Catalog module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// P1-01 (CATEGORY tree) is the module's first real feature — see docs/ARCHITECTURE.md §2 for the
/// vertical-slice layout this follows. P1-03 (Instructor profile) adds this module's first real
/// cross-module dependency: <c>Siri.Modules.Identity.Contracts.IInstructorRoleGrantor</c>, resolved by
/// <c>ApproveInstructorApplicationHandler</c> — Identity's own DI registration
/// (<c>IdentityModule.AddIdentityModule</c>) must run somewhere in the same container for that to
/// resolve; <c>Siri.Api/Program.cs</c> already calls both, order does not matter for registration. P1-04
/// (COURSE CRUD draft) adds the instructor-facing course endpoints, gated by
/// <see cref="AuthorizationPolicyNames.InstructorOnly"/> rather than <see cref="AuthorizationPolicyNames.AdminOnly"/>
/// — the first use of that policy name in this module.
/// </summary>
public static class CatalogModule
{
    /// <summary>Registers the Catalog module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services, IConfiguration configuration)
    {
        // P1-09: first Options type this module needs — see SeoOptions' own doc comment for why it has
        // no ValidateOnStart exemption despite the "no real domain yet" gap (same shape
        // EmailConfirmationOptions/PasswordResetOptions already established).
        services.AddOptions<SeoOptions>()
            .Bind(configuration.GetSection(SeoOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<EpisodeAttachmentOptions>()
            .Bind(configuration.GetSection(EpisodeAttachmentOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Singleton: wraps the shared IConnectionMultiplexer (Siri.Persistence.DependencyInjection
        // .AddSharedRedis), same lifetime story as Identity's RedisSessionRegistry — no per-request
        // state of its own.
        services.AddSingleton<CategoryTreeCache>();

        // P1-07: module-owned output-cache policy (Siri.Api/Program.cs only activates the middleware —
        // see that file's own comment). AddOutputCache is safe to call from here even though nothing
        // else in the host calls the parameterless overload first: any call to it (with or without a
        // configure delegate) registers the full set of required services.
        services.AddOutputCache(options =>
            options.AddPolicy(CourseOutputCache.PolicyName, policy => policy.Expire(TimeSpan.FromMinutes(5)).Tag(CourseOutputCache.Tag)));

        // FluentValidation validators, resolved by ValidationEndpointFilter<T> per endpoint.
        // ReorderCategoriesCommand/UpdateCategoryCommand/CreateCategoryCommand are all client-supplied
        // JSON bodies; Delete/GetTree/GetAdminTree take no bindable command, so there is nothing for
        // that filter to run against — same reasoning Identity's device-management handlers give.
        services.AddScoped<IValidator<CreateCategoryCommand>, CreateCategoryValidator>();
        services.AddScoped<IValidator<UpdateCategoryCommand>, UpdateCategoryValidator>();
        services.AddScoped<IValidator<ReorderCategoriesCommand>, ReorderCategoriesValidator>();

        // Scoped: all depend on the scoped AppDbContext.
        services.AddScoped<CreateCategoryHandler>();
        services.AddScoped<UpdateCategoryHandler>();
        services.AddScoped<ReorderCategoriesHandler>();
        services.AddScoped<DeleteCategoryHandler>();
        services.AddScoped<GetCategoryTreeHandler>();
        services.AddScoped<GetAdminCategoryTreeHandler>();

        // P1-03: ApplyAsInstructorCommand is the only one of these five handlers with a client-supplied
        // JSON body — GetMy/GetPending/Approve/Reject take no bindable command (route id or nothing at
        // all), same reasoning CATEGORY's Delete/GetTree/GetAdminTree registrations give above.
        services.AddScoped<IValidator<ApplyAsInstructorCommand>, ApplyAsInstructorValidator>();

        services.AddScoped<ApplyAsInstructorHandler>();
        services.AddScoped<GetMyInstructorProfileHandler>();
        services.AddScoped<GetPendingInstructorApplicationsHandler>();
        services.AddScoped<ApproveInstructorApplicationHandler>();
        services.AddScoped<RejectInstructorApplicationHandler>();

        // P1-04: CreateCourseCommand/UpdateCourseCommand are client-supplied JSON bodies — Delete/
        // GetCourse/GetMyCourses take no bindable command (route id, or query params ASP.NET Core
        // already binds/defaults on its own), same reasoning every registration above gives.
        services.AddScoped<IValidator<CreateCourseCommand>, CreateCourseValidator>();
        services.AddScoped<IValidator<UpdateCourseCommand>, UpdateCourseValidator>();

        services.AddScoped<CreateCourseHandler>();
        services.AddScoped<UpdateCourseHandler>();
        services.AddScoped<DeleteCourseHandler>();
        services.AddScoped<GetCourseHandler>();
        services.AddScoped<GetMyCoursesHandler>();

        // P1-05: RejectCourseCommand is the only one of these four handlers with a client-supplied JSON
        // body — SubmitForReview/GetPending/Approve take no bindable command, same reasoning every
        // registration above gives.
        services.AddScoped<IValidator<RejectCourseCommand>, RejectCourseValidator>();
        services.AddScoped<IValidator<UnpublishCourseCommand>, UnpublishCourseValidator>();

        services.AddScoped<SubmitCourseForReviewHandler>();
        services.AddScoped<GetPendingCourseReviewsHandler>();
        services.AddScoped<ApproveCourseHandler>();
        services.AddScoped<RejectCourseHandler>();
        services.AddScoped<UnpublishCourseHandler>();

        // Course + instructor-name text search on Meilisearch (Meilisearch__Url / __ApiKey / __DocumentsIndexUid). Optional by design: with no URL or no
        // real key ICourseSearchIndex resolves to DisabledCourseSearchIndex and SearchCoursesHandler uses its PostgreSQL pg_trgm path, exactly as before.
        // A malformed URL / index uid fails the boot (ValidateOnStart) so a typo cannot silently turn the feature off.
        services.AddOptions<MeilisearchOptions>()
            .Bind(configuration.GetSection(MeilisearchOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MeilisearchOptions>, MeilisearchOptionsValidator>();
        services.AddSingleton<MeilisearchAvailability>();
        services.AddHttpClient<MeilisearchCourseSearchIndex>((serviceProvider, client) =>
        {
            var meilisearch = serviceProvider.GetRequiredService<IOptions<MeilisearchOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(meilisearch.RequestTimeoutSeconds);

            // Only an active configuration ever builds this client (see the ICourseSearchIndex factory below), but the guard keeps a misuse
            // from throwing on a null address.
            if (meilisearch.GetBaseAddress() is { } baseAddress && meilisearch.HasRealApiKey)
            {
                client.BaseAddress = baseAddress;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", meilisearch.ApiKey.Trim());
            }
        });
        services.AddScoped<ICourseSearchIndex>(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<MeilisearchOptions>>().Value.IsActive
                ? serviceProvider.GetRequiredService<MeilisearchCourseSearchIndex>()
                : new DisabledCourseSearchIndex());
        services.AddScoped<CourseSearchIndexer>();
        services.AddScoped<CourseSearchReindexJob>();
        // Creates the index/settings and fills an empty index once at host start (never blocks or fails the host; a no-op while Meilisearch is off).
        // Registered here rather than in a host's Program.cs so the API and the Workers host behave the same without anyone having to remember it.
        services.AddHostedService<CourseSearchIndexBootstrapper>();

        // P1-06: query params only, no bindable command.
        services.AddScoped<SearchCoursesHandler>();

        // P1-07: route-bound slug only, no bindable command.
        services.AddScoped<GetCourseDetailHandler>();

        // P1-09: no bindable command for any of the three (route-bound page number, or nothing at all).
        services.AddScoped<GetSitemapIndexHandler>();
        services.AddScoped<GetCoursesSitemapPageHandler>();
        services.AddScoped<GetRobotsTxtHandler>();

        // P1-30: opt-in `--seed` CLI action only (Siri.Api/Program.cs), same lifetime/registration shape
        // Identity.Infrastructure.Seeding.IdentitySeeder uses — not part of any normal request pipeline.
        services.AddScoped<CatalogSeeder>();

        // P4-01: COURSE builder (sections, episodes, autosave, optimistic concurrency)
        services.AddScoped<IValidator<CreateCourseSectionCommand>, CreateCourseSectionValidator>();
        services.AddScoped<IValidator<UpdateCourseSectionCommand>, UpdateCourseSectionValidator>();
        services.AddScoped<IValidator<ReorderCourseSectionsCommand>, ReorderCourseSectionsValidator>();
        services.AddScoped<IValidator<CreateCourseEpisodeCommand>, CreateCourseEpisodeValidator>();
        services.AddScoped<IValidator<UpdateCourseEpisodeCommand>, UpdateCourseEpisodeValidator>();
        services.AddScoped<IValidator<ReorderCourseEpisodesCommand>, ReorderCourseEpisodesValidator>();
        services.AddScoped<IValidator<AutosaveCourseCommand>, AutosaveCourseValidator>();
        services.AddScoped<IValidator<AttachEpisodeMediaCommand>, AttachEpisodeMediaValidator>();

        services.AddScoped<GetCourseBuilderHandler>();
        services.AddScoped<CreateCourseSectionHandler>();
        services.AddScoped<UpdateCourseSectionHandler>();
        services.AddScoped<DeleteCourseSectionHandler>();
        services.AddScoped<ReorderCourseSectionsHandler>();
        services.AddScoped<CreateCourseEpisodeHandler>();
        services.AddScoped<UpdateCourseEpisodeHandler>();
        services.AddScoped<DeleteCourseEpisodeHandler>();
        services.AddScoped<ReorderCourseEpisodesHandler>();
        services.AddScoped<AutosaveCourseHandler>();
        services.AddScoped<AttachEpisodeMediaHandler>();

        // Learning Paths & Attachments
        services.AddScoped<IValidator<Features.CreateLearningPath.CreateLearningPathCommand>, Features.CreateLearningPath.CreateLearningPathValidator>();
        services.AddScoped<IValidator<Features.UpdateLearningPath.UpdateLearningPathCommand>, Features.UpdateLearningPath.UpdateLearningPathValidator>();
        services.AddScoped<IValidator<Features.AddEpisodeAttachment.AddEpisodeAttachmentCommand>, Features.AddEpisodeAttachment.AddEpisodeAttachmentValidator>();

        services.AddScoped<Features.CreateLearningPath.CreateLearningPathHandler>();
        services.AddScoped<Features.GetLearningPaths.GetLearningPathsHandler>();
        services.AddScoped<Features.GetLearningPathBySlug.GetLearningPathBySlugHandler>();
        services.AddScoped<Features.UpdateLearningPath.UpdateLearningPathHandler>();
        services.AddScoped<Features.DeleteLearningPath.DeleteLearningPathHandler>();

        services.AddScoped<Features.AddEpisodeAttachment.AddEpisodeAttachmentHandler>();
        services.AddScoped<Features.GetEpisodeAttachments.GetEpisodeAttachmentsHandler>();
        services.AddScoped<Features.DownloadEpisodeAttachment.DownloadEpisodeAttachmentHandler>();
        services.AddScoped<Features.DeleteEpisodeAttachment.DeleteEpisodeAttachmentHandler>();

        // Wishlist
        services.AddScoped<Features.Wishlist.GetWishlistHandler>();
        services.AddScoped<Features.Wishlist.AddToWishlistHandler>();
        services.AddScoped<Features.Wishlist.RemoveFromWishlistHandler>();

        // Virus scanner seam (P4-03). No scanning engine is integrated yet, so this default never claims a
        // file is clean: Attachments:VirusScan:Mode=Required (default) refuses the upload, Disabled is an
        // explicit, logged opt-in (Production refuses to boot with it). Register a real engine over this
        // to replace it.
        services.AddOptions<AttachmentVirusScanOptions>()
            .Bind(configuration.GetSection(AttachmentVirusScanOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<Contracts.IAttachmentVirusScanner, Infrastructure.UnconfiguredAttachmentVirusScanner>();

        // P11-01/P11-03: live-session cross-module seams. ILiveMeetingSink / ILiveMeetingReadinessReader are
        // implemented by Siri.Modules.Live (registered Scoped there with a plain AddScoped, which wins as the
        // last registration); the Null defaults below use TryAdd* so hosts/tests that do not load Live still work
        // and the result does not depend on whether AddCatalogModule or AddLiveModule runs first.
        // ILiveScheduleReader IS implemented here (Catalog owns the schedule data) — see
        // LiveScheduleReader's own doc comment.
        services.TryAddSingleton<Contracts.ILiveMeetingSink, Infrastructure.NullLiveMeetingSink>();
        services.TryAddScoped<Contracts.ILiveMeetingReadinessReader, Infrastructure.NullLiveMeetingReadinessReader>();
        services.AddScoped<Contracts.ILiveScheduleReader, Infrastructure.LiveScheduleReader>();

        // P11-02: Instructor live-session API
        services.AddScoped<IValidator<CreateLiveSessionCommand>, CreateLiveSessionCommandValidator>();
        services.AddScoped<IValidator<UpdateLiveSessionCommand>, UpdateLiveSessionCommandValidator>();
        services.AddScoped<IValidator<CancelLiveSessionCommand>, CancelLiveSessionCommandValidator>();
        services.AddScoped<IValidator<SetCourseDeliveryFormatCommand>, SetCourseDeliveryFormatCommandValidator>();

        services.AddScoped<CreateLiveSessionHandler>();
        services.AddScoped<UpdateLiveSessionHandler>();
        services.AddScoped<CancelLiveSessionHandler>();
        services.AddScoped<SetCourseDeliveryFormatHandler>();
        services.AddScoped<Features.GetCourseLiveSchedule.GetCourseLiveScheduleHandler>(); // P11-05 §4.5

        // P11-11: enrollment deadline + seat cap (Q13.1/Q13.2)
        services.AddScoped<IValidator<SetCourseEnrollmentPolicyCommand>, SetCourseEnrollmentPolicyCommandValidator>();
        services.AddScoped<SetCourseEnrollmentPolicyHandler>();

        // P11-04: per-course opt-in to Google Calendar attendee sync
        services.AddScoped<IValidator<Features.SetCourseLiveSettings.SetCourseLiveSettingsCommand>, Features.SetCourseLiveSettings.SetCourseLiveSettingsCommandValidator>();
        services.AddScoped<Features.SetCourseLiveSettings.SetCourseLiveSettingsHandler>();

        // P11-06: attach a teaching recording to a finished live session (becomes an ordinary lesson = catch-up)
        services.AddScoped<IValidator<Features.AttachSessionRecording.AttachSessionRecordingCommand>, Features.AttachSessionRecording.AttachSessionRecordingCommandValidator>();
        services.AddScoped<Features.AttachSessionRecording.AttachSessionRecordingHandler>();

        // Cross-module contracts
        services.AddScoped<Contracts.ICatalogPriceContract, Infrastructure.Contracts.CatalogPriceContract>();
        services.AddScoped<Contracts.ICourseSummaryReader, Infrastructure.Contracts.CatalogPriceContract>();
        services.AddScoped<Notification.Contracts.ICourseOwnershipVerifier, Infrastructure.Contracts.CatalogPriceContract>();
        // P11-10: instructor dashboard stats + user -> instructor-profile id resolution (money tables key by profile id).
        services.AddScoped<Contracts.IInstructorCourseStatsReader, Infrastructure.Contracts.InstructorCourseStatsReader>();
        services.AddScoped<Contracts.IInstructorProfileReader, Infrastructure.Contracts.InstructorProfileReader>();

        // Reviews (P1-08)
        services.AddScoped<CreateCourseReviewHandler>();
        services.AddScoped<GetCourseReviewsHandler>();
        services.AddScoped<Contracts.ICourseStatsUpdater, Application.CourseStatsUpdater>();

        // COURSES.ENROLLMENT_COUNT: one writer (Learning calls it on enrollment transitions) + the hourly recount job (Siri.Workers registers it).
        // The recount reads the source of truth through Contracts.ILearningEnrollmentCounter, which Siri.Modules.Learning implements.
        services.AddScoped<Contracts.ICourseEnrollmentCountUpdater, Application.CourseEnrollmentCountUpdater>();
        services.AddScoped<Infrastructure.CourseEnrollmentRecountJob>();

        // Repositories
        services.AddScoped<Application.ICourseRepository, Infrastructure.CourseRepository>();
        services.AddScoped<Application.ICategoryRepository, Infrastructure.CategoryRepository>();
        services.AddScoped<Application.ILearningPathRepository, Infrastructure.LearningPathRepository>();
        services.AddScoped<Application.IInstructorProfileRepository, Infrastructure.InstructorProfileRepository>();
        services.AddScoped<Application.IWishlistRepository, Infrastructure.WishlistRepository>();
        services.AddScoped<Application.IEpisodeAttachmentRepository, Infrastructure.EpisodeAttachmentRepository>();
        services.AddScoped<Application.ICourseReviewRepository, Infrastructure.CourseReviewRepository>();

        return services;
    }

    /// <summary>
    /// Maps the Catalog module's minimal API endpoints onto the host's route builder.
    /// <para>
    /// Default-deny at the group level, same shape <c>IdentityModule.MapIdentityEndpoints</c>'s own
    /// doc comment asks every future module to copy: the top-level group's bare
    /// <c>.RequireAuthorization()</c> requires an authenticated caller by default; the public tree read
    /// opts out with <c>.AllowAnonymous()</c>; the admin sub-group tightens further to
    /// <see cref="AuthorizationPolicyNames.AdminOnly"/> (combines with the parent's authenticated-user
    /// requirement — both must pass, which is exactly "authenticated AND Admin/SuperAdmin").
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // P1-09: mapped directly on the root builder, not through the "/api/catalog" group below —
        // search engines expect sitemap.xml/robots.txt at fixed, well-known site-root paths, not nested
        // under an API prefix. Not part of the group, so none of it inherits .RequireAuthorization()
        // either — see each endpoint's own doc comment for why that's correct here, not an oversight.
        endpoints.MapGetSitemapIndexEndpoint();
        endpoints.MapGetCoursesSitemapPageEndpoint();
        endpoints.MapGetRobotsTxtEndpoint();

        var group = endpoints.MapGroup("/api/catalog").WithTags("Catalog").RequireAuthorization();

        group.MapGetCategoryTreeEndpoint();
        group.MapSearchCoursesEndpoint();
        group.MapGetCourseDetailEndpoint();

        var adminGroup = group.MapGroup("/admin/categories").RequireAuthorization(AuthorizationPolicyNames.AdminOnly);
        adminGroup.MapGetAdminCategoryTreeEndpoint();
        adminGroup.MapCreateCategoryEndpoint();
        adminGroup.MapUpdateCategoryEndpoint();
        adminGroup.MapReorderCategoriesEndpoint();
        adminGroup.MapDeleteCategoryEndpoint();

        // P1-03: no .AllowAnonymous() on either — inherit the top-level group's default
        // .RequireAuthorization(), same "managing your own stuff requires being someone" reasoning
        // IdentityModule.MapIdentityEndpoints' device-management endpoints already establish.
        group.MapApplyAsInstructorEndpoint();
        group.MapGetMyInstructorProfileEndpoint();

        var instructorAdminGroup = group.MapGroup("/admin/instructors").RequireAuthorization(AuthorizationPolicyNames.AdminOnly);
        instructorAdminGroup.MapGetPendingInstructorApplicationsEndpoint();
        instructorAdminGroup.MapApproveInstructorApplicationEndpoint();
        instructorAdminGroup.MapRejectInstructorApplicationEndpoint();

        // P1-04: combines with the parent group's authenticated-user requirement, same "both must pass"
        // shape the AdminOnly sub-groups above use — exactly "authenticated AND Instructor/Admin/
        // SuperAdmin". Ownership (which of an instructor's own courses) is each handler's own job, not
        // something a route-level policy can express — see UpdateCourseHandler's doc comment.
        var instructorCourseGroup = group.MapGroup("/instructor/courses").RequireAuthorization(AuthorizationPolicyNames.InstructorOnly);
        instructorCourseGroup.MapCreateCourseEndpoint();
        instructorCourseGroup.MapGetMyCoursesEndpoint();
        instructorCourseGroup.MapGetCourseEndpoint();
        instructorCourseGroup.MapUpdateCourseEndpoint();
        instructorCourseGroup.MapDeleteCourseEndpoint();
        instructorCourseGroup.MapSubmitCourseForReviewEndpoint();

        // P4-01: COURSE builder endpoints (sections, episodes, autosave)
        instructorCourseGroup.MapGetCourseBuilderEndpoint();
        instructorCourseGroup.MapCreateCourseSectionEndpoint();
        instructorCourseGroup.MapUpdateCourseSectionEndpoint();
        instructorCourseGroup.MapDeleteCourseSectionEndpoint();
        instructorCourseGroup.MapReorderCourseSectionsEndpoint();
        instructorCourseGroup.MapCreateCourseEpisodeEndpoint();
        instructorCourseGroup.MapUpdateCourseEpisodeEndpoint();
        instructorCourseGroup.MapDeleteCourseEpisodeEndpoint();
        instructorCourseGroup.MapReorderCourseEpisodesEndpoint();
        instructorCourseGroup.MapAutosaveCourseEndpoint();

        // P11-02: live sessions
        instructorCourseGroup.MapCreateLiveSessionEndpoint();
        instructorCourseGroup.MapUpdateLiveSessionEndpoint();
        instructorCourseGroup.MapCancelLiveSessionEndpoints();
        instructorCourseGroup.MapSetCourseDeliveryFormatEndpoint();

        // P11-11: enrollment deadline + seat cap (Q13.1/Q13.2)
        instructorCourseGroup.MapSetCourseEnrollmentPolicyEndpoint();

        // P1-05: mirrors the /admin/instructors sub-group's shape exactly.
        var courseAdminGroup = group.MapGroup("/admin/courses").RequireAuthorization(AuthorizationPolicyNames.AdminOnly);
        courseAdminGroup.MapGetPendingCourseReviewsEndpoint();
        courseAdminGroup.MapApproveCourseEndpoint();
        courseAdminGroup.MapRejectCourseEndpoint();
        courseAdminGroup.MapUnpublishCourseEndpoint();

        // Learning Paths & Attachments
        group.MapLearningPathEndpoints();
        group.MapEpisodeAttachmentEndpoints();
        group.MapWishlistEndpoints();

        // Reviews (P1-08)
        var coursesGroup = group.MapGroup("/courses");

        coursesGroup.MapGet("/{courseId:guid}/reviews", async (
            Guid courseId,
            [Microsoft.AspNetCore.Mvc.FromQuery] int? page,
            [Microsoft.AspNetCore.Mvc.FromQuery] int? pageSize,
            Features.GetCourseReviews.GetCourseReviewsHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(courseId, page ?? 1, pageSize ?? 10, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
        })
        .WithName("GetCourseReviews")
        .WithSummary("ดึงรายการรีวิวและความคิดเห็นของคอร์สเรียน")
        .AllowAnonymous()
        .Produces<Features.GetCourseReviews.CourseReviewSummaryResponse>(StatusCodes.Status200OK);

        coursesGroup.MapPost("/{courseId:guid}/reviews", async (
            Guid courseId,
            [Microsoft.AspNetCore.Mvc.FromBody] Features.CreateCourseReview.CreateCourseReviewRequest request,
            Features.CreateCourseReview.CreateCourseReviewHandler handler,
            IUserContext userContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!userContext.UserId.HasValue) return Results.Unauthorized();
            var result = await handler.HandleAsync(courseId, userContext.UserId.Value, request, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
        })
        .WithName("CreateCourseReview")
        .WithSummary("เขียนรีวิวและให้คะแนนคอร์สเรียน (เฉพาะผู้ที่ลงทะเบียนเรียน)")
        .RequireAuthorization()
        .Produces<Features.CreateCourseReview.CourseReviewDto>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        return endpoints;
    }
}
