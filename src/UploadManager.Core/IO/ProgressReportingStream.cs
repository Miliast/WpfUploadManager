namespace UploadManager.Core.IO;

/// <summary>
/// Read-only stream wrapper that reports the total number of bytes read so far.
/// </summary>
internal sealed class ProgressReportingStream(Stream inner, Action<long> onProgress) : Stream
{
    private long _bytesRead;

    public override bool CanRead => inner.CanRead;

    public override bool CanSeek => inner.CanSeek;

    public override bool CanWrite => false;

    public override long Length => inner.Length;

    public override long Position
    {
        get => inner.Position;
        set
        {
            inner.Position = value;
            _bytesRead = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => Report(inner.Read(buffer, offset, count));

    public override int Read(Span<byte> buffer) => Report(inner.Read(buffer));

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        Report(await inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Report(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));

    public override long Seek(long offset, SeekOrigin origin)
    {
        _bytesRead = inner.Seek(offset, origin);
        return _bytesRead;
    }

    public override void Flush()
    {
    }

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

    private int Report(int read)
    {
        if (read > 0)
        {
            _bytesRead += read;
            onProgress(_bytesRead);
        }

        return read;
    }
}
