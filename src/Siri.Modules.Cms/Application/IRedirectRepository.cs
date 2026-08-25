using Siri.Modules.Cms.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Persistence port for <see cref="REDIRECT"/> — see <see cref="IBannerRepository"/>'s own doc comment for
/// the general Repository+Service shape this follows (docs/DECISIONS.md D-17); <c>Infrastructure.RedirectRepository</c>
/// is the only implementation.
/// </summary>
public interface IRedirectRepository
{
    Task<REDIRECT?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Lookup by the incoming path — what a future redirect-resolving middleware would call. Not
    /// built in this task (see <see cref="REDIRECT"/>'s own doc comment); the method exists now because the
    /// lookup shape is part of this entity's persistence contract regardless of who calls it first.</summary>
    Task<REDIRECT?> GetByFromPathAsync(string fromPath, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="fromPath"/> is already claimed by another redirect rule.
    /// <paramref name="excludeRedirectId"/> lets an Update check exclude the rule being edited.</summary>
    Task<bool> FromPathExistsAsync(string fromPath, Guid? excludeRedirectId, CancellationToken cancellationToken);

    /// <summary>Every redirect rule, offset-paginated — the admin list
    /// (.claude/rules/database.md: "รายการที่โตได้ต้อง paginate เสมอ ... offset ได้สำหรับ admin table").</summary>
    Task<PagedResult<REDIRECT>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken);

    void Add(REDIRECT redirect);

    void Remove(REDIRECT redirect);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
