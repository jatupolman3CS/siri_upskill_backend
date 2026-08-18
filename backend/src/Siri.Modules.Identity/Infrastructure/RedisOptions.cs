using System.ComponentModel.DataAnnotations;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Redis") — deliberately the
/// already-existing top-level <c>Redis</c> section from the original appsettings.json/
/// appsettings.Development.json scaffold (ARCHITECTURE.md §1 diagram: Redis is shared
/// platform infrastructure, like MSSQL, not owned by any one module), not a nested
/// <c>Identity:Redis</c> section — this task is simply the first real reader of it.
/// <para>
/// Only <see cref="Siri.Modules.Identity"/> binds/uses this today (the SE-03 session mirror,
/// <c>ISessionRegistry</c>) — no other module needs Redis yet (rate-limiter partitioning and the
/// catalog/search caches mentioned in ARCHITECTURE.md §2/§6 are later, separate tasks). If/when a
/// second module needs Redis, it can bind its own <c>IOptions&lt;RedisOptions&gt;</c> off this exact
/// same section rather than duplicating the connection string — hoisting this type into a shared
/// project only becomes worth doing once there is a second real consumer (YAGNI today).
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
