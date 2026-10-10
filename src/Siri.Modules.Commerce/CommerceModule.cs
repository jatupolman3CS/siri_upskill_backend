using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.Integrations.Payment;
using Siri.Integrations.Payment.Stripe;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
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
        services.AddScoped<IPaymentAmountOverrideRepository, PaymentAmountOverrideRepository>();
        services.AddScoped<IPaymentOpsQueueRepository, PaymentOpsQueueRepository>();
        services.AddScoped<IStripeWebhookEventRepository, StripeWebhookEventRepository>();
        services.AddScoped<IRefundRepository, RefundRepository>();
        services.AddScoped<IPromoCodeRepository, PromoCodeRepository>();
        services.AddScoped<IBundleRepository, BundleRepository>();
        services.AddScoped<IFlashSaleRepository, FlashSaleRepository>();
        services.AddScoped<ITaxInvoiceRepository, TaxInvoiceRepository>();

        // P3-01: Stripe Payment integration. Real data only: a missing key stays EMPTY (never a fake
        // "sk_test_placeholder_key"). The host still boots, but StripePaymentMethod refuses every Stripe
        // call and GET /payments/config answers 503 payment.provider_not_configured until real keys are
        // set; ProductionConfigurationGuard makes Production fail fast instead.
        services.AddOptions<StripeOptions>()
            .Bind(configuration.GetSection(StripeOptions.SectionName))
            .PostConfigure(options =>
            {
                // Legacy un-prefixed env var aliases (STRIPE_*). Real values only — no defaults.
                if (!StripeOptions.IsConfigured(options.SecretKey))
                {
                    options.SecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY") ?? options.SecretKey;
                }
                if (!StripeOptions.IsConfigured(options.PublishableKey))
                {
                    options.PublishableKey = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY") ?? options.PublishableKey;
                }
                if (!StripeOptions.IsConfigured(options.WebhookSecret))
                {
                    options.WebhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET") ?? options.WebhookSecret;
                }
            })
            .ValidateOnStart();

        // P11-09: operational gate for which payment methods are currently enabled
        var paymentSection = configuration.GetSection(PaymentOptions.SectionName);
        services.AddOptions<PaymentOptions>()
            .Bind(paymentSection)
            .PostConfigure(options =>
            {
                // Bind() appends configured list items to the [PromptPay] default instead of replacing it
                // (duplicates, and PromptPay could never be switched off) — re-read the raw section so any
                // configured value replaces the default, which only applies when nothing is configured.
                var configured = paymentSection.GetSection(nameof(PaymentOptions.EnabledMethods)).Get<List<PaymentMethod>>();
                options.EnabledMethods = configured is { Count: > 0 } ? configured.Distinct().ToList() : [PaymentMethod.PromptPay];
            })
            .ValidateOnStart();

        // P3-03: Order expiry job options & worker
        services.AddOptions<OrderExpiryOptions>()
            .Bind(configuration.GetSection(OrderExpiryOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Real data only: the receipt/tax-invoice seller identity (legal name, tax id, address) is configuration,
        // never a built-in company. Commerce:Seller:* wins; anything unset falls back to the payout module's payer
        // identity (the same company — see ReceiptSellerOptions). A still-missing value keeps the host booting but
        // the receipt endpoints answer 503 receipt.seller_not_configured; ProductionConfigurationGuard makes
        // Production fail fast instead.
        services.AddOptions<ReceiptSellerOptions>()
            .Configure(options => ReceiptSellerOptions.Apply(options, configuration))
            .ValidateOnStart();

        services.AddScoped<IPaymentMethod, StripePaymentMethod>();
        services.AddScoped<OrderExpiryJob>();
        services.AddScoped<IPricingEngine, PricingEngine>();

        services.AddScoped<CartService>();
        services.AddScoped<OrderService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<PaymentAmountOverrideService>();
        services.AddScoped<StripeWebhookHandler>();
        services.AddScoped<RefundService>();
        services.AddScoped<PromoCodeService>();
        services.AddScoped<BundleService>();
        services.AddScoped<FlashSaleService>();
        services.AddScoped<TaxInvoiceService>();
        services.AddScoped<PaymentOpsQueueService>();

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
        services.AddScoped<IValidator<SetPaymentAmountOverrideCommand>, SetPaymentAmountOverrideValidator>();
        services.AddScoped<IValidator<ResolvePaymentOpsRequest>, ResolvePaymentOpsValidator>();
        services.AddScoped<IValidator<DismissPaymentOpsRequest>, DismissPaymentOpsValidator>();

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
        adminGroup.MapGroup("/payment-ops").MapPaymentOpsEndpoints();

        return endpoints;
    }
}
