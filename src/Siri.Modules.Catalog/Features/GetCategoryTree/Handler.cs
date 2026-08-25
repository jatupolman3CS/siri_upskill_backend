using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Features.GetCategoryTree;

/// <summary>
/// The public, active-only category tree — Redis-cached (<see cref="CategoryTreeCache"/>). Returns
/// already-serialized JSON rather than a typed DTO: on a cache hit there is nothing to deserialize
/// into an object graph just to re-serialize it back out, and on a miss, the freshly-built JSON is
/// exactly what gets cached, so caller and cache always agree on the wire format. Uses the app's own
/// configured <see cref="JsonOptions"/> (camelCase by default) rather than a fresh
/// <see cref="JsonSerializerOptions"/>, so a cache-hit response and a cache-miss response are never
/// differently cased.
/// <para>
/// No expected-failure branch (any caller can always read the tree), so this returns a plain
/// <see cref="string"/>, not wrapped in <c>Result&lt;T&gt;</c> — same reasoning
/// <c>Identity.Features.ListSessions.ListSessionsHandler</c>'s own doc comment gives for skipping the
/// wrapper.
/// </para>
/// </summary>
public sealed class GetCategoryTreeHandler(AppDbContext dbContext, CategoryTreeCache cache, IOptions<JsonOptions> jsonOptions)
{
    public async Task<string> HandleAsync(CancellationToken cancellationToken)
    {
        var cached = await cache.GetAsync(cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        var categories = await dbContext.Categories()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tree = CategoryTreeAssembler.Assemble(categories, includeInactive: false);
        var json = JsonSerializer.Serialize(tree, jsonOptions.Value.SerializerOptions);

        await cache.SetAsync(json, cancellationToken).ConfigureAwait(false);

        return json;
    }
}
