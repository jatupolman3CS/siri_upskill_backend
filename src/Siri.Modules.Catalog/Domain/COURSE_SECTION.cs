using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// One syllabus section inside a <see cref="COURSE"/>. Construction is <c>internal</c>, reachable only
/// through <see cref="COURSE.AddSection"/>. Owns <see cref="Episodes"/> — <see cref="COURSE_EPISODE"/>
/// construction is only reachable through <see cref="AddEpisode"/> here, never directly.
/// <para>
/// No back-navigation to <see cref="COURSE"/> — nothing needs to walk from a section back up to its
/// course, and one-directional keeps the aggregate simpler (same reasoning <see cref="CATEGORY"/>
/// applies to skipping bidirectional navigation, just in the opposite navigation direction).
/// </para>
/// </summary>
public sealed class COURSE_SECTION : IAuditable
{
    private readonly List<COURSE_EPISODE> _episodes = [];

    /// <summary>EF Core materialization only.</summary>
    private COURSE_SECTION()
    {
    }

    public Guid Id { get; private set; }

    public Guid CourseId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    /// <summary>Display order among sibling sections within the same <see cref="CourseId"/>. Owned by
    /// <see cref="COURSE.ReorderSections"/> only.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Mutate only through <see cref="AddEpisode"/>/<see cref="ReorderEpisodes"/> — never expose
    /// the backing list directly.</summary>
    public IReadOnlyCollection<COURSE_EPISODE> Episodes => _episodes.AsReadOnly();

    // ---- IAuditable ---------------------------------------------------------------------------
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    internal static COURSE_SECTION Create(Guid courseId, string title, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new COURSE_SECTION
        {
            Id = UuidV7.NewId(),
            CourseId = courseId,
            Title = title,
            SortOrder = sortOrder,
        };
    }

    public void Rename(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Title = title;
    }

    /// <summary>Called only by <see cref="COURSE.ReorderSections"/>, which already validated the full
    /// sibling set — no independent validation here.</summary>
    internal void Reorder(int sortOrder)
    {
        SortOrder = sortOrder;
    }

    /// <summary>Appends a new episode at the end of this section (<see cref="COURSE_EPISODE.SortOrder"/> =
    /// current <see cref="Episodes"/> count) — the entity computes this itself rather than accepting it
    /// from the caller, unlike <c>CATEGORY.Create</c> (P1-01), which has no <c>Children</c> navigation
    /// and so cannot see its own siblings; this aggregate can.</summary>
    public COURSE_EPISODE AddEpisode(string title, string? description, bool isFreePreview)
    {
        var episode = COURSE_EPISODE.Create(CourseId, Id, title, description, _episodes.Count, isFreePreview);
        _episodes.Add(episode);
        return episode;
    }

    /// <summary>Reassigns every episode's <see cref="COURSE_EPISODE.SortOrder"/> to its index in
    /// <paramref name="orderedEpisodeIds"/> (0..N-1). Must name this section's entire current episode
    /// set — a partial batch would leave the episodes left out holding stale/colliding sort values,
    /// same reasoning P1-01's <c>ReorderCategoriesHandler</c> already established at the handler level;
    /// here it is a domain invariant instead, since this aggregate already holds the full set in memory.</summary>
    public void ReorderEpisodes(IReadOnlyList<Guid> orderedEpisodeIds)
    {
        ArgumentNullException.ThrowIfNull(orderedEpisodeIds);

        if (orderedEpisodeIds.Count != _episodes.Count || !orderedEpisodeIds.ToHashSet().SetEquals(_episodes.Select(e => e.Id)))
        {
            throw new ArgumentException(
                "orderedEpisodeIds must contain exactly this section's current episodes, no more and no less.",
                nameof(orderedEpisodeIds));
        }

        for (var index = 0; index < orderedEpisodeIds.Count; index++)
        {
            _episodes.Single(e => e.Id == orderedEpisodeIds[index]).Reorder(index);
        }
    }

    /// <summary>Removes an episode from this section and re-indexes the remaining episodes to maintain sequential 0..N-1 SortOrder.</summary>
    public void RemoveEpisode(Guid episodeId)
    {
        var episode = _episodes.FirstOrDefault(e => e.Id == episodeId)
            ?? throw new InvalidOperationException($"Episode {episodeId} was not found in this section.");

        _episodes.Remove(episode);
        for (var index = 0; index < _episodes.Count; index++)
        {
            _episodes[index].Reorder(index);
        }
    }
}
