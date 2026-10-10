using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Storage;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Features;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Application;

/// <summary>An uploaded teaching-material file as the browser sent it. <see cref="Content"/> must be seekable
/// (ASP.NET Core's <c>IFormFile.OpenReadStream()</c> is) because the pipeline reads the header, scans, then uploads.</summary>
public sealed record MaterialUpload(string FileName, string ContentType, Stream Content);

/// <summary>What was actually stored — all values are server-derived, none are client-claimed.</summary>
public sealed record StoredMaterial(string StorageKey, string FileName, string ContentType, long SizeBytes);

/// <summary>A freshly minted time-limited download link.</summary>
public sealed record MaterialDownloadLink(string Url, DateTime ExpiresAtUtc);

/// <summary>
/// The one place that turns an instructor's uploaded file into a private object in Cloudflare R2, and later
/// into a short-lived download link. Shared by episode attachments and live-session attachments so both obey
/// exactly the same rules (docs/contracts/P4-03c-teaching-materials-r2.md):
/// <list type="number">
/// <item>The <b>server</b> reads the first bytes of the stream and checks them against the extension
/// (<see cref="AttachmentFileValidator"/>) — the old design trusted header bytes the client supplied.</item>
/// <item>The stream goes through <see cref="IAttachmentVirusScanner"/> (refused with 503 while no engine is
/// configured — Q9; this class never reports an unscanned file as clean).</item>
/// <item>The storage key is built here from server ids and a fresh GUID; the client never chooses it, so a
/// client can't point a download at some other object in the bucket.</item>
/// <item>The object is stored with <c>Content-Disposition: attachment</c> and a server-derived MIME type, so it is
/// downloaded, never rendered inline from the storage origin.</item>
/// </list>
/// Authorization (who may upload / download) is the caller's job and has already happened by the time this runs.
/// </summary>
public sealed class TeachingMaterialStorage(
    IFileStorage fileStorage,
    IAttachmentVirusScanner virusScanner,
    IOptions<EpisodeAttachmentOptions> options,
    IClock clock,
    ILogger<TeachingMaterialStorage> logger)
{
    /// <summary>How many leading bytes are inspected for magic-number / binary-text checks.</summary>
    public const int HeaderBytesToInspect = 4096;

    /// <summary>Root of every key this class creates — one prefix so a bucket lifecycle rule or an IAM-style
    /// policy can target teaching materials without touching other objects.</summary>
    public const string KeyRoot = "teaching-materials";

    public static string EpisodeKeyPrefix(Guid courseId, Guid episodeId) =>
        $"{KeyRoot}/courses/{courseId:N}/episodes/{episodeId:N}";

    public static string LiveSessionKeyPrefix(Guid courseId, Guid sessionId) =>
        $"{KeyRoot}/courses/{courseId:N}/live-sessions/{sessionId:N}";

    public async Task<Result<StoredMaterial>> StoreAsync(
        string keyPrefix,
        MaterialUpload upload,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPrefix);
        ArgumentNullException.ThrowIfNull(upload);

        var stream = upload.Content;
        if (!stream.CanSeek)
        {
            // A programming error in the caller, not a client mistake.
            throw new InvalidOperationException("Teaching-material uploads must be passed as a seekable stream.");
        }

        var fileName = AttachmentFileNames.Sanitize(upload.FileName);
        if (fileName is null)
        {
            return Result.Failure<StoredMaterial>(DomainError.Validation("ชื่อไฟล์ไม่ถูกต้อง"));
        }

        var extension = AttachmentFileValidator.GetFileExtension(fileName).ToLowerInvariant();
        var length = stream.Length;

        var header = new byte[(int)Math.Min(length, HeaderBytesToInspect)];
        stream.Position = 0;
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;

        var validation = AttachmentFileValidator.Validate(
            fileName,
            NormalizeContentType(upload.ContentType),
            length,
            header,
            options.Value.MaxFileSizeBytes);

        if (validation.IsFailure)
        {
            return Result.Failure<StoredMaterial>(validation.Error);
        }

        var scan = await virusScanner.ScanAsync(fileName, stream, cancellationToken).ConfigureAwait(false);
        stream.Position = 0;
        if (scan.IsFailure)
        {
            return Result.Failure<StoredMaterial>(scan.Error);
        }

        var contentType = AttachmentFileValidator.GetCanonicalContentType(extension) ?? "application/octet-stream";
        var key = $"{keyPrefix.TrimEnd('/')}/{Guid.NewGuid():N}{extension}";

        var stored = await fileStorage.UploadAsync(key, stream, length, contentType, fileName, cancellationToken).ConfigureAwait(false);
        if (stored.IsFailure)
        {
            return Result.Failure<StoredMaterial>(stored.Error);
        }

        return Result.Success(new StoredMaterial(key, fileName, contentType, length));
    }

    public async Task<Result<MaterialDownloadLink>> CreateDownloadLinkAsync(string storageKey, CancellationToken cancellationToken)
    {
        var ttl = TimeSpan.FromSeconds(options.Value.DownloadUrlTtlSeconds);
        var signed = await fileStorage.GetSignedUrlAsync(storageKey, ttl, cancellationToken).ConfigureAwait(false);

        return signed.IsSuccess
            ? Result.Success(new MaterialDownloadLink(signed.Value, clock.UtcNow.Add(ttl)))
            : Result.Failure<MaterialDownloadLink>(signed.Error);
    }

    /// <summary>
    /// Best-effort removal of objects whose database rows are already gone (or never got written). A failure
    /// here only leaves an unreachable orphan in a private bucket, so it is logged and swallowed rather than
    /// failing a request whose real work succeeded.
    /// </summary>
    public async Task DeleteQuietlyAsync(IEnumerable<string> storageKeys, CancellationToken cancellationToken)
    {
        foreach (var key in storageKeys)
        {
            var result = await fileStorage.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                logger.LogWarning(
                    "Teaching-material object {StorageKey} could not be deleted ({ErrorCode}); it remains as an orphan in storage.",
                    key,
                    result.Error.Code);
            }
        }
    }

    /// <summary>Drops MIME parameters (<c>text/plain; charset=utf-8</c>) and treats a missing type as the generic binary type.</summary>
    internal static string NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return "application/octet-stream";
        }

        var semicolon = contentType.IndexOf(';');
        var essence = semicolon >= 0 ? contentType[..semicolon] : contentType;

        return essence.Trim().ToLowerInvariant();
    }
}
