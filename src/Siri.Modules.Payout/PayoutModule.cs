using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Modules.Payout;

/// <summary>
/// Composition root for the Payout module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// </summary>
public static class PayoutModule
{
    /// <summary>Registers the Payout module's services (repositories, application services, validators)
    /// into the container.</summary>
    public static IServiceCollection AddPayoutModule(this IServiceCollection services, IConfiguration? configuration = null)
    {
        services.AddScoped<IRevenueSplitRepository, RevenueSplitRepository>();
        services.AddScoped<IInstructorPayoutAccountRepository, InstructorPayoutAccountRepository>();
        services.AddScoped<IPayoutBatchRepository, PayoutBatchRepository>();
        services.AddScoped<IPayoutBatchItemRepository, PayoutBatchItemRepository>();

        services.AddScoped<RevenueSplitService>();
        services.AddScoped<InstructorPayoutAccountService>();
        services.AddScoped<PayoutBatchService>();
        services.AddScoped<InstructorEarningsService>();
        services.AddScoped<PayoutPolicyService>();

        services.AddScoped<IValidator<CreateRevenueSplitCommand>, CreateRevenueSplitValidator>();
        services.AddScoped<IValidator<CreateInstructorPayoutAccountCommand>, CreateInstructorPayoutAccountValidator>();
        services.AddScoped<IValidator<CreatePayoutBatchCommand>, CreatePayoutBatchValidator>();

        // Cross-module contracts
        services.AddScoped<Contracts.IRevenueSplitContract, Infrastructure.Contracts.RevenueSplitContract>();
        services.AddScoped<Contracts.IInstructorRevenueReader, Infrastructure.Contracts.InstructorRevenueReader>(); // P11-10

        if (configuration is not null)
        {
            services.AddOptions<PayoutOptions>()
                .Bind(configuration.GetSection(PayoutOptions.SectionName))
                .ValidateDataAnnotations()
                .ValidateOnStart();
        }
        else
        {
            services.AddOptions<PayoutOptions>();
        }


        return services;
    }

    /// <summary>
    /// Maps the Payout module's minimal API endpoints onto the host's route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapPayoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/payout").WithTags("Payout").RequireAuthorization();

        var instructorGroup = group.MapGroup("/instructor");
        instructorGroup.MapGroup("/revenue-splits").MapListMyRevenueSplitsEndpoint();
        instructorGroup.MapGroup("/payout-account")
            .MapCreateInstructorPayoutAccountEndpoint()
            .MapGetMyInstructorPayoutAccountEndpoint();

        instructorGroup.MapGetTaxCertificateEndpoint();

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
            .MapVerifyInstructorPayoutAccountEndpoint()
            .MapListInstructorPayoutAccountsEndpoint();

        var adminBatches = adminGroup.MapGroup("/batches");
        adminBatches.MapCreatePayoutBatchEndpoint();
        adminBatches.MapGetPayoutBatchEndpoint();
        adminBatches.MapListPayoutBatchesEndpoint();
        adminBatches.MapExecutePayoutBatchEndpoint();
        adminBatches.MapExportPayoutBatchEndpoint();

        return endpoints;
    }
}
