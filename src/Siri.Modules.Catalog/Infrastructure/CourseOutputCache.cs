namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>
/// Output-cache policy name + tag for the public course reads (task P1-07: "output cache + invalidate
/// ตอน publish/แก้ราคา"). Centralized here — not a literal string at each call site — so
/// <c>CatalogModule</c>'s policy registration and <c>ApproveCourseHandler</c>'s invalidation call can
/// never silently drift apart, the same reasoning <c>Siri.Modules.Identity.Domain.Role</c>'s own doc
/// comment gives for its id/name constants.
/// <para>
/// One shared tag for every publicly-cached course read (search results and course detail alike) rather
/// than a per-course tag — deliberately coarse: a single course publishing could affect any number of
/// different cached search-result pages (different filters/sorts/pages), and this codebase's catalog
/// scale (docs/DECISIONS.md D-10: "พอสำหรับ catalog &lt; ~10k คอร์ส") doesn't need finer-grained
/// invalidation to stay cheap. In-memory only (the default <c>IOutputCacheStore</c>, no Redis-backed
/// store wired up) — correct for this project's actual deployment topology (docs/DEPLOYMENT.md: single
/// Contabo VPS, one API instance, no horizontal scaling to keep multiple instances' caches in sync
/// across).
/// </para>
/// </summary>
public static class CourseOutputCache
{
    public const string PolicyName = "PublicCourseCatalog";

    public const string Tag = "courses";
}
