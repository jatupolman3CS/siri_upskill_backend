using Microsoft.Extensions.Configuration;
using Siri.Integrations.Email;
using Siri.Modules.Live;
using Siri.Modules.Notification.Infrastructure.Delivery;
using Siri.SharedKernel.Configuration;

namespace Siri.Workers;

/// <summary>
/// What the Workers host must have configured before it may start in Production. Same checks as <c>Siri.Api</c>'s
/// <c>ProductionConfigurationGuard</c> for the settings this host actually uses — each one is a shared helper, so the two hosts cannot drift:
/// <list type="bullet">
/// <item><description><see cref="DataProtectionProductionRequirements"/> — the host decrypts the instructors' stored Google refresh tokens; a dev/placeholder key here
/// cannot read what the API encrypted with the real one.</description></item>
/// <item><description><see cref="EmailProductionRequirements"/> — it drains the email outbox, so mail must not be dropped.</description></item>
/// <item><description><see cref="LiveProductionRequirements"/> — it runs <c>live-meeting-sync</c>, so no fake provider / half-configured Google client.</description></item>
/// <item><description><see cref="NotificationDeliveryProductionRequirements"/> — when the notification pipeline runs on Kafka (<c>Notification:Delivery:Transport=Kafka</c>), the broker address and transport security must be set.</description></item>
/// </list>
/// </summary>
public static class WorkersProductionRequirements
{
    /// <summary>Human-readable problems (never including secret values); empty when the configuration is acceptable for production.</summary>
    public static IReadOnlyList<string> GetProblems(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        return DataProtectionProductionRequirements.GetProblems(configuration)
            .Concat(EmailProductionRequirements.GetProblems(configuration))
            .Concat(LiveProductionRequirements.GetProblems(configuration))
            .Concat(NotificationDeliveryProductionRequirements.GetProblems(configuration))
            .ToList();
    }
}
