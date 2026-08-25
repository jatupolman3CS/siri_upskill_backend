using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// A course, and the aggregate root for its <see cref="Sections"/> (each owning its own
/// <see cref="CourseSection.Episodes"/>), <see cref="Outcomes"/>, and <see cref="Requirements"/> — see
/// docs/DATABASE.md's "catalog" section. Unlike <see cref="Category"/> (P1-01, deliberately flat, no
/// <c>Children</c> navigation — categories are independent nodes), a <see cref="CourseSection"/>/
/// <see cref="CourseEpisode"/> without a <see cref="Course"/> is meaningless, so this is genuine
/// composition: every child is constructed only through this aggregate's own methods
/// (<see cref="AddSection"/>/<see cref="AddOutcome"/>/<see cref="AddRequirement"/>, and transitively
/// <see cref="CourseSection.AddEpisode"/>), never independently.
/// <para>
/// Two columns reference other tables, each resolved differently — see <c>CourseConfiguration</c>'s own
/// doc comment for the full reasoning: <see cref="InstructorId"/> (→ <c>InstructorProfiles</c>, same
/// module — got its real FK once task P1-03 built that table) and <see cref="TrailerMediaAssetId"/>
/// (→ <c>media.MediaAssets</c>, a different module/schema — no FK constraint, ever, by design).
/// </para>
/// <para>
/// <see cref="EpisodeCount"/>/<see cref="TotalDurationSeconds"/>/<see cref="RatingAverage"/>/
/// <see cref="RatingCount"/>/<see cref="EnrollmentCount"/> are denormalized (DATABASE.md: "ห้าม UPDATE
/// ตรงจาก handler อื่น ให้ผ่าน CourseStatsUpdater ที่เดียว") — no method on this aggregate touches them;
/// that updater does not exist yet (a later task), so they simply stay at their zero defaults through
/// everything P1-02 does.
/// </para>
/// </summary>
public sealed class Course : IAuditable, ISoftDelete
{
    /// <summary>THB is the only currency this platform supports (docs/PAYMENT.md — Stripe PromptPay,
    /// THB-only) — not a per-course choice, so <see cref="Create"/> takes no currency parameter.</summary>
    private const string OnlySupportedCurrency = "THB";

    private readonly List<CourseSection> _sections = [];
    private readonly List<CourseOutcome> _outcomes = [];
    private readonly List<CourseRequirement> _requirements = [];

    /// <summary>EF Core materialization only.</summary>
    private Course()
    {
    }

    public Guid Id { get; private set; }

    public string Slug { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string? Subtitle { get; private set; }

    public string? Description { get; private set; }

    /// <summary>FK to <c>InstructorProfiles.Id</c> — see <c>CourseConfiguration</c> for the relationship
    /// configuration (added by task P1-03).</summary>
    public Guid InstructorId { get; private set; }

    public Guid CategoryId { get; private set; }

    public CourseLevel Level { get; private set; }

    public CourseLanguage Language { get; private set; }

    public string? ThumbnailUrl { get; private set; }

    /// <summary>FKs to <c>media.MediaAssets.Id</c> conceptually — see this class's own doc comment for
    /// why there is no database-level FK constraint, ever.</summary>
    public Guid? TrailerMediaAssetId { get; private set; }

    public decimal Price { get; private set; }

    /// <summary>"Was" price for strikethrough display — optional, no ordering relative to
    /// <see cref="Price"/> is enforced at this layer (a UI/handler-level concern, not a domain
    /// invariant this task takes a position on).</summary>
    public decimal? ComparePrice { get; private set; }

    public string Currency { get; private set; } = OnlySupportedCurrency;

    /// <summary><c>null</c> = lifetime access.</summary>
    public int? AccessDurationDays { get; private set; }

    public CourseStatus Status { get; private set; }

    public DateTime? PublishedAtUtc { get; private set; }

    /// <summary>Why the most recent <see cref="Reject"/> happened — task P1-05's "validation rule +
    /// audit". Cleared by <see cref="SubmitForReview"/>/<see cref="Publish"/> so a fixed-and-resubmitted
    /// course never shows a stale reason from a previous rejection.</summary>
    public string? RejectionReason { get; private set; }

    // ---- Denormalized (CourseStatsUpdater-only — see class doc comment) ------------------------
    public int TotalDurationSeconds { get; private set; }

    public int EpisodeCount { get; private set; }

    public decimal RatingAverage { get; private set; }

    public int RatingCount { get; private set; }

    public int EnrollmentCount { get; private set; }

    public string? SeoTitle { get; private set; }

    public string? SeoDescription { get; private set; }

    /// <summary>EF concurrency token (SQL Server <c>rowversion</c>) — see <c>CourseConfiguration</c>.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<CourseSection> Sections => _sections.AsReadOnly();

    public IReadOnlyCollection<CourseOutcome> Outcomes => _outcomes.AsReadOnly();

    public IReadOnlyCollection<CourseRequirement> Requirements => _requirements.AsReadOnly();

    // ---- ISoftDelete ----------------------------------------------------------------------------
    public bool IsDeleted { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    bool ISoftDelete.IsDeleted
    {
        get => IsDeleted;
        set => IsDeleted = value;
    }

    DateTime? ISoftDelete.DeletedAtUtc
    {
        get => DeletedAtUtc;
        set => DeletedAtUtc = value;
    }

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

    /// <summary>Creates a new draft course. Everything beyond the fields required to identify/place/
    /// price it starts empty/null and is filled in later through the Set*/Update* methods below — this
    /// mirrors how a real course-builder flow works (start minimal, fill in incrementally), and is why
    /// P1-04 (Course CRUD draft) depends on this task rather than needing its own entity design.</summary>
    public static Course Create(string slug, string title, Guid instructorId, Guid categoryId, CourseLevel level, CourseLanguage language, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), price, "price cannot be negative.");
        }

        return new Course
        {
            Id = UuidV7.NewId(),
            Slug = slug,
            Title = title,
            InstructorId = instructorId,
            CategoryId = categoryId,
            Level = level,
            Language = language,
            Price = price,
            Currency = OnlySupportedCurrency,
            Status = CourseStatus.Draft,
        };
    }

    public void UpdateBasicInfo(string title, string? subtitle, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        Title = title;
        Subtitle = subtitle;
        Description = description;
    }

    public void ChangeSlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        Slug = slug;
    }

    public void SetCategory(Guid categoryId)
    {
        CategoryId = categoryId;
    }

    public void SetLevel(CourseLevel level)
    {
        Level = level;
    }

    public void SetLanguage(CourseLanguage language)
    {
        Language = language;
    }

    public void SetThumbnail(string? thumbnailUrl)
    {
        ThumbnailUrl = thumbnailUrl;
    }

    public void SetTrailer(Guid? trailerMediaAssetId)
    {
        TrailerMediaAssetId = trailerMediaAssetId;
    }

    public void SetPricing(decimal price, decimal? comparePrice)
    {
        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), price, "price cannot be negative.");
        }

        if (comparePrice is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(comparePrice), comparePrice, "comparePrice cannot be negative.");
        }

        Price = price;
        ComparePrice = comparePrice;
    }

    public void SetAccessDuration(int? accessDurationDays)
    {
        if (accessDurationDays is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(accessDurationDays), accessDurationDays, "accessDurationDays must be a positive integer, or null for lifetime access.");
        }

        AccessDurationDays = accessDurationDays;
    }

    public void SetSeo(string? seoTitle, string? seoDescription)
    {
        SeoTitle = seoTitle;
        SeoDescription = seoDescription;
    }

    /// <summary>Appends a new section at the end of <see cref="Sections"/> (<see cref="CourseSection.SortOrder"/>
    /// = current count) — same "the aggregate computes its own append position" reasoning as
    /// <see cref="CourseSection.AddEpisode"/>.</summary>
    public CourseSection AddSection(string title)
    {
        var section = CourseSection.Create(Id, title, _sections.Count);
        _sections.Add(section);
        return section;
    }

    /// <summary>Reassigns every section's <see cref="CourseSection.SortOrder"/> to its index in
    /// <paramref name="orderedSectionIds"/> — must name this course's entire current section set (same
    /// full-set requirement as <see cref="CourseSection.ReorderEpisodes"/>).</summary>
    public void ReorderSections(IReadOnlyList<Guid> orderedSectionIds)
    {
        ArgumentNullException.ThrowIfNull(orderedSectionIds);

        if (orderedSectionIds.Count != _sections.Count || !orderedSectionIds.ToHashSet().SetEquals(_sections.Select(s => s.Id)))
        {
            throw new ArgumentException(
                "orderedSectionIds must contain exactly this course's current sections, no more and no less.",
                nameof(orderedSectionIds));
        }

        for (var index = 0; index < orderedSectionIds.Count; index++)
        {
            _sections.Single(s => s.Id == orderedSectionIds[index]).Reorder(index);
        }
    }

    /// <summary>
    /// Removes a section from this course and re-indexes the remaining sections.
    /// Invariant: mutating or deleting sections is prohibited for published or archived courses to preserve enrolled learners' progress.
    /// </summary>
    public void RemoveSection(Guid sectionId)
    {
        if (Status is CourseStatus.Published or CourseStatus.Archived)
        {
            throw new InvalidOperationException($"Cannot remove a section from a course in {Status} status.");
        }

        var section = _sections.FirstOrDefault(s => s.Id == sectionId)
            ?? throw new InvalidOperationException($"Section {sectionId} was not found on this course.");

        _sections.Remove(section);
        for (var index = 0; index < _sections.Count; index++)
        {
            _sections[index].Reorder(index);
        }
    }

    /// <summary>
    /// Removes an episode from its parent section and re-indexes the remaining sibling episodes.
    /// Invariant: mutating or deleting episodes is prohibited for published or archived courses to preserve enrolled learners' progress.
    /// </summary>
    public void RemoveEpisode(Guid episodeId)
    {
        if (Status is CourseStatus.Published or CourseStatus.Archived)
        {
            throw new InvalidOperationException($"Cannot remove an episode from a course in {Status} status.");
        }

        var section = _sections.FirstOrDefault(s => s.Episodes.Any(e => e.Id == episodeId))
            ?? throw new InvalidOperationException($"Episode {episodeId} was not found on this course.");

        section.RemoveEpisode(episodeId);
    }

    public CourseOutcome AddOutcome(string text)
    {
        var outcome = CourseOutcome.Create(Id, text, _outcomes.Count);
        _outcomes.Add(outcome);
        return outcome;
    }

    public void SetOutcomes(IEnumerable<string> outcomes)
    {
        ArgumentNullException.ThrowIfNull(outcomes);

        _outcomes.Clear();
        foreach (var text in outcomes)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                _outcomes.Add(CourseOutcome.Create(Id, text.Trim(), _outcomes.Count));
            }
        }
    }

    public void ReorderOutcomes(IReadOnlyList<Guid> orderedOutcomeIds)
    {
        ArgumentNullException.ThrowIfNull(orderedOutcomeIds);

        if (orderedOutcomeIds.Count != _outcomes.Count || !orderedOutcomeIds.ToHashSet().SetEquals(_outcomes.Select(o => o.Id)))
        {
            throw new ArgumentException(
                "orderedOutcomeIds must contain exactly this course's current outcomes, no more and no less.",
                nameof(orderedOutcomeIds));
        }

        for (var index = 0; index < orderedOutcomeIds.Count; index++)
        {
            _outcomes.Single(o => o.Id == orderedOutcomeIds[index]).Reorder(index);
        }
    }

    public CourseRequirement AddRequirement(string text)
    {
        var requirement = CourseRequirement.Create(Id, text, _requirements.Count);
        _requirements.Add(requirement);
        return requirement;
    }

    public void SetRequirements(IEnumerable<string> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);

        _requirements.Clear();
        foreach (var text in requirements)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                _requirements.Add(CourseRequirement.Create(Id, text.Trim(), _requirements.Count));
            }
        }
    }

    public void ReorderRequirements(IReadOnlyList<Guid> orderedRequirementIds)
    {
        ArgumentNullException.ThrowIfNull(orderedRequirementIds);

        if (orderedRequirementIds.Count != _requirements.Count
            || !orderedRequirementIds.ToHashSet().SetEquals(_requirements.Select(r => r.Id)))
        {
            throw new ArgumentException(
                "orderedRequirementIds must contain exactly this course's current requirements, no more and no less.",
                nameof(orderedRequirementIds));
        }

        for (var index = 0; index < orderedRequirementIds.Count; index++)
        {
            _requirements.Single(r => r.Id == orderedRequirementIds[index]).Reorder(index);
        }
    }

    /// <summary>
    /// Publishes the course. Guards the one invariant task P1-02 exists to enforce (docs/TASKS.md:
    /// "publish ต้องมี ≥1 episode ที่มี media"): at least one episode, anywhere in <see cref="Sections"/>,
    /// must have <see cref="CourseEpisode.MediaAssetId"/> set.
    /// <para>
    /// Deliberately permissive on the *from*-status (allowed from <see cref="CourseStatus.Draft"/>,
    /// <see cref="CourseStatus.InReview"/>, or <see cref="CourseStatus.Rejected"/>) — the actual
    /// Draft→InReview→Published/Rejected admin-approval *process* (task P1-05) is a handler-level
    /// concern layered on top: <c>ApproveCourseHandler</c> only ever calls this after independently
    /// checking <see cref="Status"/> is <see cref="CourseStatus.InReview"/>, so this method staying
    /// permissive doesn't weaken that workflow — it just means this method itself isn't the place the
    /// rule lives (same "domain method guards its own invariant, handler guards the workflow stage" split
    /// <c>UpdateCourseHandler</c>'s Draft-only gate already uses). Only <see cref="CourseStatus.Published"/>
    /// (not idempotent — re-publishing isn't a meaningful no-op) and <see cref="CourseStatus.Archived"/>
    /// (no implicit un-archive) are rejected.
    /// </para>
    /// </summary>
    public void Publish(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is CourseStatus.Published or CourseStatus.Archived)
        {
            throw new InvalidOperationException($"Cannot publish a course in {Status} status.");
        }

        if (!HasEpisodeWithMedia())
        {
            throw new InvalidOperationException("Cannot publish a course with no episode that has media attached.");
        }

        Status = CourseStatus.Published;
        PublishedAtUtc = clock.UtcNow;
        RejectionReason = null;
    }

    /// <summary>
    /// Submits the course for admin review (task P1-05). Only from <see cref="CourseStatus.Draft"/> (the
    /// normal path) or <see cref="CourseStatus.Rejected"/> (fixed and resubmitted) — guards the same
    /// "has media" invariant <see cref="Publish"/> does, for the same reason: there's no point occupying
    /// an admin's review queue with a course that could never actually be published as-is.
    /// </summary>
    public void SubmitForReview()
    {
        if (Status is not (CourseStatus.Draft or CourseStatus.Rejected))
        {
            throw new InvalidOperationException($"Cannot submit a course in {Status} status for review.");
        }

        if (!HasEpisodeWithMedia())
        {
            throw new InvalidOperationException("Cannot submit a course with no episode that has media attached for review.");
        }

        Status = CourseStatus.InReview;
        RejectionReason = null;
    }

    /// <summary>
    /// Rejects a course under review (task P1-05). Only from <see cref="CourseStatus.InReview"/> — the
    /// workflow-stage rule itself (an admin can only reject what was actually submitted) lives at the
    /// handler level too (<c>RejectCourseHandler</c> checks this before calling), same split
    /// <see cref="Publish"/>'s own doc comment describes; this guard is the domain-level backstop.
    /// </summary>
    public void Reject(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status != CourseStatus.InReview)
        {
            throw new InvalidOperationException($"Cannot reject a course in {Status} status.");
        }

        Status = CourseStatus.Rejected;
        RejectionReason = reason;
    }

    private bool HasEpisodeWithMedia() => _sections.SelectMany(s => s.Episodes).Any(e => e.MediaAssetId is not null);
}
