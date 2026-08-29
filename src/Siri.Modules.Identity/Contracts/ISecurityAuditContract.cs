namespace Siri.Modules.Identity.Contracts;

/// <summary>
/// Cross-module contract allowing other modules to record security audits cleanly
/// without referencing Identity's internal Domain or Infrastructure namespaces.
/// </summary>
public interface ISecurityAuditContract
{
    Task RecordAuditAsync(
        string eventType,
        Guid? userId,
        string? detail,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
