using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>
/// <c>DbSet</c>-style accessors for the Commerce module's entities on the shared <see cref="AppDbContext"/>
/// — same reasoning as <c>Siri.Modules.Catalog.Infrastructure.AppDbContextCatalogExtensions</c>'s own
/// doc comment (<see cref="AppDbContext"/> carries no module-owned <c>DbSet&lt;T&gt;</c> properties to
/// avoid a circular project reference). Every entity gets its own accessor here, including pure
/// composition children (<see cref="ORDER_ITEMS"/>, <see cref="CART_ITEMS"/>, etc.) that are normally
/// reached only through their aggregate root's navigation — same precedent
/// <c>AppDbContextCatalogExtensions.CourseSections</c>/etc. already set, for the same reason: later
/// tasks (read models, admin listings, bulk operations) will often want direct/bulk access without
/// loading a full aggregate graph.
/// </summary>
public static class AppDbContextCommerceExtensions
{
    public static DbSet<ORDER> Orders(this AppDbContext context) => context.Set<ORDER>();

    public static DbSet<ORDER_ITEM> OrderItems(this AppDbContext context) => context.Set<ORDER_ITEM>();

    public static DbSet<CART> Carts(this AppDbContext context) => context.Set<CART>();

    public static DbSet<CART_ITEM> CartItems(this AppDbContext context) => context.Set<CART_ITEM>();

    public static DbSet<PAYMENT> Payments(this AppDbContext context) => context.Set<PAYMENT>();

    public static DbSet<PAYMENT_AMOUNT_OVERRIDE> PaymentAmountOverrides(this AppDbContext context) => context.Set<PAYMENT_AMOUNT_OVERRIDE>();

    public static DbSet<STRIPE_WEBHOOK_EVENT> StripeWebhookEvents(this AppDbContext context) => context.Set<STRIPE_WEBHOOK_EVENT>();

    /// <summary>Table/class name is <c>PAYMENT_OPS_QUEUE</c> (no trailing "S") — see that entity's own
    /// doc comment. The accessor keeps the module's usual "read as a collection" naming even though the
    /// underlying name is already collection-shaped.</summary>
    public static DbSet<PAYMENT_OPS_QUEUE> PaymentOpsQueue(this AppDbContext context) => context.Set<PAYMENT_OPS_QUEUE>();

    public static DbSet<REFUND> Refunds(this AppDbContext context) => context.Set<REFUND>();

    public static DbSet<PROMO_CODE> PromoCodes(this AppDbContext context) => context.Set<PROMO_CODE>();

    public static DbSet<PROMO_REDEMPTION> PromoRedemptions(this AppDbContext context) => context.Set<PROMO_REDEMPTION>();

    public static DbSet<BUNDLE> Bundles(this AppDbContext context) => context.Set<BUNDLE>();

    public static DbSet<BUNDLE_ITEM> BundleItems(this AppDbContext context) => context.Set<BUNDLE_ITEM>();

    public static DbSet<FLASH_SALE> FlashSales(this AppDbContext context) => context.Set<FLASH_SALE>();

    public static DbSet<FLASH_SALE_ITEM> FlashSaleItems(this AppDbContext context) => context.Set<FLASH_SALE_ITEM>();

    public static DbSet<TAX_INVOICE> TaxInvoices(this AppDbContext context) => context.Set<TAX_INVOICE>();
}
