using Siri.SharedKernel;

namespace Siri.Integrations.Storage;

/// <summary>
/// Stable <see cref="DomainError"/>s the storage integration returns. A code ending in
/// <see cref="DomainErrorHttpResults.NotConfiguredCodeSuffix"/> is answered by the API as HTTP 503.
/// </summary>
public static class StorageErrors
{
    /// <summary>R2 credentials / bucket are missing or still placeholders, so no call can be made. There
    /// is deliberately no local-disk or in-memory fallback: a file that silently landed somewhere else
    /// would be unreachable (or worse, reachable) once the real store is configured.</summary>
    public const string ProviderNotConfiguredCode = "storage.provider_not_configured";

    /// <summary>Generic text on purpose: this reaches API clients as the ProblemDetails title, so which
    /// settings are missing is only logged server-side (never echoed to callers).</summary>
    public static DomainError ProviderNotConfigured() =>
        new(ProviderNotConfiguredCode, "File storage is not configured.");

    /// <summary>The store was reachable in principle but the call failed (network, 5xx, rejected
    /// credentials). Answered as HTTP 503 via <see cref="DomainError.Unavailable"/>; the provider's own
    /// message is logged, never returned.</summary>
    public static DomainError OperationFailed() =>
        DomainError.Unavailable("File storage is temporarily unavailable.");

    public static DomainError InvalidKey() =>
        DomainError.Validation("Storage key is not valid.");
}
