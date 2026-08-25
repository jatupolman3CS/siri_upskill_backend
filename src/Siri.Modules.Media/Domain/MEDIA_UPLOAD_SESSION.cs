using Siri.Persistence.Conventions;

namespace Siri.Modules.Media.Domain;

/// <summary>
/// A single upload attempt against <see cref="MEDIA_ASSET_ID"/> — the signed/temporary URL a client uploads
/// bytes to, with its own short lifecycle independent of the parent asset's <see cref="MediaAssetStatus"/>
/// (docs/DATABASE.md's "media" section: "MediaUploadSessions(Id PK, MediaAssetId FK, UploadUrl,
/// ExpiresAtUtc, Status)"). Pure child of its <see cref="MEDIA_ASSET"/> — see
/// <c>MEDIA_UPLOAD_SESSIONConfiguration</c> for why its FK is <c>Cascade</c>, unlike most cross-entity FKs
/// in this codebase (matches the "real ownership edge" reasoning
/// <c>Siri.Modules.Catalog.Infrastructure.CourseSectionConfiguration</c> already gives for
/// <c>Course</c>→<c>CourseSection</c>).
/// <para>See <see cref="MEDIA_ASSET"/>'s own doc comment for the UPPERCASE naming exception (D-17) and why
/// <see cref="CreatedAtUtc"/>/<see cref="CreatedBy"/>/<see cref="UpdatedAtUtc"/>/<see cref="UpdatedBy"/>
/// below stay PascalCase regardless.</para>
/// <para>Scaffold pass (D-17): <see cref="Create"/>/<see cref="Complete"/>/<see cref="MarkExpired"/> are
/// stubbed — see <see cref="MEDIA_ASSET"/>'s doc comment for why. <see cref="MarkExpired"/> has no
/// Service/Endpoint caller in this pass either (a future expiry-sweep job's job, not a client action) —
/// see <c>MediaModule</c>'s own doc comment.</para>
/// </summary>
public sealed class MEDIA_UPLOAD_SESSION : IAuditable
{
    /// <summary>EF Core materialization only.</summary>
    private MEDIA_UPLOAD_SESSION()
    {
    }

    public Guid MEDIA_UPLOAD_SESSION_ID { get; private set; }

    public Guid MEDIA_ASSET_ID { get; private set; }

    public string UPLOAD_URL { get; private set; } = string.Empty;

    public DateTime EXPIRES_AT_UTC { get; private set; }

    public MediaUploadSessionStatus STATUS { get; private set; }

    // ---- IAuditable (stays PascalCase — see MEDIA_ASSET's own doc comment) -------------------------
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

    /// <summary>Starts a new upload session for <paramref name="mediaAssetId"/>, in
    /// <see cref="MediaUploadSessionStatus.Pending"/> status.</summary>
    public static MEDIA_UPLOAD_SESSION Create(Guid mediaAssetId, string uploadUrl, DateTime expiresAtUtc)
    {
        if (mediaAssetId == Guid.Empty)
        {
            throw new ArgumentException("Media asset id cannot be empty.", nameof(mediaAssetId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(uploadUrl);

        return new MEDIA_UPLOAD_SESSION
        {
            MEDIA_UPLOAD_SESSION_ID = Siri.SharedKernel.UuidV7.NewId(),
            MEDIA_ASSET_ID = mediaAssetId,
            UPLOAD_URL = uploadUrl,
            EXPIRES_AT_UTC = expiresAtUtc,
            STATUS = MediaUploadSessionStatus.Pending,
        };
    }

    /// <summary>Pending → Completed, once the client finishes uploading.</summary>
    public void Complete()
    {
        if (STATUS != MediaUploadSessionStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot complete upload session with status {STATUS}.");
        }

        STATUS = MediaUploadSessionStatus.Completed;
    }

    /// <summary>Pending → Expired.</summary>
    public void MarkExpired()
    {
        if (STATUS != MediaUploadSessionStatus.Pending)
        {
            throw new InvalidOperationException($"Cannot expire upload session with status {STATUS}.");
        }

        STATUS = MediaUploadSessionStatus.Expired;
    }
}
