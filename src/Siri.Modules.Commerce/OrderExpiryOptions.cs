using System.ComponentModel.DataAnnotations;

namespace Siri.Modules.Commerce;

/// <summary>
/// Configuration for the order expiry background job.
/// </summary>
public sealed class OrderExpiryOptions
{
    public const string SectionName = "Commerce:OrderExpiry";

    /// <summary>
    /// Duration in minutes after which an order in AwaitingPayment status is considered expired. Default 30.
    /// </summary>
    [Range(1, 1440)]
    public int ExpiryMinutes { get; set; } = 30;

    /// <summary>
    /// Maximum batch size of expired orders processed in a single run. Default 50.
    /// </summary>
    [Range(1, 500)]
    public int BatchSize { get; set; } = 50;
}
