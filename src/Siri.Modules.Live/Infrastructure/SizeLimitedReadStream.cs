namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// A forward-only read-through wrapper that refuses to hand out more than <c>maxBytes</c> bytes (P11-13 <c>MaxFileSizeMegabytes</c>). The recording import streams a
/// Google Drive file straight to the video provider without ever buffering it; when Google does not announce the size up front (<c>Content-Length</c> missing) this is
/// what stops an oversized file mid-flight. Reading past the limit throws <see cref="RecordingTooLargeException"/> and sets <see cref="Exceeded"/>, so the caller can
/// tell "too large" from any other I/O failure even if the code in between wraps the exception.
/// </summary>
internal sealed class SizeLimitedReadStream(Stream inner, long maxBytes) : Stream
{
    private long _totalRead;

    /// <summary>True once a read would have gone past the limit.</summary>
    public bool Exceeded { get; private set; }

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Count(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    private int Count(int read)
    {
        _totalRead += read;
        if (_totalRead > maxBytes)
        {
            Exceeded = true;
            throw new RecordingTooLargeException();
        }

        return read;
    }
}

/// <summary>The recording is larger than <c>Live:Recording:AutoImport:MaxFileSizeMegabytes</c>. Carries no data on purpose (not a file name, size or id).</summary>
internal sealed class RecordingTooLargeException() : IOException("The recording exceeds the configured maximum size.");
