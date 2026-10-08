using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// Stable <see cref="DomainError"/> codes for receipt / tax-invoice generation. A code ending in
/// <see cref="DomainErrorHttpResults.NotConfiguredCodeSuffix"/> is answered by the API as HTTP 503.
/// </summary>
public static class ReceiptErrors
{
    /// <summary>The seller identity (<see cref="ReceiptSellerOptions"/>) is missing or still a placeholder, so
    /// no receipt can be printed — there is deliberately no invented fallback company.</summary>
    public const string SellerNotConfiguredCode = "receipt.seller_not_configured";

    /// <summary>Generic text on purpose (reaches API clients as the ProblemDetails title) — which setting is
    /// missing is only for operators and is not echoed.</summary>
    public static DomainError SellerNotConfigured() =>
        new(SellerNotConfiguredCode, "Receipt seller details are not configured.");

    /// <summary>The buyer's real name could not be resolved (the account no longer exists), so a receipt
    /// cannot be issued — no placeholder name is printed.</summary>
    public static DomainError BuyerNotFound() =>
        DomainError.NotFound("ไม่พบข้อมูลผู้ซื้อสำหรับคำสั่งซื้อนี้");
}
