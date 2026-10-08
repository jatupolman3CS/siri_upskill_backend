using System.Diagnostics;
using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// A course, and the aggregate root for its <see cref="Sections"/> (each owning its own
/// <see cref="COURSE_SECTION.Episodes"/>), <see cref="Outcomes"/>, and <see cref="Requirements"/> — see
/// docs/DATABASE.md's "catalog" section. Unlike <see cref="CATEGORY"/> (P1-01, deliberately flat, no
/// <c>Children</c> navigation — categories are independent nodes), a <see cref="COURSE_SECTION"/>/
/// <see cref="COURSE_EPISODE"/> without a <see cref="COURSE"/> is meaningless, so this is genuine
/// composition: every child is constructed only through this aggregate's own methods
/// (<see cref="AddSection"/>/<see cref="AddOutcome"/>/<see cref="AddRequirement"/>, and transitively
/// <see cref="COURSE_SECTION.AddEpisode"/>), never independently.
/// <para>
/// Two columns reference other tables, each resolved differently — see <c>CourseConfiguration</c>'s own
/// doc comment for the full reasoning: <see cref="InstructorId"/> (→ <c>InstructorProfiles</c>, same
/// module — got its real FK once task P1-03 built that table) and <see cref="TrailerMediaAssetId"/>
/// (→ <c>media.MediaAssets</c>, a different module/schema — no FK constraint, ever, by design).
/// </para>
/// <para>
/// All five stats columns are denormalized (DATABASE.md: "ห้าม UPDATE ตรงจาก handler อื่น ให้ผ่าน
/// updater ที่เดียว"), but they split into two groups with different owners. <see cref="RatingAverage"/>/
/// <see cref="RatingCount"/>/<see cref="EnrollmentCount"/> depend on data this aggregate does not own
/// (reviews, enrollments in Learning) — no method here writes <see cref="EnrollmentCount"/>: its one writer is
/// <c>Siri.Modules.Catalog.Application.CourseEnrollmentCountUpdater</c> (set-based <c>ExecuteUpdate</c>, called by
/// Learning on enrollment transitions and by the hourly <c>course-enrollment-recount</c> job — see
/// <c>ICourseEnrollmentCountUpdater</c> for what the number means). The rating pair is written by
/// <see cref="UpdateRatingStats"/> through the review handler / <c>CourseStatsUpdater</c>.
/// <see cref="EpisodeCount"/>/<see cref="TotalDurationSeconds"/> are different: everything they depend on
/// (<see cref="Sections"/> and each section's episodes) is already owned by this aggregate, so there is
/// no reason to wait for a cross-module updater — <see cref="RecalculateEpisodeStats"/> is the single
/// place both are written, called automatically by every method on this aggregate that can change the
/// episode set or a episode's duration (<see cref="AddSection"/>/<see cref="RemoveSection"/>/
/// <see cref="AddEpisode(Guid,string,string?,bool)"/>/<see cref="RemoveEpisode"/>/
/// <see cref="AttachEpisodeMedia"/>/<see cref="RemoveEpisodeMedia"/>) — production code never needs to
/// remember to call it itself.
/// </para>
/// </summary>
public sealed class COURSE : IAuditable, ISoftDelete
{
    /// <summary>THB is the only currency this platform supports (docs/PAYMENT.md — Stripe PromptPay,
    /// THB-only) — not a per-course choice, so <see cref="Create"/> takes no currency parameter.</summary>
    private const string OnlySupportedCurrency = "THB";

    private readonly List<COURSE_SECTION> _sections = [];
    private readonly List<COURSE_OUTCOME> _outcomes = [];
    private readonly List<COURSE_REQUIREMENT> _requirements = [];
    private readonly List<COURSE_LIVE_SESSION> _liveSessions = [];

    /// <summary>EF Core materialization only.</summary>
    private COURSE()
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

    /// <summary>How this course is delivered — task P11-01 (docs/HYBRID_LIVE.md §1.1). Defaults to
    /// <see cref="Domain.DeliveryFormat.OnDemand"/>, matching every course created before this task
    /// existed (migration default, no data-migration script needed — see <c>CourseConfiguration</c>).
    /// Changed only through <see cref="SetDeliveryFormat"/>, never a bare setter, so its own invariant
    /// (no switching back to OnDemand while a Scheduled session is still on the books) can never be
    /// bypassed.</summary>
    public DeliveryFormat DeliveryFormat { get; private set; } = DeliveryFormat.OnDemand;

    public DateTime? PublishedAtUtc { get; private set; }

    /// <summary>Why the most recent <see cref="Reject"/> happened — task P1-05's "validation rule +
    /// audit". Cleared by <see cref="SubmitForReview"/>/<see cref="Publish"/> so a fixed-and-resubmitted
    /// course never shows a stale reason from a previous rejection.</summary>
    public string? RejectionReason { get; private set; }

    /// <summary><c>null</c> = enrollment never closes (task P11-11, Q13.1 — docs/contracts/
    /// P11-11-enrollment-deadline-seat-cap.md). Enforced by <c>Siri.Modules.Commerce.Application
    /// .OrderService.CreateAsync</c> via <c>ICatalogPriceContract.GetEnrollmentPoliciesAsync</c>, not by
    /// anything on this aggregate — see <see cref="SetEnrollmentPolicy"/>.</summary>
    public DateTime? EnrollmentDeadlineUtc { get; private set; }

    /// <summary><c>null</c> = unlimited seats (task P11-11, Q13.2). See <see cref="SetEnrollmentPolicy"/>
    /// and <see cref="SeatsUsed"/>.</summary>
    public int? MaxSeats { get; private set; }

    /// <summary>Count of orders that have reserved a seat on this course (task P11-11) — mutated only via
    /// atomic <c>ExecuteUpdateAsync</c> from Commerce (<c>ICatalogPriceContract.TryReserveSeatAsync</c>/
    /// <c>ReleaseSeatAsync</c>), never through this aggregate's own change-tracked properties. Kept as a
    /// property (not a private field) purely so read paths (<c>ICatalogPriceContract
    /// .GetEnrollmentPoliciesAsync</c>) can project it — <see cref="SetEnrollmentPolicy"/> never touches
    /// it.</summary>
    public int SeatsUsed { get; private set; }

    /// <summary>Instructor opt-in (task P11-04, docs/contracts/P11-04-live-invites-ics-reminders.md §2.3/§6):
    /// when <c>true</c>, learners invited to this course's live sessions are also added as attendees of the
    /// instructor's Google Calendar event, so a Meet link forwarded to someone else has to ask to be
    /// admitted. Defaults to <c>false</c> — it sends learners' e-mail addresses to Google, so it must be a
    /// deliberate choice. Changed only through <see cref="SetGoogleAttendeeSync"/>; read by Live through
    /// <c>ILiveScheduleReader</c> (<c>LiveSessionContext.GoogleAttendeeSyncEnabled</c>).</summary>
    public bool GoogleAttendeeSyncEnabled { get; private set; }

    // ---- Denormalized, self-maintained (RecalculateEpisodeStats — see class doc comment) --------
    public int TotalDurationSeconds { get; private set; }

    public int EpisodeCount { get; private set; }

    // ---- Denormalized, updater-only (cross-module — see class doc comment) ------------------------
    public decimal RatingAverage { get; private set; }

    public int RatingCount { get; private set; }

    public int EnrollmentCount { get; private set; }

    public string? SeoTitle { get; private set; }

    public string? SeoDescription { get; private set; }

    /// <summary>EF concurrency token (SQL Server <c>rowversion</c>) — see <c>CourseConfiguration</c>.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<COURSE_SECTION> Sections => _sections.AsReadOnly();

    public IReadOnlyCollection<COURSE_OUTCOME> Outcomes => _outcomes.AsReadOnly();

    public IReadOnlyCollection<COURSE_REQUIREMENT> Requirements => _requirements.AsReadOnly();

    public IReadOnlyCollection<COURSE_LIVE_SESSION> LiveSessions => _liveSessions.AsReadOnly();

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
    /// P1-04 (COURSE CRUD draft) depends on this task rather than needing its own entity design.</summary>
    public static COURSE Create(string slug, string title, Guid instructorId, Guid categoryId, CourseLevel level, CourseLanguage language, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (price < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(price), price, "price cannot be negative.");
        }

        return new COURSE
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

    /// <summary>ตั้งนโยบายปิดรับสมัคร/เพดานที่นั่ง (task P11-11, Q13.1/Q13.2 — docs/contracts/
    /// P11-11-enrollment-deadline-seat-cap.md). ใช้ได้กับคอร์สทุก DeliveryFormat โดยตั้งใจ ไม่ผูกกับ
    /// Live/Hybrid (ดู contract §1.1). ลด maxSeats ให้ต่ำกว่า SeatsUsed ปัจจุบันได้โดยไม่ throw — แค่หยุดขาย
    /// ที่นั่งใหม่ ไม่กระทบคนที่ enroll ไปแล้ว (ตัดสินใจแล้ว ไม่ใช่ช่องโหว่ที่ลืมเช็ค). SeatsUsed เองไม่ถูกแตะโดย
    /// method นี้เลย (แก้ผ่าน ICatalogPriceContract.TryReserveSeatAsync/ReleaseSeatAsync ด้วย raw
    /// ExecuteUpdateAsync จาก Commerce เท่านั้น — ดู contract §2.4).</summary>
    public void SetEnrollmentPolicy(DateTime? enrollmentDeadlineUtc, int? maxSeats)
    {
        if (enrollmentDeadlineUtc is { Kind: not DateTimeKind.Utc })
        {
            throw new ArgumentException(
                "enrollmentDeadlineUtc must be UTC (database.md: Npgsql throws on non-Utc DateTime).",
                nameof(enrollmentDeadlineUtc));
        }

        if (maxSeats is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSeats), maxSeats, "maxSeats must be a positive integer, or null for unlimited seats.");
        }

        EnrollmentDeadlineUtc = enrollmentDeadlineUtc;
        MaxSeats = maxSeats;
    }

    /// <summary>Turns the Google Calendar attendee sync (<see cref="GoogleAttendeeSyncEnabled"/>) on or off.
    /// Rejected for an <see cref="CourseStatus.Archived"/> course (same guard as <see cref="SetDeliveryFormat"/>);
    /// deliberately not tied to <see cref="DeliveryFormat"/> — a course may be switched to Live later and the
    /// flag simply has no effect until it has live sessions.</summary>
    public void SetGoogleAttendeeSync(bool enabled)
    {
        if (Status == CourseStatus.Archived)
        {
            throw new InvalidOperationException($"Cannot change Google attendee sync of a course in {Status} status.");
        }

        GoogleAttendeeSyncEnabled = enabled;
    }

    public void SetSeo(string? seoTitle, string? seoDescription)
    {
        SeoTitle = seoTitle;
        SeoDescription = seoDescription;
    }

    /// <summary>Appends a new section at the end of <see cref="Sections"/> (<see cref="COURSE_SECTION.SortOrder"/>
    /// = current count) — same "the aggregate computes its own append position" reasoning as
    /// <see cref="COURSE_SECTION.AddEpisode"/>.</summary>
    public COURSE_SECTION AddSection(string title)
    {
        var section = COURSE_SECTION.Create(Id, title, _sections.Count);
        _sections.Add(section);
        RecalculateEpisodeStats(); // no-op today (a new section starts with 0 episodes) but keeps this
                                    // method consistent with every other structural mutation below rather
                                    // than being a silent exception to the rule.
        return section;
    }

    /// <summary>Reassigns every section's <see cref="COURSE_SECTION.SortOrder"/> to its index in
    /// <paramref name="orderedSectionIds"/> — must name this course's entire current section set (same
    /// full-set requirement as <see cref="COURSE_SECTION.ReorderEpisodes"/>).</summary>
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

        RecalculateEpisodeStats(); // the removed section takes its episodes with it.
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
        RecalculateEpisodeStats();
    }

    /// <summary>
    /// Adds an episode to the section identified by <paramref name="sectionId"/> and keeps
    /// <see cref="EpisodeCount"/>/<see cref="TotalDurationSeconds"/> in sync — the aggregate-root entry
    /// point production code (handlers, the catalog seeder) should call instead of finding the section
    /// via <see cref="Sections"/> and calling <see cref="COURSE_SECTION.AddEpisode"/> on it directly.
    /// <para>
    /// <see cref="COURSE_SECTION.AddEpisode"/> itself stays public rather than becoming <c>internal</c>:
    /// a large number of existing unit/integration tests build course fixtures by calling it directly on
    /// a section (they don't need a full <see cref="COURSE"/> reference and don't assert on this
    /// aggregate's stats), and <see cref="COURSE_SECTION"/> deliberately has no back-navigation to
    /// <see cref="COURSE"/> (see this class's own doc comment) so it has no way to keep these two
    /// properties in sync even if it wanted to. This method is what closes that gap for the call sites
    /// that do need it kept in sync.
    /// </para>
    /// </summary>
    public COURSE_EPISODE AddEpisode(Guid sectionId, string title, string? description, bool isFreePreview)
    {
        var section = _sections.FirstOrDefault(s => s.Id == sectionId)
            ?? throw new InvalidOperationException($"Section {sectionId} was not found on this course.");

        var episode = section.AddEpisode(title, description, isFreePreview);
        RecalculateEpisodeStats();
        return episode;
    }

    /// <summary>Attaches (or replaces) media on the episode identified by <paramref name="episodeId"/>,
    /// wherever it lives among <see cref="Sections"/>, and keeps <see cref="TotalDurationSeconds"/> in
    /// sync — same "aggregate-root entry point" reasoning as <see cref="AddEpisode(Guid,string,string?,bool)"/>.</summary>
    public COURSE_EPISODE AttachEpisodeMedia(Guid episodeId, Guid mediaAssetId, int durationSeconds)
    {
        var episode = _sections.SelectMany(s => s.Episodes).FirstOrDefault(e => e.Id == episodeId)
            ?? throw new InvalidOperationException($"Episode {episodeId} was not found on this course.");

        episode.AttachMedia(mediaAssetId, durationSeconds);
        RecalculateEpisodeStats();
        return episode;
    }

    /// <summary>Removes media from the episode identified by <paramref name="episodeId"/> and keeps
    /// <see cref="TotalDurationSeconds"/> in sync — same "aggregate-root entry point" reasoning as
    /// <see cref="AddEpisode(Guid,string,string?,bool)"/>.</summary>
    public COURSE_EPISODE RemoveEpisodeMedia(Guid episodeId)
    {
        var episode = _sections.SelectMany(s => s.Episodes).FirstOrDefault(e => e.Id == episodeId)
            ?? throw new InvalidOperationException($"Episode {episodeId} was not found on this course.");

        episode.RemoveMedia();
        RecalculateEpisodeStats();
        return episode;
    }

    /// <summary>เปลี่ยนรูปแบบการส่งมอบคอร์ส. ปฏิเสธถ้าคอร์สเป็น Archived (เหตุผลเดียวกับ RemoveSection/RemoveEpisode's
    /// สถานะที่ห้ามแตะ — แต่ต่างตรงที่ *ไม่* บล็อก Published: ต่างจาก section/episode structure ที่แก้ post-publish
    /// จะทำลาย progress ของผู้เรียน, การเปลี่ยน DeliveryFormat ของคอร์ส Live/Hybrid ที่กำลังสอนอยู่จริง (เพิ่ม session
    /// รายสัปดาห์ต่อเนื่อง) เป็น flow ปกติที่ต้องรองรับ — ไม่ใช่ edge case) ปฏิเสธถ้าจะเปลี่ยนกลับเป็น OnDemand ทั้งที่ยังมี
    /// session Scheduled ค้างอยู่ (ต้อง CancelLiveSession ทุกคาบก่อน — ป้องกัน session ที่ไม่มีความหมายค้างอยู่บนคอร์ส
    /// ที่ประกาศตัวเองว่าไม่มีตารางสอนแล้ว) ไม่ re-validate Publish invariant ย้อนหลัง (ดู <see cref="CanPublishOrSubmit(IClock)"/>'s
    /// หมายเหตุ "ทำไมไม่ re-check ตอนเปลี่ยน format").</summary>
    public void SetDeliveryFormat(DeliveryFormat format)
    {
        if (Status == CourseStatus.Archived)
        {
            throw new InvalidOperationException($"Cannot change delivery format of a course in {Status} status.");
        }

        if (format == DeliveryFormat.OnDemand && _liveSessions.Any(s => s.Status == CourseLiveSessionStatus.Scheduled))
        {
            throw new InvalidOperationException("Cannot switch to OnDemand while scheduled live sessions exist — cancel them first.");
        }

        DeliveryFormat = format;
    }

    /// <summary>เพิ่มคาบสอนสดใหม่. SortOrder คำนวณเองจาก _liveSessions.Count (pattern เดียวกับ AddSection/AddEpisode)
    /// แต่ใช้จริงแค่เป็น tiebreaker — การเรียงลำดับหลักที่ทุก read model ควรใช้คือ StartsAtUtc ไม่ใช่ SortOrder
    /// (ต่างจาก section/episode ที่ SortOrder คือลำดับที่ผู้สอนจงใจจัดเอง ตารางสอนสดเรียงตามเวลาธรรมชาติอยู่แล้ว
    /// ไม่มี endpoint ให้ reorder เอง).</summary>
    public COURSE_LIVE_SESSION AddLiveSession(string title, string? description, DateTime startsAtUtc, DateTime endsAtUtc, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status == CourseStatus.Archived)
        {
            throw new InvalidOperationException($"Cannot add a live session to a course in {Status} status.");
        }

        if (DeliveryFormat == DeliveryFormat.OnDemand)
        {
            throw new InvalidOperationException("Cannot add a live session to an OnDemand course — change DeliveryFormat first.");
        }

        ValidateSessionWindow(startsAtUtc, endsAtUtc, excludingSessionId: null);

        if (startsAtUtc <= clock.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(startsAtUtc), startsAtUtc, "A new live session must start in the future.");
        }

        var session = COURSE_LIVE_SESSION.Create(Id, title, description, startsAtUtc, endsAtUtc, _liveSessions.Count);
        _liveSessions.Add(session);
        return session;
    }

    /// <summary>แก้เวลา/ชื่อ/คำอธิบายของคาบที่มีอยู่ (reschedule). ต่างจาก AddLiveSession ตรงที่ *ไม่* บังคับว่า
    /// startsAtUtc ใหม่ต้องเป็นอนาคต — คาบที่กำลังสอนสดอยู่จริง (StartsAtUtc ผ่านไปแล้วแต่ EndsAtUtc ยังไม่ถึง)
    /// ต้องแก้ EndsAtUtc ให้ยาวขึ้นได้ (เช่น "ต่อเวลาอีก 30 นาที") — ธุรกิจต้องการ flow นี้จริงตาม
    /// docs/HYBRID_LIVE.md §1.2's "Live" display state ที่ต้อง toggle ปุ่ม "เข้าห้อง" ตามเวลาจริง ไม่ใช่ตาม
    /// สมมติฐานว่าคาบที่กำลังสอนอยู่แก้ไม่ได้. Guard เดียวที่มีคือ "คาบที่ EndsAtUtc ผ่านไปแล้วห้ามแก้" (ตาม task
    /// spec ตรง ๆ).</summary>
    public void UpdateLiveSession(Guid sessionId, string title, string? description, DateTime startsAtUtc, DateTime endsAtUtc, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var session = FindLiveSessionOrThrow(sessionId);

        if (session.Status != CourseLiveSessionStatus.Scheduled)
        {
            throw new InvalidOperationException($"Cannot update a live session in {session.Status} status.");
        }

        if (session.EndsAtUtc <= clock.UtcNow)
        {
            throw new InvalidOperationException("Cannot update a live session that has already ended.");
        }

        ValidateSessionWindow(startsAtUtc, endsAtUtc, excludingSessionId: sessionId);

        // A past start stays allowed (a session that is already live may be extended), but the new window
        // must still end in the future — otherwise a Scheduled session could be moved entirely into the
        // past, bypassing AddLiveSession's future-only rule.
        if (endsAtUtc <= clock.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(endsAtUtc), endsAtUtc, "A rescheduled live session must end in the future.");
        }

        session.Reschedule(title, description, startsAtUtc, endsAtUtc);
    }

    /// <summary>ยกเลิกคาบ (soft — Status→Cancelled, ไม่ hard delete แถวทิ้ง; DELETE endpoint ของ P11-02 ก็แม็ปมาที่
    /// method นี้เหมือนกัน เพียงแค่ reason เป็น null — ดู §3's หมายเหตุ "DELETE vs POST .../cancel"). reason ว่าง/
    /// null ได้ (DELETE ไม่บังคับส่ง เหตุผล) — trim แล้วเก็บ null ถ้าว่างเปล่าหลัง trim.</summary>
    public void CancelLiveSession(Guid sessionId, string? reason, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var session = FindLiveSessionOrThrow(sessionId);

        if (session.Status != CourseLiveSessionStatus.Scheduled)
        {
            throw new InvalidOperationException($"Cannot cancel a live session in {session.Status} status.");
        }

        if (session.EndsAtUtc <= clock.UtcNow)
        {
            throw new InvalidOperationException("Cannot cancel a live session that has already ended.");
        }

        session.Cancel(reason);
    }

    /// <summary>ผูกบทเรียนบันทึกเข้ากับคาบ (P11-06 เรียกใช้ — episode ต้องถูกสร้าง/AttachMedia ไปแล้วก่อนโดย
    /// AttachSessionRecordingHandler ผ่าน COURSE.AddEpisode/AttachEpisodeMedia ที่มีอยู่แล้วจาก P1-02, ก่อนเรียก
    /// method นี้). ไม่เช็ค session.Status เลยโดยตั้งใจ — แนบบันทึกได้แม้คาบจะ Cancelled ก็ตาม (สอนสดไปแล้วเปลี่ยนใจ
    /// ยกเลิกคาบที่เหลือ แต่บันทึกของคาบที่สอนไปแล้วยังอัปโหลดได้ปกติ ไม่ใช่ edge case ที่ควรบล็อก) เรียกซ้ำได้
    /// (overwrite RecordingEpisodeId เดิม — ไม่ throw ถ้ามีอยู่แล้ว, กรณีผู้สอนอัปโหลดผิดไฟล์แล้วอัปใหม่).</summary>
    public void AttachSessionRecording(Guid sessionId, Guid episodeId)
    {
        var session = FindLiveSessionOrThrow(sessionId);

        if (!_sections.SelectMany(s => s.Episodes).Any(e => e.Id == episodeId))
        {
            throw new InvalidOperationException($"Episode {episodeId} does not belong to this course.");
        }

        session.AttachRecording(episodeId);
    }

    private COURSE_LIVE_SESSION FindLiveSessionOrThrow(Guid sessionId) =>
        _liveSessions.FirstOrDefault(s => s.Id == sessionId)
            ?? throw new InvalidOperationException($"Live session {sessionId} was not found on this course.");

    /// <summary>ตรวจ duration (15 นาที–8 ชม.) + ไม่ทับกับคาบ Scheduled อื่นในคอร์สเดียวกัน (Cancelled ไม่นับ —
    /// เวลาที่คาบถูกยกเลิกไปแล้วว่างให้จองซ้ำได้ปกติ) excludingSessionId กันตัวเองชนตัวเองตอน UpdateLiveSession.
    /// Overlap = half-open interval เทียบกันตรงไปตรงมา (a.Start &lt; b.End &amp;&amp; b.Start &lt; a.End) — คาบที่
    /// เวลาต่อกันพอดี (EndsAtUtc ของคาบหนึ่ง == StartsAtUtc ของอีกคาบ) ไม่ถือว่าทับกัน.</summary>
    private void ValidateSessionWindow(DateTime startsAtUtc, DateTime endsAtUtc, Guid? excludingSessionId)
    {
        if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("startsAtUtc and endsAtUtc must be UTC (database.md: Npgsql throws on non-Utc DateTime).");
        }

        if (endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentException("endsAtUtc must be after startsAtUtc.");
        }

        var duration = endsAtUtc - startsAtUtc;
        if (duration < TimeSpan.FromMinutes(15) || duration > TimeSpan.FromHours(8))
        {
            throw new ArgumentException("Live session duration must be between 15 minutes and 8 hours.");
        }

        var overlaps = _liveSessions.Any(s =>
            s.Id != excludingSessionId &&
            s.Status == CourseLiveSessionStatus.Scheduled &&
            s.StartsAtUtc < endsAtUtc && startsAtUtc < s.EndsAtUtc);

        if (overlaps)
        {
            throw new InvalidOperationException("This time overlaps with another scheduled live session in this course.");
        }
    }

    public COURSE_OUTCOME AddOutcome(string text)
    {
        var outcome = COURSE_OUTCOME.Create(Id, text, _outcomes.Count);
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
                _outcomes.Add(COURSE_OUTCOME.Create(Id, text.Trim(), _outcomes.Count));
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

    public COURSE_REQUIREMENT AddRequirement(string text)
    {
        var requirement = COURSE_REQUIREMENT.Create(Id, text, _requirements.Count);
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
                _requirements.Add(COURSE_REQUIREMENT.Create(Id, text.Trim(), _requirements.Count));
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
    /// must have <see cref="COURSE_EPISODE.MediaAssetId"/> set.
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

        if (!CanPublishOrSubmit(clock))
        {
            throw new InvalidOperationException(PublishInvariantMessage());
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
    public void SubmitForReview(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (Status is not (CourseStatus.Draft or CourseStatus.Rejected))
        {
            throw new InvalidOperationException($"Cannot submit a course in {Status} status for review.");
        }

        if (!CanPublishOrSubmit(clock))
        {
            throw new InvalidOperationException(PublishInvariantMessage());
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

    public void Unpublish(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status != CourseStatus.Published)
        {
            throw new InvalidOperationException($"Cannot unpublish a course in {Status} status.");
        }

        Status = CourseStatus.Draft;
        RejectionReason = reason;
    }

    public void Archive()
    {
        Status = CourseStatus.Archived;
    }

    public void UpdateRatingStats(decimal ratingAverage, int ratingCount)
    {
        RatingAverage = ratingAverage;
        RatingCount = Math.Max(0, ratingCount);
    }

    /// <summary>docs/HYBRID_LIVE.md §1.1's ตาราง — OnDemand ต้องมี episode ที่มี media (เดิม, ไม่เปลี่ยน) ·
    /// Live/Hybrid ต้องมี "session อนาคตหรือ episode มี media" (อย่างใดอย่างหนึ่งพอ — Hybrid ที่ยังไม่ตั้งตาราง
    /// สอนสดแต่มี VOD ครบแล้วก็ publish ได้ปกติ, Live ล้วนที่ยังไม่มี episode เลยแต่ตั้งตารางสอนไว้แล้วก็ publish ได้
    /// เหมือนกัน — "อย่างใดอย่างหนึ่ง" ไม่ใช่ "ทั้งสอง"). แทนที่ HasEpisodeWithMedia() ตัวเดิมที่ยังอยู่เป็น private
    /// helper ตัวหนึ่งในสอง ไม่ใช่ถูกลบทิ้ง.</summary>
    private bool CanPublishOrSubmit(IClock clock) => DeliveryFormat switch
    {
        DeliveryFormat.OnDemand => HasEpisodeWithMedia(),
        DeliveryFormat.Live or DeliveryFormat.Hybrid => HasFutureScheduledLiveSession(clock) || HasEpisodeWithMedia(),
        _ => throw new UnreachableException($"Unknown {nameof(DeliveryFormat)} value: {DeliveryFormat}."),
    };

    private bool HasEpisodeWithMedia() => _sections.SelectMany(s => s.Episodes).Any(e => e.MediaAssetId is not null);

    private bool HasFutureScheduledLiveSession(IClock clock) =>
        _liveSessions.Any(s => s.Status == CourseLiveSessionStatus.Scheduled && s.StartsAtUtc > clock.UtcNow);

    private string PublishInvariantMessage() => DeliveryFormat == DeliveryFormat.OnDemand
        ? "Cannot publish a course with no episode that has media attached."
        : "Cannot publish a Live/Hybrid course with no future scheduled session and no episode that has media attached.";

    /// <summary>
    /// The single place <see cref="EpisodeCount"/>/<see cref="TotalDurationSeconds"/> are ever assigned —
    /// database.md's "denormalized values updated from exactly one place" rule, scoped to the half of
    /// this aggregate's stats that don't need cross-module data (see class doc comment). Recomputes both
    /// from <see cref="Sections"/> as currently held in memory; an episode with no media yet contributes
    /// 0 to <see cref="TotalDurationSeconds"/> (its <see cref="COURSE_EPISODE.DurationSeconds"/> is
    /// <c>null</c> until <see cref="COURSE_EPISODE.AttachMedia"/>), not a missing/undefined value.
    /// <para>
    /// <c>internal</c>, not <c>private</c>: every structural mutation on this aggregate already calls it
    /// automatically, so production code never has a reason to call it directly — but it is also the
    /// designated one-time backfill/self-heal hook for rows written by a build of <c>CatalogSeeder</c>
    /// older than this method (which built courses by calling <see cref="COURSE_SECTION.AddEpisode"/>/
    /// <see cref="COURSE_EPISODE.AttachMedia"/> directly, bypassing this aggregate's stats entirely — see
    /// <c>CatalogSeeder.BackfillEpisodeStatsAsync</c>, same assembly). Not <c>public</c>: nothing outside
    /// this module should ever be able to force a recompute.
    /// </para>
    /// </summary>
    internal void RecalculateEpisodeStats()
    {
        var episodes = _sections.SelectMany(s => s.Episodes).ToList();
        EpisodeCount = episodes.Count;
        TotalDurationSeconds = episodes.Sum(e => e.DurationSeconds ?? 0);
    }
}
