namespace Etherprof.StreamTest.Tests;

using System.IO;
using System.Text;

using Etherprof.StreamTest.Models;
using Etherprof.StreamTest.Protocol;
using Xunit;

public class StreamProtocolTests
{
    [Fact]
    public void SerializeAndDeserialize_HelloMessage_Matches()
    {
        var original = new HelloMessage
        {
            SessionId = Guid.NewGuid(),
            Protocol = StreamTestProtocol.Udp,
            Direction = StreamTestDirection.Receive,
            TargetBitsPerSecond = 5_000_000,
            IsReconnect = true
        };

        byte[] bytes = StreamProtocolSerializer.SerializeHello(original);
        using var ms = new MemoryStream(bytes);
        var frame = StreamProtocolSerializer.ReadNextFrameAsync(ms).GetAwaiter().GetResult();

        Assert.NotNull(frame);
        Assert.Equal(MessageType.Hello, frame!.Type);

        bool success = StreamProtocolSerializer.TryDeserializeHelloPayload(frame.Payload, out var deserialized);
        Assert.True(success);
        Assert.NotNull(deserialized);
        Assert.Equal(original.SessionId, deserialized!.SessionId);
        Assert.Equal(original.Protocol, deserialized.Protocol);
        Assert.Equal(original.Direction, deserialized.Direction);
        Assert.Equal(original.TargetBitsPerSecond, deserialized.TargetBitsPerSecond);
        Assert.True(deserialized.IsReconnect);
    }

    [Fact]
    public void SerializeAndDeserialize_StatReportMessage_Matches()
    {
        var original = new StatReportMessage
        {
            SessionId = Guid.NewGuid(),
            HighestSequenceReceived = 100,
            UniquePacketsReceived = 95,
            FinalizedPacketsLost = 5,
            DuplicatePackets = 2,
            OutOfOrderPackets = 3,
            BytesReceived = 120_000,
            JitterMs = 4.2,
            LongestGapMs = 150.0
        };

        byte[] bytes = StreamProtocolSerializer.SerializeStatReport(original);
        using var ms = new MemoryStream(bytes);
        var frame = StreamProtocolSerializer.ReadNextFrameAsync(ms).GetAwaiter().GetResult();

        Assert.NotNull(frame);
        Assert.Equal(MessageType.StatReport, frame!.Type);

        bool success = StreamProtocolSerializer.TryDeserializeStatReportPayload(frame.Payload, out var deserialized);
        Assert.True(success);
        Assert.NotNull(deserialized);
        Assert.Equal(original.SessionId, deserialized!.SessionId);
        Assert.Equal(original.HighestSequenceReceived, deserialized.HighestSequenceReceived);
        Assert.Equal(original.UniquePacketsReceived, deserialized.UniquePacketsReceived);
        Assert.Equal(original.FinalizedPacketsLost, deserialized.FinalizedPacketsLost);
        Assert.Equal(original.DuplicatePackets, deserialized.DuplicatePackets);
        Assert.Equal(original.OutOfOrderPackets, deserialized.OutOfOrderPackets);
        Assert.Equal(original.BytesReceived, deserialized.BytesReceived);
        Assert.Equal(original.JitterMs, deserialized.JitterMs, precision: 2);
        Assert.Equal(original.LongestGapMs, deserialized.LongestGapMs, precision: 2);
    }

    [Fact]
    public async Task ReadNextFrameAsync_MultipleFramesInSingleStream_ReadsInOrder()
    {
        var dataMsg = new DataMessage { SessionId = Guid.NewGuid(), SequenceNumber = 1, SendTimestampMicroseconds = 100, Payload = Encoding.ASCII.GetBytes("DATA1") };
        var statMsg = new StatReportMessage { SessionId = dataMsg.SessionId, BytesReceived = 5000 };

        byte[] frame1 = StreamProtocolSerializer.SerializeData(dataMsg, 100);
        byte[] frame2 = StreamProtocolSerializer.SerializeStatReport(statMsg);

        byte[] combined = new byte[frame1.Length + frame2.Length];
        Buffer.BlockCopy(frame1, 0, combined, 0, frame1.Length);
        Buffer.BlockCopy(frame2, 0, combined, frame1.Length, frame2.Length);

        using var ms = new MemoryStream(combined);

        var parsed1 = await StreamProtocolSerializer.ReadNextFrameAsync(ms);
        Assert.NotNull(parsed1);
        Assert.Equal(MessageType.Data, parsed1!.Type);

        var parsed2 = await StreamProtocolSerializer.ReadNextFrameAsync(ms);
        Assert.NotNull(parsed2);
        Assert.Equal(MessageType.StatReport, parsed2!.Type);
    }

    [Fact]
    public async Task ReadNextFrameAsync_FrameSplitAcrossSingleByteReads_ParsesSuccessfully()
    {
        var dataMsg = new DataMessage { SessionId = Guid.NewGuid(), SequenceNumber = 42, SendTimestampMicroseconds = 500, Payload = Encoding.ASCII.GetBytes("SPLIT_TEST") };
        byte[] frameBytes = StreamProtocolSerializer.SerializeData(dataMsg, 100);

        using var slowStream = new SlowByteStream(frameBytes);
        var parsed = await StreamProtocolSerializer.ReadNextFrameAsync(slowStream);

        Assert.NotNull(parsed);
        Assert.Equal(MessageType.Data, parsed!.Type);

        bool ok = StreamProtocolSerializer.TryDeserializeDataPayload(parsed.Payload, out var deserialized);
        Assert.True(ok);
        Assert.Equal(42, deserialized!.SequenceNumber);
        Assert.Equal("SPLIT_TEST", Encoding.ASCII.GetString(deserialized.Payload));
    }

    [Fact]
    public async Task ReadNextFrameAsync_PayloadContainsMagicBytes_DoesNotBreakFraming()
    {
        // DATA payload explicitly contains the bytes "EPROF" (the protocol magic string!)
        byte[] sneakyPayload = Encoding.ASCII.GetBytes("PREFIX_EPROF_SUFFIX");
        var dataMsg = new DataMessage { SessionId = Guid.NewGuid(), SequenceNumber = 99, Payload = sneakyPayload };

        byte[] frameBytes = StreamProtocolSerializer.SerializeData(dataMsg, 100);
        using var ms = new MemoryStream(frameBytes);

        var parsed = await StreamProtocolSerializer.ReadNextFrameAsync(ms);
        Assert.NotNull(parsed);
        Assert.Equal(MessageType.Data, parsed!.Type);

        bool ok = StreamProtocolSerializer.TryDeserializeDataPayload(parsed.Payload, out var deserialized);
        Assert.True(ok);
        Assert.Equal(sneakyPayload, deserialized!.Payload);
    }

    [Fact]
    public async Task ReadNextFrameAsync_MalformedPayloadLength_ThrowsInvalidDataException()
    {
        byte[] invalidFrame = new byte[11];
        Buffer.BlockCopy(StreamProtocolConstants.Magic, 0, invalidFrame, 0, 5);
        invalidFrame[5] = StreamProtocolConstants.Version;
        invalidFrame[6] = (byte)MessageType.Data;
        // Payload length = 2,000,000 bytes (exceeds MaxTcpFramePayloadSize 1 MiB)
        invalidFrame[7] = 0x00;
        invalidFrame[8] = 0x1E;
        invalidFrame[9] = 0x84;
        invalidFrame[10] = 0x80;

        using var ms = new MemoryStream(invalidFrame);
        await Assert.ThrowsAsync<InvalidDataException>(() => StreamProtocolSerializer.ReadNextFrameAsync(ms));
    }

    [Fact]
    public async Task ReadNextFrameAsync_TruncatedStream_ThrowsEndOfStreamException()
    {
        var dataMsg = new DataMessage { SessionId = Guid.NewGuid(), SequenceNumber = 1, Payload = new byte[100] };
        byte[] fullFrame = StreamProtocolSerializer.SerializeData(dataMsg, 150);

        // Truncate payload halfway
        byte[] truncatedFrame = new byte[fullFrame.Length - 20];
        Buffer.BlockCopy(fullFrame, 0, truncatedFrame, 0, truncatedFrame.Length);

        using var ms = new MemoryStream(truncatedFrame);
        await Assert.ThrowsAsync<EndOfStreamException>(() => StreamProtocolSerializer.ReadNextFrameAsync(ms));
    }

    private sealed class SlowByteStream : Stream
    {
        private readonly byte[] _data;
        private int _position;

        public SlowByteStream(byte[] data) => _data = data;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _data.Length) return 0;
            buffer[offset] = _data[_position++];
            return 1; // Yield only 1 byte per read to test fragmentation
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _data.Length) return ValueTask.FromResult(0);
            buffer.Span[0] = _data[_position++];
            return ValueTask.FromResult(1);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
