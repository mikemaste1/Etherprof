namespace Etherprof.SpeedTest;

public sealed class GeneratedPayloadStream : Stream
{
    private const int BufferSize = 256 * 1024; // 256 KiB small reusable buffer (Requirement 17)
    private static readonly byte[] SharedBuffer;

    private readonly long _totalLength;
    private long _position;

    public Action<int>? OnBytesEmitted { get; set; }

    static GeneratedPayloadStream()
    {
        SharedBuffer = new byte[BufferSize];
        var rng = new Random(42);
        rng.NextBytes(SharedBuffer);
        // Ensure non-zero pattern
        for (int i = 0; i < SharedBuffer.Length; i++)
        {
            if (SharedBuffer[i] == 0) SharedBuffer[i] = (byte)((i % 254) + 1);
        }
    }

    public GeneratedPayloadStream(long totalLength)
    {
        if (totalLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(totalLength), "Total length must be greater than zero.");
        _totalLength = totalLength;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _totalLength;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_position >= _totalLength)
            return 0;

        long remaining = _totalLength - _position;
        int toCopy = (int)Math.Min(count, Math.Min(remaining, SharedBuffer.Length));

        Buffer.BlockCopy(SharedBuffer, 0, buffer, offset, toCopy);
        _position += toCopy;
        OnBytesEmitted?.Invoke(toCopy);

        return toCopy;
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position >= _totalLength)
            return 0;

        long remaining = _totalLength - _position;
        int toCopy = (int)Math.Min(buffer.Length, Math.Min(remaining, SharedBuffer.Length));

        SharedBuffer.AsMemory(0, toCopy).CopyTo(buffer);
        _position += toCopy;
        OnBytesEmitted?.Invoke(toCopy);

        return toCopy;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
