namespace Etherprof.StreamTest.Protocol;

using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Text;
using Etherprof.StreamTest.Models;

public enum MessageType : byte
{
    Hello = 0x01,
    Ready = 0x02,
    Data = 0x03,
    StatReport = 0x04,
    Stop = 0x05,
    Error = 0x06
}

public static class StreamProtocolConstants
{
    public static readonly byte[] Magic = Encoding.ASCII.GetBytes("EPROF"); // 5 bytes
    public const byte Version = 1;
    public const int DefaultPort = 49100;
    public const int DefaultUdpPayloadSize = 1200;
    public const int HeaderSize = 11; // 5 Magic + 1 Version + 1 Type + 4 PayloadLength
    public const int MaxTcpFramePayloadSize = 1_048_576; // 1 MiB max frame payload
}

public sealed class HelloMessage
{
    public Guid SessionId { get; init; }
    public StreamTestProtocol Protocol { get; init; }
    public StreamTestDirection Direction { get; init; }
    public long TargetBitsPerSecond { get; init; }
    public bool IsReconnect { get; init; }
}

public sealed class ReadyMessage
{
    public Guid SessionId { get; init; }
    public int ServerPort { get; init; }
}

public sealed class DataMessage
{
    public Guid SessionId { get; init; }
    public long SequenceNumber { get; init; }
    public long SendTimestampMicroseconds { get; init; }
    public byte[] Payload { get; init; } = Array.Empty<byte>();
}

public sealed class StatReportMessage
{
    public Guid SessionId { get; init; }
    public long HighestSequenceReceived { get; init; }
    public long UniquePacketsReceived { get; init; }
    public long FinalizedPacketsLost { get; init; }
    public long DuplicatePackets { get; init; }
    public long OutOfOrderPackets { get; init; }
    public long BytesReceived { get; init; }
    public double JitterMs { get; init; }
    public double LongestGapMs { get; init; }
}

public sealed class TcpFrame
{
    public MessageType Type { get; }
    public byte[] Payload { get; }

    public TcpFrame(MessageType type, byte[] payload)
    {
        Type = type;
        Payload = payload;
    }
}

public static class StreamProtocolSerializer
{
    public static byte[] SerializeFrame(MessageType type, ReadOnlySpan<byte> payload)
    {
        byte[] buffer = new byte[StreamProtocolConstants.HeaderSize + payload.Length];
        Buffer.BlockCopy(StreamProtocolConstants.Magic, 0, buffer, 0, 5);
        buffer[5] = StreamProtocolConstants.Version;
        buffer[6] = (byte)type;
        BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(7, 4), payload.Length);

        if (payload.Length > 0)
        {
            payload.CopyTo(buffer.AsSpan(StreamProtocolConstants.HeaderSize));
        }

        return buffer;
    }

    public static byte[] SerializeHello(HelloMessage msg)
    {
        byte[] payload = new byte[16 + 1 + 1 + 8 + 1];
        msg.SessionId.TryWriteBytes(payload.AsSpan(0, 16));
        payload[16] = (byte)msg.Protocol;
        payload[17] = (byte)msg.Direction;
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(18, 8), msg.TargetBitsPerSecond);
        payload[26] = msg.IsReconnect ? (byte)1 : (byte)0;

        return SerializeFrame(MessageType.Hello, payload);
    }

    public static bool TryDeserializeHelloPayload(ReadOnlySpan<byte> payload, out HelloMessage? message)
    {
        message = null;
        if (payload.Length < 27) return false;

        Guid sessionId = new Guid(payload.Slice(0, 16));
        var protocol = (StreamTestProtocol)payload[16];
        var direction = (StreamTestDirection)payload[17];
        long targetBps = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(18, 8));
        bool isReconnect = payload[26] == 1;

        message = new HelloMessage
        {
            SessionId = sessionId,
            Protocol = protocol,
            Direction = direction,
            TargetBitsPerSecond = targetBps,
            IsReconnect = isReconnect
        };
        return true;
    }

    public static byte[] SerializeReady(ReadyMessage msg)
    {
        byte[] payload = new byte[16 + 4];
        msg.SessionId.TryWriteBytes(payload.AsSpan(0, 16));
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(16, 4), msg.ServerPort);

        return SerializeFrame(MessageType.Ready, payload);
    }

    public static bool TryDeserializeReadyPayload(ReadOnlySpan<byte> payload, out ReadyMessage? message)
    {
        message = null;
        if (payload.Length < 20) return false;

        Guid sessionId = new Guid(payload.Slice(0, 16));
        int port = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(16, 4));

        message = new ReadyMessage
        {
            SessionId = sessionId,
            ServerPort = port
        };
        return true;
    }

    public static byte[] SerializeData(DataMessage msg, int totalDatagramSize = StreamProtocolConstants.DefaultUdpPayloadSize)
    {
        int contentLen = msg.Payload?.Length ?? 0;
        int metadataLength = 16 + 8 + 8 + 4; // 36 bytes (16 SessionId, 8 Seq, 8 Ts, 4 ContentLen)
        int paddingLen = Math.Max(0, totalDatagramSize - (StreamProtocolConstants.HeaderSize + metadataLength + contentLen));

        byte[] payload = new byte[metadataLength + contentLen + paddingLen];

        msg.SessionId.TryWriteBytes(payload.AsSpan(0, 16));
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(16, 8), msg.SequenceNumber);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(24, 8), msg.SendTimestampMicroseconds);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(32, 4), contentLen);

        if (contentLen > 0 && msg.Payload != null)
        {
            Buffer.BlockCopy(msg.Payload, 0, payload, metadataLength, contentLen);
        }

        return SerializeFrame(MessageType.Data, payload);
    }

    public static bool TryDeserializeDataPayload(ReadOnlySpan<byte> payload, out DataMessage? message)
    {
        message = null;
        if (payload.Length < 36) return false;

        Guid sessionId = new Guid(payload.Slice(0, 16));
        long seq = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(16, 8));
        long sendTsMicro = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(24, 8));
        int contentLen = BinaryPrimitives.ReadInt32BigEndian(payload.Slice(32, 4));

        if (contentLen < 0 || contentLen > payload.Length - 36) return false;

        byte[] dataContent = payload.Slice(36, contentLen).ToArray();

        message = new DataMessage
        {
            SessionId = sessionId,
            SequenceNumber = seq,
            SendTimestampMicroseconds = sendTsMicro,
            Payload = dataContent
        };
        return true;
    }

    public static byte[] SerializeStatReport(StatReportMessage msg)
    {
        // 16 (sessionId) + 6*8 (longs) + 2*8 (doubles) = 16 + 48 + 16 = 80 bytes payload
        byte[] payload = new byte[80];
        msg.SessionId.TryWriteBytes(payload.AsSpan(0, 16));
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(16, 8), msg.HighestSequenceReceived);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(24, 8), msg.UniquePacketsReceived);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(32, 8), msg.FinalizedPacketsLost);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(40, 8), msg.DuplicatePackets);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(48, 8), msg.OutOfOrderPackets);
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(56, 8), msg.BytesReceived);
        BinaryPrimitives.WriteDoubleBigEndian(payload.AsSpan(64, 8), msg.JitterMs);
        BinaryPrimitives.WriteDoubleBigEndian(payload.AsSpan(72, 8), msg.LongestGapMs);

        return SerializeFrame(MessageType.StatReport, payload);
    }

    public static bool TryDeserializeStatReportPayload(ReadOnlySpan<byte> payload, out StatReportMessage? message)
    {
        message = null;
        if (payload.Length < 80) return false;

        Guid sessionId = new Guid(payload.Slice(0, 16));
        long highestSeq = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(16, 8));
        long uniqueRecvd = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(24, 8));
        long finalizedLost = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(32, 8));
        long duplicates = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(40, 8));
        long outOfOrder = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(48, 8));
        long bytes = BinaryPrimitives.ReadInt64BigEndian(payload.Slice(56, 8));
        double jitter = BinaryPrimitives.ReadDoubleBigEndian(payload.Slice(64, 8));
        double gap = BinaryPrimitives.ReadDoubleBigEndian(payload.Slice(72, 8));

        message = new StatReportMessage
        {
            SessionId = sessionId,
            HighestSequenceReceived = highestSeq,
            UniquePacketsReceived = uniqueRecvd,
            FinalizedPacketsLost = finalizedLost,
            DuplicatePackets = duplicates,
            OutOfOrderPackets = outOfOrder,
            BytesReceived = bytes,
            JitterMs = jitter,
            LongestGapMs = gap
        };
        return true;
    }

    /// <summary>
    /// Reads a full framed TCP message from a stream asynchronously, handling partial reads and multi-frame fragmentation.
    /// </summary>
    public static async Task<TcpFrame?> ReadNextFrameAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        byte[] headerBuf = new byte[StreamProtocolConstants.HeaderSize];
        int headerRead = 0;

        while (headerRead < StreamProtocolConstants.HeaderSize)
        {
            int r = await stream.ReadAsync(headerBuf.AsMemory(headerRead, StreamProtocolConstants.HeaderSize - headerRead), cancellationToken).ConfigureAwait(false);
            if (r <= 0) return null;
            headerRead += r;
        }

        ReadOnlySpan<byte> headerSpan = headerBuf;
        if (!headerSpan.Slice(0, 5).SequenceEqual(StreamProtocolConstants.Magic))
        {
            throw new InvalidDataException("Invalid frame magic marker.");
        }

        if (headerSpan[5] != StreamProtocolConstants.Version)
        {
            throw new InvalidDataException($"Unsupported protocol version: {headerSpan[5]}");
        }

        var type = (MessageType)headerSpan[6];
        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(headerSpan.Slice(7, 4));

        if (payloadLength is < 0 or > StreamProtocolConstants.MaxTcpFramePayloadSize)
        {
            throw new InvalidDataException($"Invalid payload length: {payloadLength}");
        }

        byte[] payload = new byte[payloadLength];
        int payloadRead = 0;
        while (payloadRead < payloadLength)
        {
            int r = await stream.ReadAsync(payload.AsMemory(payloadRead, payloadLength - payloadRead), cancellationToken).ConfigureAwait(false);
            if (r <= 0) throw new EndOfStreamException("Truncated TCP frame payload.");
            payloadRead += r;
        }

        return new TcpFrame(type, payload);
    }
}
