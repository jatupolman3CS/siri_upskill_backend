using System.ComponentModel.DataAnnotations;

namespace Siri.Persistence.DependencyInjection;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Redis") — the shared platform
/// connection (ARCHITECTURE.md §1 diagram: Redis is shared infrastructure, like MSSQL, not owned by
/// any one module).
/// <para>
/// Originally lived in <c>Siri.Modules.Identity</c> (the first, and until P1-01, only consumer — the
/// SE-03 session mirror, <see cref="Siri.SharedKernel.IUserContext"/>-adjacent <c>ISessionRegistry</c>).
/// Relocated here for P1-01 (Catalog's category-tree cache), the anticipated "second real consumer"
/// its own original doc comment called out — hoisting into shared, non-module-owned infrastructure
/// (this project, which already plays that role for <see cref="AppDbContext"/>) is what lets both
/// modules share one <c>IConnectionMultiplexer</c> instead of each opening its own Redis connection.
/// </para>
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>Non-secret in dev (appsettings*.json carries the <c>localhost:6379</c> placeholder,
    /// same convention as <c>ConnectionStrings:Default</c>) — a real production value with
    /// auth/TLS belongs in user-secrets/env per security.md, never committed.</summary>
    [Required]
    public string ConnectionString { get; set; } = string.Empty;
}
