using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.Integrations.Payment;
using Siri.Integrations.Payment.Stripe;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce;

/// <summary>
/// Composition root for the Commerce module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// </summary>
public static class CommerceModule
{
    /// <summary>Registers the Commerce module's services (repositories, application services, validators)
    /// into the container.</summary>
    public static IServiceCollection AddCommerceModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ICartRepository, CartRepository>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IPaymentOpsQueueRepository, PaymentOpsQueueRepository>();
        services.AddScoped<IStripeWebhookEventRepository, StripeWebhookEventRepository>();
        services.AddScoped<IRefundRepository, RefundRepository>();
        services.AddScoped<IPromoCodeRepository, PromoCodeRepository>();
        services.AddScoped<IBundleRepository, BundleRepository>();
        services.AddScoped<IFlashSaleRepository, FlashSaleRepository>();
        services.AddScoped<ITaxInvoiceRepository, TaxInvoiceRepository>();

        // P3-01: Stripe Payment integration
        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<IPaymentMethod, StripePaymentMethod>();

        services.AddScoped<CartService>();
        services.AddScoped<OrderService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<StripeWebhookHandler>();
        services.AddScoped<RefundService>();
        services.AddScoped<PromoCodeService>();
        services.AddScoped<BundleService>();
        services.AddScoped<FlashSaleService>();
        services.AddScoped<TaxInvoiceService>();

        services.AddScoped<IValidator<AddCartItemCommand>, AddCartItemValidator>();
        services.AddScoped<IValidator<CreateOrderCommand>, CreateOrderValidator>();
        services.AddScoped<IValidator<CreatePaymentCommand>, CreatePaymentValidator>();
        services.AddScoped<IValidator<RequestRefundCommand>, RequestRefundValidator>();
        services.AddScoped<IValidator<ApproveRefundCommand>, ApproveRefundValidator>();
        services.AddScoped<IValidator<RejectRefundCommand>, RejectRefundValidator>();
        services.AddScoped<IValidator<CreatePromoCodeCommand>, CreatePromoCodeValidator>();
        services.AddScoped<IValidator<ValidatePromoCodeCommand>, ValidatePromoCodeValidator>();
        services.AddScoped<IValidator<CreateBundleCommand>, CreateBundleValidator>();
        services.AddScoped<IValidator<CreateFlashSaleCommand>, CreateFlashSaleValidator>();
        services.AddScoped<IValidator<IssueTaxInvoiceCommand>, IssueTaxInvoiceValidator>();

        // Cross-module contracts
        services.AddScoped<Contracts.ICommerceStatsContract, Infrastructure.Contracts.CommerceStatsContract>();

        return services;
    }

    /// <summary>
    /// Maps the Commerce module's minimal API endpoints onto the host's route builder.
    /// </summary>
    public static IEndpointRouteBuilder MapCommerceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/commerce").WithTags("Commerce").RequireAuthorization();

        group.MapGroup("/cart").MapCartEndpoints();
        group.MapGroup("/orders").MapOrderEndpoints();
        group.MapGroup("/payments").MapPaymentEndpoints();
        group.MapGroup("/promo-codes").MapCustomerPromoCodeEndpoints();
        group.MapGroup("/webhooks").MapStripeWebhookEndpoints();
        group.MapGroup("/refunds").MapRefundEndpoints();
        group.MapGroup("/tax-invoices").MapTaxInvoiceEndpoints();
        group.MapGroup("/bundles").MapBundleEndpoints();
        group.MapGroup("/flash-sales").MapFlashSaleEndpoints();

        var adminGroup = group.MapGroup("/admin").RequireAuthorization(AuthorizationPolicyNames.AdminOnly);

        adminGroup.MapGroup("/refunds").MapAdminRefundEndpoints();
        adminGroup.MapGroup("/promo-codes").MapPromoCodeEndpoints();
        adminGroup.MapGroup("/bundles").MapAdminBundleEndpoints();
        adminGroup.MapGroup("/flash-sales").MapAdminFlashSaleEndpoints();
        adminGroup.MapGroup("/tax-invoices").MapAdminTaxInvoiceEndpoints();

        return endpoints;
    }
}
