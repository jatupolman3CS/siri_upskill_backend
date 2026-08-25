using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Modules.Payout;

/// <summary>
/// Composition root for the Payout module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// <para>
/// This module (docs/DECISIONS.md D-17) uses the Repository+Service pattern and UPPERCASE entity naming,
/// unlike Identity/Catalog/Notification's vertical-slice/PascalCase — see <c>.claude/rules/backend.md</c>.
/// Scaffold pass: every <c>Application/*Service.cs</c> method is stubbed, so the module compiles, migrates,
/// and routes correctly, but calling any endpoint below throws at runtime until a later task fills in the
/// real logic (revenue-split formula per docs/DECISIONS.md Q4's default, batch execution, real account
/// encryption — none of that is guessed at here, see each entity/service's own doc comment).
/// </para>
/// <para>
/// <see cref="PAYOUT_BATCH_ITEM"/> (its repository is registered below for when a later task's
/// <c>PayoutBatchService</c> implementation needs it) has no <c>Service</c>/<c>Endpoints</c> of its own —
/// it is a pure composition child of <c>PAYOUT_BATCH</c>, same reasoning <c>ORDER_ITEM</c> follows in
/// Commerce.
/// </para>
/// </summary>
public static class PayoutModule
{
    /// <summary>Registers the Payout module's services (repositories, application services, validators)
    /// into the container.</summary>
    public static IServiceCollection AddPayoutModule(this IServiceCollection services)
    {
        services.AddScoped<IRevenueSplitRepository, RevenueSplitRepository>();
        services.AddScoped<IInstructorPayoutAccountRepository, InstructorPayoutAccountRepository>();
        services.AddScoped<IPayoutBatchRepository, PayoutBatchRepository>();
        services.AddScoped<IPayoutBatchItemRepository, PayoutBatchItemRepository>();

        services.AddScoped<RevenueSplitService>();
        services.AddScoped<InstructorPayoutAccountService>();
        services.AddScoped<PayoutBatchService>();
        services.AddScoped<InstructorEarningsService>();

        services.AddScoped<IValidator<CreateRevenueSplitCommand>, CreateRevenueSplitValidator>();
        services.AddScoped<IValidator<CreateInstructorPayoutAccountCommand>, CreateInstructorPayoutAccountValidator>();
        services.AddScoped<IValidator<CreatePayoutBatchCommand>, CreatePayoutBatchValidator>();

        // Cross-module contracts
        services.AddScoped<Contracts.IRevenueSplitContract, Infrastructure.Contracts.RevenueSplitContract>();

        return services;
    }

    /// <summary>
    /// Maps the Payout module's minimal API endpoints onto the host's route builder.
    /// <para>
    /// Default-deny at the top-level group (bare <c>.RequireAuthorization()</c>). Two sub-groups: an
    /// <c>/instructor</c> prefix (bare authenticated — each handler resolves the caller's own
    /// <c>IUserContext.UserId</c>, never a client-supplied id, for anything scoped to "my own") and an
    /// <c>/admin</c> prefix (<see cref="AuthorizationPolicyNames.AdminOnly"/> — payout is money-movement
    /// data, same "AdminOnly by default unless explicitly the caller's own resource" split Catalog's
    /// instructor-application review endpoints already use).
    /// </para>
    /// </summary>
    public static IEndpointRouteBuilder MapPayoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/payout").WithTags("Payout").RequireAuthorization();

        var instructorGroup = group.MapGroup("/instructor");
        instructorGroup.MapGroup("/revenue-splits").MapListMyRevenueSplitsEndpoint();
        instructorGroup.MapGroup("/payout-account")
            .MapCreateInstructorPayoutAccountEndpoint()
            .MapGetMyInstructorPayoutAccountEndpoint();

        instructorGroup.MapGet("/earnings/summary", async (
            InstructorEarningsService service,
            IUserContext userContext,
            CancellationToken cancellationToken) =>
        {
            if (userContext.UserId is not { } userId)
            {
                return Results.Unauthorized();
            }

            var result = await service.GetEarningsSummaryAsync(userId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        })
        .WithName("PayoutGetMyEarningsSummary")
        .WithSummary("ดูสรุปรายได้สะสม/รอบล่าสุด/ประมาณการรอบถัดไปของผู้สอน")
        .Produces<InstructorEarningsSummaryResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        instructorGroup.MapGet("/history", async (
            InstructorEarningsService service,
            IUserContext userContext,
            CancellationToken cancellationToken,
            int page = 1,
            int pageSize = 20) =>
        {
            if (userContext.UserId is not { } userId)
            {
                return Results.Unauthorized();
            }

            var result = await service.GetPayoutHistoryAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        })
        .WithName("PayoutGetMyPayoutHistory")
        .WithSummary("ดูประวัติการรับเงินโอนของผู้สอน")
        .Produces<PagedResult<InstructorPayoutHistoryItem>>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        var adminGroup = group.MapGroup("/admin").RequireAuthorization(AuthorizationPolicyNames.AdminOnly);

        var adminRevenueSplits = adminGroup.MapGroup("/revenue-splits");
        adminRevenueSplits.MapCreateRevenueSplitEndpoint();
        adminRevenueSplits.MapGetRevenueSplitEndpoint();
        adminRevenueSplits.MapListRevenueSplitsEndpoint();

        adminGroup.MapGroup("/payout-accounts")
            .MapGetInstructorPayoutAccountByInstructorIdEndpoint()
            .MapListInstructorPayoutAccountsEndpoint();

        var adminBatches = adminGroup.MapGroup("/batches");
        adminBatches.MapCreatePayoutBatchEndpoint();
        adminBatches.MapGetPayoutBatchEndpoint();
        adminBatches.MapListPayoutBatchesEndpoint();

        return endpoints;
    }
}
