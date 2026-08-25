using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Domain;

/// <summary>
/// A video asset registered with the external video/DRM provider (docs/DATABASE.md's "media" section) —
/// the row a future task's Bunny Stream integration (<c>Siri.Integrations.Video.IVideoProvider</c>) creates
/// when an upload starts and updates as the provider's processing pipeline reports status.
/// <para>
/// <b>UPPERCASE naming exception</b>: this entity's class/property names, and its table/column names, are
/// UPPERCASE rather than this codebase's usual PascalCase — a project-owner-approved exception
/// (docs/DECISIONS.md D-17) scoped to the 7 new Repository+Service modules (Media is one), not a mistake
/// and not a pattern to copy into Identity/Catalog/Notification. <see cref="CreatedAtUtc"/>/
/// <see cref="CreatedBy"/>/<see cref="UpdatedAtUtc"/>/<see cref="UpdatedBy"/> below are the one deliberate
/// carve-out even within this entity: they stay PascalCase because <c>AuditableEntityInterceptor</c> looks
/// them up via <c>nameof(IAuditable.CreatedAtUtc)</c> — a hardcoded C# member-name string, not a column
/// name — so renaming the C# property (not just the mapped column, see <c>MEDIA_ASSETConfiguration</c>)
/// would fail at runtime with <see cref="InvalidOperationException"/>, not at compile time.
/// </para>
/// <para>
/// <see cref="Create"/>/<see cref="MarkProcessing"/>/<see cref="MarkReady"/>/<see cref="MarkFailed"/> are
/// implemented for real as of 2026-08-24 (the old D-17 scaffold note was stale). No <see cref="ISoftDelete"/> — not in
/// DATABASE.md's soft-delete list (Course/Post/Discussion only), and this table isn't financial/
/// entitlement data either (database.md's hard-delete ban is scoped to Orders/Payments/RevenueSplits/
/// Enrollments/Certificates), so a real hard delete (see <c>MediaAssetRepository.Remove</c>) is acceptable.
/// </para>
/// </summary>
public sealed class MEDIA_ASSET : IAuditable
{
    /// <summary>EF Core materialization only.</summary>
    private MEDIA_ASSET()
    {
    }

    public Guid MEDIA_ASSET_ID { get; private set; }

    /// <summary>Which provider this asset lives on (e.g. "BunnyStream") — a constant/config value the
    /// future integration task supplies, never client input — see <c>CreateMediaAssetCommand</c>'s own doc
    /// comment for why the client only ever supplies a title.</summary>
    public string PROVIDER { get; private set; } = string.Empty;

    /// <summary>The provider's own id for this asset (e.g. Bunny Stream's video GUID) — comes back from
    /// <c>IVideoProvider.CreateVideoAsync</c>'s result, never client input.</summary>
    public string PROVIDER_ASSET_ID { get; private set; } = string.Empty;

    /// <summary>Provider-side id used to build playback URLs — <c>null</c> until the provider assigns one
    /// (typically once processing starts, not at upload time).</summary>
    public string? PLAYBACK_ID { get; private set; }

    public MediaAssetStatus STATUS { get; private set; }

    /// <summary>Known only once the provider finishes processing — <c>null</c> before <see cref="MarkReady"/>.</summary>
    public int? DURATION_SECONDS { get; private set; }

    public bool DRM_ENABLED { get; private set; }

    public string? THUMBNAIL_URL { get; private set; }

    /// <summary>FKs to <c>identity.Users</c> conceptually — no database-level FK constraint: cross-module/
    /// cross-schema, same reasoning <c>Siri.Modules.Catalog.Domain.Course.TrailerMediaAssetId</c> and
    /// <c>InstructorProfile.UserId</c> already establish (docs/ARCHITECTURE.md §1 keeps modules as separate
    /// projects/schemas on purpose, so a service can be split out later without an FK to untangle first).</summary>
    public Guid UPLOADED_BY_USER_ID { get; private set; }

    /// <summary>When <see cref="MarkReady"/> ran — <c>null</c> until then.</summary>
    public DateTime? READY_AT_UTC { get; private set; }

    /// <summary>Set by <see cref="MarkFailed"/> — the provider's/pipeline's own failure detail, closer to an
    /// admin/ops diagnostic field than end-user copy (same spirit as <c>Course.RejectionReason</c>); never a
    /// raw exception message (security.md: no stack traces in anything that could reach a client).</summary>
    public string? ERROR_MESSAGE { get; private set; }

    // ---- IAuditable (stays PascalCase — see this class's own doc comment) ------------------------
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

    /// <summary>Registers a new asset in <see cref="MediaAssetStatus.Uploading"/> status.</summary>
    public static MEDIA_ASSET Create(string provider, string providerAssetId, Guid uploadedByUserId, bool drmEnabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerAssetId);

        return new MEDIA_ASSET
        {
            MEDIA_ASSET_ID = UuidV7.NewId(),
            PROVIDER = provider,
            PROVIDER_ASSET_ID = providerAssetId,
            UPLOADED_BY_USER_ID = uploadedByUserId,
            DRM_ENABLED = drmEnabled,
            STATUS = MediaAssetStatus.Uploading,
        };
    }

    /// <summary>Uploading → Processing, once the provider acknowledges the upload finished and started transcoding.</summary>
    public void MarkProcessing()
    {
        if (STATUS != MediaAssetStatus.Uploading)
        {
            throw new InvalidOperationException($"Cannot transition media asset from {STATUS} to Processing.");
        }

        STATUS = MediaAssetStatus.Processing;
    }

    /// <summary>Processing/Uploading → Ready.</summary>
    public void MarkReady(string? playbackId, int durationSeconds, string? thumbnailUrl, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (STATUS is not (MediaAssetStatus.Uploading or MediaAssetStatus.Processing))
        {
            throw new InvalidOperationException($"Cannot transition media asset from {STATUS} to Ready.");
        }

        STATUS = MediaAssetStatus.Ready;
        PLAYBACK_ID = playbackId;
        DURATION_SECONDS = durationSeconds;
        THUMBNAIL_URL = thumbnailUrl;
        READY_AT_UTC = clock.UtcNow;
    }

    /// <summary>Uploading/Processing → Failed.</summary>
    public void MarkFailed(string errorMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);

        if (STATUS is not (MediaAssetStatus.Uploading or MediaAssetStatus.Processing))
        {
            throw new InvalidOperationException($"Cannot transition media asset from {STATUS} to Failed.");
        }

        STATUS = MediaAssetStatus.Failed;
        ERROR_MESSAGE = errorMessage;
    }
}
