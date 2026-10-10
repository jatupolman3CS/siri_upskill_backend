using System.Text;
using Siri.Integrations.Storage;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Application;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Catalog;

public sealed class TeachingMaterialStorageTests
{
    private static readonly string Prefix = TeachingMaterialStorage.EpisodeKeyPrefix(Guid.NewGuid(), Guid.NewGuid());

    private static MemoryStream Pdf(int extraBytes = 100) =>
        new([.. "%PDF-1.7\n"u8.ToArray(), .. new byte[extraBytes]]);

    [Fact]
    public async Task StoreAsync_ValidPdf_UploadsUnderServerBuiltKeyWithServerDerivedMetadata()
    {
        var storage = new InMemoryFileStorage();
        var sut = TeachingMaterialTestFactory.Create(storage);
        await using var content = Pdf();

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("Lesson 1.PDF", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var stored = result.Value;
        Assert.StartsWith(Prefix + "/", stored.StorageKey);
        Assert.EndsWith(".pdf", stored.StorageKey);
        Assert.Equal("Lesson 1.PDF", stored.FileName);
        Assert.Equal("application/pdf", stored.ContentType);
        Assert.Equal(content.Length, stored.SizeBytes);

        var uploaded = Assert.Single(storage.Objects);
        Assert.Equal(stored.StorageKey, uploaded.Key);
        Assert.Equal("application/pdf", uploaded.Value.ContentType);
        Assert.Equal("Lesson 1.PDF", uploaded.Value.DownloadFileName);
        Assert.Equal(content.Length, uploaded.Value.Content.Length);
    }

    [Fact]
    public async Task StoreAsync_ClientFileNameNeverReachesTheStorageKey()
    {
        var storage = new InMemoryFileStorage();
        var sut = TeachingMaterialTestFactory.Create(storage);
        await using var content = Pdf();

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("..\\..\\secret folder\\evil name.pdf", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("evil name.pdf", result.Value.FileName);
        Assert.DoesNotContain("evil", result.Value.StorageKey, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("..", result.Value.StorageKey, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StoreAsync_ThaiFileName_IsPreservedForDisplay()
    {
        var sut = TeachingMaterialTestFactory.Create();
        await using var content = Pdf();

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("เอกสารประกอบ.pdf", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("เอกสารประกอบ.pdf", result.Value.FileName);
    }

    [Fact]
    public async Task StoreAsync_SameFileTwice_GetsTwoDifferentKeys()
    {
        var sut = TeachingMaterialTestFactory.Create();
        await using var first = Pdf();
        await using var second = Pdf();

        var a = await sut.StoreAsync(Prefix, new MaterialUpload("a.pdf", "application/pdf", first), CancellationToken.None);
        var b = await sut.StoreAsync(Prefix, new MaterialUpload("a.pdf", "application/pdf", second), CancellationToken.None);

        Assert.NotEqual(a.Value.StorageKey, b.Value.StorageKey);
    }

    [Fact]
    public async Task StoreAsync_ExecutableDisguisedAsPdf_IsRejectedAndNothingIsStoredOrScanned()
    {
        var storage = new InMemoryFileStorage();
        var scanner = StubVirusScanner.Clean();
        var sut = TeachingMaterialTestFactory.Create(storage, scanner);
        await using var content = new MemoryStream([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("innocent.pdf", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(0, storage.UploadCalls);
        Assert.Equal(0, scanner.ScanCalls);
    }

    [Fact]
    public async Task StoreAsync_PngBytesNamedPdf_IsRejected()
    {
        var storage = new InMemoryFileStorage();
        var sut = TeachingMaterialTestFactory.Create(storage);
        await using var content = new MemoryStream([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]);

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("a.pdf", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, storage.UploadCalls);
    }

    [Theory]
    [InlineData("tool.exe", "application/octet-stream")]
    [InlineData("page.html", "text/html")]
    [InlineData("script.js", "text/javascript")]
    [InlineData("noextension", "application/pdf")]
    public async Task StoreAsync_DisallowedExtension_IsRejected(string fileName, string contentType)
    {
        var storage = new InMemoryFileStorage();
        var sut = TeachingMaterialTestFactory.Create(storage);
        await using var content = Pdf();

        var result = await sut.StoreAsync(Prefix, new MaterialUpload(fileName, contentType, content), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, storage.UploadCalls);
    }

    [Fact]
    public async Task StoreAsync_DeclaredMimeThatDoesNotMatchExtension_IsRejected()
    {
        var storage = new InMemoryFileStorage();
        var sut = TeachingMaterialTestFactory.Create(storage);
        await using var content = Pdf();

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("a.pdf", "image/png", content), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, storage.UploadCalls);
    }

    [Fact]
    public async Task StoreAsync_MimeParametersAreIgnored()
    {
        var storage = new InMemoryFileStorage();
        var sut = TeachingMaterialTestFactory.Create(storage);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("hello notes"));

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("notes.txt", "Text/Plain; charset=utf-8", content), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal("text/plain", result.Value.ContentType);
    }

    [Fact]
    public async Task StoreAsync_StoredContentTypeComesFromTheExtensionNotTheClient()
    {
        var storage = new InMemoryFileStorage();
        var sut = TeachingMaterialTestFactory.Create(storage);
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("a,b\n1,2"));

        // text/plain is an allowed alias for .csv, but the stored/served type is the canonical one.
        var result = await sut.StoreAsync(Prefix, new MaterialUpload("data.csv", "text/plain", content), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("text/csv", result.Value.ContentType);
        Assert.Equal("text/csv", Assert.Single(storage.Objects).Value.ContentType);
    }

    [Fact]
    public async Task StoreAsync_MissingContentType_IsTreatedAsOctetStream()
    {
        var sut = TeachingMaterialTestFactory.Create();
        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("# Notes"));

        // .md allows application/octet-stream (browsers often send no type for it).
        var result = await sut.StoreAsync(Prefix, new MaterialUpload("notes.md", "", content), CancellationToken.None);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
    }

    [Fact]
    public async Task StoreAsync_TextFileWithNullByte_IsRejected()
    {
        var sut = TeachingMaterialTestFactory.Create();
        await using var content = new MemoryStream([0x68, 0x69, 0x00, 0x01]);

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("notes.txt", "text/plain", content), CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task StoreAsync_FileOverTheConfiguredLimit_IsRejectedBeforeUpload()
    {
        var storage = new InMemoryFileStorage();
        var sut = TeachingMaterialTestFactory.Create(storage, options: new EpisodeAttachmentOptions { MaxFileSizeBytes = 1024 });
        await using var content = Pdf(extraBytes: 2000);

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("big.pdf", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, storage.UploadCalls);
    }

    [Fact]
    public async Task StoreAsync_EmptyFile_IsRejected()
    {
        var storage = new InMemoryFileStorage();
        var sut = TeachingMaterialTestFactory.Create(storage);
        await using var content = new MemoryStream();

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("empty.pdf", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(0, storage.UploadCalls);
    }

    [Fact]
    public async Task StoreAsync_ScannerRefusal_BlocksTheUploadAndPropagatesTheError()
    {
        var storage = new InMemoryFileStorage();
        var refusal = new DomainError("attachment.virus_scanner_not_configured", "no engine");
        var scanner = new StubVirusScanner(Result.Failure(refusal));
        var sut = TeachingMaterialTestFactory.Create(storage, scanner);
        await using var content = Pdf();

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("a.pdf", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(refusal, result.Error);
        Assert.Equal(1, scanner.ScanCalls);
        Assert.Equal(0, storage.UploadCalls);
    }

    [Fact]
    public async Task StoreAsync_StorageFailure_IsReturnedAndNothingIsRecorded()
    {
        var storage = new InMemoryFileStorage { FailUploads = true };
        var sut = TeachingMaterialTestFactory.Create(storage);
        await using var content = Pdf();

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("a.pdf", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unavailable", result.Error.Code);
        Assert.Empty(storage.Objects);
    }

    [Fact]
    public async Task StoreAsync_UploadsTheWholeFileEvenAfterHeaderInspectionAndScan()
    {
        var storage = new InMemoryFileStorage();
        var sut = TeachingMaterialTestFactory.Create(storage);
        var bytes = new byte[10_000];
        "%PDF-1.7\n"u8.CopyTo(bytes);
        new Random(42).NextBytes(bytes.AsSpan(20));
        await using var content = new MemoryStream(bytes);

        var result = await sut.StoreAsync(Prefix, new MaterialUpload("big.pdf", "application/pdf", content), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(bytes, Assert.Single(storage.Objects).Value.Content);
    }

    [Fact]
    public async Task StoreAsync_NonSeekableStream_IsAProgrammingError()
    {
        var sut = TeachingMaterialTestFactory.Create();
        await using var content = new NonSeekableStream();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.StoreAsync(Prefix, new MaterialUpload("a.pdf", "application/pdf", content), CancellationToken.None));
    }

    [Fact]
    public async Task CreateDownloadLinkAsync_UsesConfiguredTtlAndReportsExpiry()
    {
        var now = new DateTime(2026, 10, 10, 3, 0, 0, DateTimeKind.Utc);
        var sut = TeachingMaterialTestFactory.Create(options: new EpisodeAttachmentOptions { DownloadUrlTtlSeconds = 120 }, now: now);

        var link = await sut.CreateDownloadLinkAsync("teaching-materials/x/a.pdf", CancellationToken.None);

        Assert.True(link.IsSuccess);
        Assert.Equal(now.AddSeconds(120), link.Value.ExpiresAtUtc);
        Assert.Contains("teaching-materials/x/a.pdf", link.Value.Url);
        Assert.Contains("ttl=120", link.Value.Url);
    }

    [Fact]
    public async Task DeleteQuietlyAsync_KeepsGoingWhenOneDeleteFails()
    {
        var storage = new FlakyDeleteStorage(failingKey: "a");
        var sut = TeachingMaterialTestFactory.Create(storage);

        await sut.DeleteQuietlyAsync(["a", "b", "c"], CancellationToken.None);

        Assert.Equal(["a", "b", "c"], storage.AttemptedKeys);
    }

    [Fact]
    public void KeyPrefixes_AreScopedByCourseAndParent()
    {
        var courseId = Guid.NewGuid();
        var parentId = Guid.NewGuid();

        Assert.Equal($"teaching-materials/courses/{courseId:N}/episodes/{parentId:N}", TeachingMaterialStorage.EpisodeKeyPrefix(courseId, parentId));
        Assert.Equal($"teaching-materials/courses/{courseId:N}/live-sessions/{parentId:N}", TeachingMaterialStorage.LiveSessionKeyPrefix(courseId, parentId));
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }

    private sealed class FlakyDeleteStorage(string failingKey) : IFileStorage
    {
        public List<string> AttemptedKeys { get; } = [];

        public Task<Result<StoredFile>> UploadAsync(string key, Stream content, long contentLength, string contentType, string? downloadFileName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<string>> GetSignedUrlAsync(string key, TimeSpan timeToLive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken)
        {
            AttemptedKeys.Add(key);
            return Task.FromResult(key == failingKey ? Result.Failure(StorageErrors.OperationFailed()) : Result.Success());
        }
    }
}
