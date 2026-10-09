using System.Diagnostics;

namespace Siri.Integrations.Messaging;

/// <summary>Tracing source for Kafka consumption. Hosts opt in with <c>.AddSource(MessagingTelemetry.SourceName)</c>.</summary>
public static class MessagingTelemetry
{
    public const string SourceName = "Siri.Messaging";

    internal static readonly ActivitySource ActivitySource = new(SourceName);
}
