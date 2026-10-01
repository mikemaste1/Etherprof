namespace Etherprof.StreamTest.Server;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Etherprof.StreamTest.Abstractions;
using Etherprof.StreamTest.Metrics;
using Etherprof.StreamTest.Models;
using Etherprof.StreamTest.Pacing;
using Etherprof.StreamTest.Protocol;
using Microsoft.Extensions.Logging;

public sealed class StreamTestServer : IStreamTestServer
{
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<Guid, ServerSession> _sessions = new();
    private TcpListener? _tcpListener;
    private Socket? _udpSocket;
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private int _boundPort;
    private readonly object _stateLock = new();

    public StreamTestServer(ILogger<StreamTestServer>? logger = null)
    {
        _logger = logger;
    }

    public bool IsRunning
    {
        get { lock (_stateLock) return _isRunning; }
    }

    public int ActiveClientCount => _sessions.Count;

    public int BoundPort => _boundPort;

    public event EventHandler? ActiveClientsChanged;

    public Task StartAsync(int port = StreamProtocolConstants.DefaultPort, CancellationToken cancellationToken = default)
    {
        lock (_stateLock)
        {
            if (_isRunning) return Task.CompletedTask;
            _boundPort = port;
            _cts = new CancellationTokenSource();
            _isRunning = true;
        }

        try
        {
            if (port == 0)
            {
                var rand = new Random();
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    int candidatePort = rand.Next(20000, 45000);
                    try
                    {
                        var tcp = new TcpListener(IPAddress.Any, candidatePort);
                        tcp.Start();

                        var udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                        udp.Bind(new IPEndPoint(IPAddress.Any, candidatePort));

                        _tcpListener = tcp;
                        _udpSocket = udp;
                        _boundPort = candidatePort;
                        break;
                    }
                    catch (SocketException)
                    {
                        try { _tcpListener?.Stop(); } catch { }
                        try { _udpSocket?.Dispose(); } catch { }
                        _tcpListener = null;
                        _udpSocket = null;
                        if (attempt == 49) throw;
                    }
                }
            }
            else
            {
                int listenPort = port > 0 ? port : StreamProtocolConstants.DefaultPort;
                _tcpListener = new TcpListener(IPAddress.Any, listenPort);
                _tcpListener.Start();
                _boundPort = ((IPEndPoint)_tcpListener.LocalEndpoint).Port;

                _udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _udpSocket.Bind(new IPEndPoint(IPAddress.Any, _boundPort));
            }

            _logger?.LogInformation("StreamTestServer started on 0.0.0.0:{Port} (TCP & UDP)", _boundPort);

            if (_tcpListener != null)
                _ = Task.Run(() => AcceptTcpClientsAsync(_tcpListener, _cts.Token));
            if (_udpSocket != null)
                _ = Task.Run(() => ListenUdpAsync(_udpSocket, _cts.Token));
            _ = Task.Run(() => MonitorSessionExpiryAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start StreamTestServer on port {Port}", port);
            _ = StopAsync();
            throw;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        lock (_stateLock)
        {
            if (!_isRunning) return;
            _isRunning = false;
            cts = _cts;
            _cts = null;
        }

        cts?.Cancel();

        try { _tcpListener?.Stop(); } catch { }
        try { _udpSocket?.Close(); } catch { }

        foreach (var session in _sessions.Values)
        {
            session.Dispose();
        }
        _sessions.Clear();

        ActiveClientsChanged?.Invoke(this, EventArgs.Empty);
        _logger?.LogInformation("StreamTestServer stopped");
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task AcceptTcpClientsAsync(TcpListener listener, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var clientSocket = await listener.AcceptSocketAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleTcpClientAsync(clientSocket, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) break;
                _logger?.LogWarning(ex, "Error accepting TCP client");
            }
        }
    }

    private async Task HandleTcpClientAsync(Socket socket, CancellationToken cancellationToken)
    {
        Guid sessionId = Guid.Empty;
        using var stream = new NetworkStream(socket, ownsSocket: true);

        try
        {
            // Read HELLO frame using explicit TCP framing
            var helloFrame = await StreamProtocolSerializer.ReadNextFrameAsync(stream, cancellationToken).ConfigureAwait(false);
            if (helloFrame == null || helloFrame.Type != MessageType.Hello)
            {
                _logger?.LogWarning("Invalid initial TCP frame from client; expected HELLO");
                return;
            }

            if (!StreamProtocolSerializer.TryDeserializeHelloPayload(helloFrame.Payload, out var hello) || hello == null)
            {
                _logger?.LogWarning("Invalid TCP HELLO payload from client");
                return;
            }

            sessionId = hello.SessionId;
            long targetBps = Math.Min(hello.TargetBitsPerSecond, StreamTestRequest.MaxTargetBitsPerSecond);

            // Server Session Handover (Correction 5): Atomically update generation & cancel old transport
            ServerSession session;
            long generation;

            if (_sessions.TryGetValue(sessionId, out var existingSession))
            {
                session = existingSession;
                generation = session.AtomicallyHandoverTransport(socket);
            }
            else
            {
                session = new ServerSession(sessionId, hello.Protocol, hello.Direction, targetBps);
                generation = session.AtomicallyHandoverTransport(socket);
                _sessions[sessionId] = session;
            }

            var sessionCts = session.GetGenerationCts(generation);
            if (sessionCts == null) return;

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, sessionCts.Token);

            // Send READY
            var readyMsg = new ReadyMessage { SessionId = sessionId, ServerPort = _boundPort };
            byte[] readyFrame = StreamProtocolSerializer.SerializeReady(readyMsg);
            await stream.WriteAsync(readyFrame, linkedCts.Token).ConfigureAwait(false);

            ActiveClientsChanged?.Invoke(this, EventArgs.Empty);

            _logger?.LogInformation("TCP Session active {SessionId}, Direction: {Direction}, TargetRate: {Rate} bps (IsReconnect: {IsReconnect}, Generation: {Gen})",
                sessionId, hello.Direction, targetBps, hello.IsReconnect, generation);

            if (hello.Direction == StreamTestDirection.Send)
            {
                // Client -> Server (TCP SEND): Server reads DATA frames, measures DATA payload bytes, returns periodic STAT_REPORT
                _ = Task.Run(() => SendTcpStatReportsAsync(stream, session, generation, linkedCts.Token), linkedCts.Token);

                while (!linkedCts.Token.IsCancellationRequested && session.IsCurrentGeneration(generation, socket))
                {
                    var frame = await StreamProtocolSerializer.ReadNextFrameAsync(stream, linkedCts.Token).ConfigureAwait(false);
                    if (frame == null) break;

                    session.TouchActivity();

                    if (frame.Type == MessageType.Data)
                    {
                        if (StreamProtocolSerializer.TryDeserializeDataPayload(frame.Payload, out var dataMsg) && dataMsg != null)
                        {
                            // Count ONLY DATA payload content bytes (Correction 3)
                            session.AddDataBytesRead(dataMsg.Payload.Length);
                        }
                    }
                    else if (frame.Type == MessageType.Stop)
                    {
                        break;
                    }
                }
            }
            else
            {
                // Client <- Server (TCP RECEIVE): Server rate-limits & generates DATA frames to Client, reads Client STAT_REPORTs
                _ = Task.Run(() => ReadTcpStatReportsAsync(stream, session, generation, linkedCts.Token), linkedCts.Token);

                var rateLimiter = new TokenBucketRateLimiter(targetBps);
                int dataPayloadSize = Math.Clamp((int)(targetBps / 8 / 10), 1024, 32768); // Dynamic DATA payload size (Correction 4)
                byte[] rawData = new byte[dataPayloadSize];
                Random.Shared.NextBytes(rawData);

                long sequence = 0;
                while (!linkedCts.Token.IsCancellationRequested && session.IsCurrentGeneration(generation, socket))
                {
                    sequence++;
                    long sendMicro = (Stopwatch.GetTimestamp() - session.StartTicks) * 1_000_000 / Stopwatch.Frequency;
                    var dataMsg = new DataMessage
                    {
                        SessionId = sessionId,
                        SequenceNumber = sequence,
                        SendTimestampMicroseconds = sendMicro,
                        Payload = rawData
                    };

                    byte[] dataFrame = StreamProtocolSerializer.SerializeData(dataMsg, dataPayloadSize + 32 + StreamProtocolConstants.HeaderSize);

                    // Rate budget applies ONLY to generated DATA payload bytes (Correction 3)
                    await rateLimiter.PaceAsync(rawData.Length, linkedCts.Token).ConfigureAwait(false);
                    await stream.WriteAsync(dataFrame, linkedCts.Token).ConfigureAwait(false);

                    session.TouchActivity();
                    session.AddDataBytesRead(rawData.Length);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "TCP session transport ended for {SessionId}", sessionId);
        }
        finally
        {
            if (sessionId != Guid.Empty && _sessions.TryGetValue(sessionId, out var s) && s.IsCurrentSocket(socket))
            {
                _sessions.TryRemove(sessionId, out _);
                s.Dispose();
                ActiveClientsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async Task SendTcpStatReportsAsync(NetworkStream stream, ServerSession session, long generation, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && session.IsCurrentGeneration(generation))
        {
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            if (!session.IsCurrentGeneration(generation)) break;

            var report = new StatReportMessage
            {
                SessionId = session.SessionId,
                BytesReceived = session.BytesTransferred
            };

            byte[] reportFrame = StreamProtocolSerializer.SerializeStatReport(report);
            try
            {
                await stream.WriteAsync(reportFrame, cancellationToken).ConfigureAwait(false);
            }
            catch { break; }
        }
    }

    private async Task ReadTcpStatReportsAsync(NetworkStream stream, ServerSession session, long generation, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && session.IsCurrentGeneration(generation))
        {
            try
            {
                var frame = await StreamProtocolSerializer.ReadNextFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                if (frame == null) break;

                session.TouchActivity();

                if (frame.Type == MessageType.StatReport)
                {
                    if (StreamProtocolSerializer.TryDeserializeStatReportPayload(frame.Payload, out var stat) && stat != null && stat.SessionId == session.SessionId)
                    {
                        if (session.IsCurrentGeneration(generation))
                        {
                            session.LastClientStatReport = stat;
                        }
                    }
                }
            }
            catch { break; }
        }
    }

    private async Task ListenUdpAsync(Socket socket, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[2048];
        EndPoint remoteEp = new IPEndPoint(IPAddress.Any, 0);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, remoteEp, cancellationToken).ConfigureAwait(false);
                int len = result.ReceivedBytes;
                EndPoint clientEp = result.RemoteEndPoint;

                if (len < StreamProtocolConstants.HeaderSize) continue;

                byte msgType = buffer[6];

                if (msgType == (byte)MessageType.Hello)
                {
                    if (StreamProtocolSerializer.TryDeserializeHelloPayload(buffer.AsSpan(StreamProtocolConstants.HeaderSize, len - StreamProtocolConstants.HeaderSize), out var hello) && hello != null)
                    {
                        long targetBps = Math.Min(hello.TargetBitsPerSecond, StreamTestRequest.MaxTargetBitsPerSecond);
                        var session = _sessions.GetOrAdd(hello.SessionId, id => new ServerSession(id, hello.Protocol, hello.Direction, targetBps, clientEp));
                        session.LastRemoteEndPoint = clientEp;
                        session.TouchActivity();

                        // Reply READY
                        var readyMsg = new ReadyMessage { SessionId = hello.SessionId, ServerPort = _boundPort };
                        byte[] readyBytes = StreamProtocolSerializer.SerializeReady(readyMsg);
                        await socket.SendToAsync(readyBytes, SocketFlags.None, clientEp, cancellationToken).ConfigureAwait(false);

                        _logger?.LogInformation("UDP Session registered {SessionId}, Direction: {Direction}, TargetRate: {Rate} bps",
                            hello.SessionId, hello.Direction, targetBps);

                        if (hello.Direction == StreamTestDirection.Receive)
                        {
                            _ = Task.Run(() => SendUdpTrafficToClientAsync(socket, session, clientEp, cancellationToken), cancellationToken);
                        }
                        else
                        {
                            _ = Task.Run(() => SendUdpStatReportsToClientAsync(socket, session, clientEp, cancellationToken), cancellationToken);
                        }
                    }
                }
                else if (msgType == (byte)MessageType.Data)
                {
                    if (StreamProtocolSerializer.TryDeserializeDataPayload(buffer.AsSpan(StreamProtocolConstants.HeaderSize, len - StreamProtocolConstants.HeaderSize), out var dataMsg) && dataMsg != null)
                    {
                        if (_sessions.TryGetValue(dataMsg.SessionId, out var session))
                        {
                            session.TouchActivity();
                            long sessionStartTicks = session.StartTicks;
                            long nowTicks = Stopwatch.GetTimestamp();
                            long nowMicro = (nowTicks - sessionStartTicks) * 1_000_000 / Stopwatch.Frequency;

                            session.SequenceTracker.ProcessPacket(dataMsg.SequenceNumber, nowTicks);
                            session.JitterCalc.AddSample(dataMsg.SendTimestampMicroseconds, nowMicro);
                            session.AddDataBytesRead(dataMsg.Payload.Length);
                            session.LastRemoteEndPoint = clientEp;
                        }
                    }
                }
                else if (msgType == (byte)MessageType.StatReport)
                {
                    if (StreamProtocolSerializer.TryDeserializeStatReportPayload(buffer.AsSpan(StreamProtocolConstants.HeaderSize, len - StreamProtocolConstants.HeaderSize), out var statMsg) && statMsg != null)
                    {
                        if (_sessions.TryGetValue(statMsg.SessionId, out var session))
                        {
                            session.TouchActivity();
                            session.LastClientStatReport = statMsg;
                        }
                    }
                }
                else if (msgType == (byte)MessageType.Stop)
                {
                    if (len >= 27)
                    {
                        Guid sId = new Guid(buffer.AsSpan(11, 16));
                        if (_sessions.TryRemove(sId, out var s))
                        {
                            s.Dispose();
                            ActiveClientsChanged?.Invoke(this, EventArgs.Empty);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) break;
                _logger?.LogWarning(ex, "Error in UDP listener");
            }
        }
    }

    private async Task SendUdpTrafficToClientAsync(Socket socket, ServerSession session, EndPoint clientEp, CancellationToken cancellationToken)
    {
        var rateLimiter = new TokenBucketRateLimiter(session.TargetBitsPerSecond);
        long sequence = 0;
        int dataPayloadSize = StreamProtocolConstants.DefaultUdpPayloadSize - 36 - StreamProtocolConstants.HeaderSize;
        byte[] payloadData = new byte[dataPayloadSize];

        while (!cancellationToken.IsCancellationRequested && _sessions.ContainsKey(session.SessionId))
        {
            sequence++;
            long nowTicks = Stopwatch.GetTimestamp();
            long sendMicro = (nowTicks - session.StartTicks) * 1_000_000 / Stopwatch.Frequency;

            var dataMsg = new DataMessage
            {
                SessionId = session.SessionId,
                SequenceNumber = sequence,
                SendTimestampMicroseconds = sendMicro,
                Payload = payloadData
            };

            byte[] packet = StreamProtocolSerializer.SerializeData(dataMsg, StreamProtocolConstants.DefaultUdpPayloadSize);
            await rateLimiter.PaceAsync(dataPayloadSize, cancellationToken).ConfigureAwait(false);

            try
            {
                await socket.SendToAsync(packet, SocketFlags.None, clientEp, cancellationToken).ConfigureAwait(false);
                session.AddDataBytesRead(dataPayloadSize);
                session.TouchActivity();
            }
            catch { break; }
        }
    }

    private async Task SendUdpStatReportsToClientAsync(Socket socket, ServerSession session, EndPoint clientEp, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _sessions.ContainsKey(session.SessionId))
        {
            await Task.Delay(300, cancellationToken).ConfigureAwait(false);

            var statMsg = new StatReportMessage
            {
                SessionId = session.SessionId,
                HighestSequenceReceived = session.SequenceTracker.ExpectedPackets,
                UniquePacketsReceived = session.SequenceTracker.UniquePacketsReceived,
                FinalizedPacketsLost = session.SequenceTracker.FinalizedPacketsLost,
                DuplicatePackets = session.SequenceTracker.DuplicatePackets,
                OutOfOrderPackets = session.SequenceTracker.OutOfOrderPackets,
                BytesReceived = session.BytesTransferred,
                JitterMs = session.JitterCalc.CurrentJitterMs,
                LongestGapMs = session.SequenceTracker.LongestGap.TotalMilliseconds
            };

            byte[] reportPacket = StreamProtocolSerializer.SerializeStatReport(statMsg);
            try
            {
                await socket.SendToAsync(reportPacket, SocketFlags.None, clientEp, cancellationToken).ConfigureAwait(false);
            }
            catch { break; }
        }
    }

    private async Task MonitorSessionExpiryAsync(CancellationToken cancellationToken)
    {
        // 60-second inactivity session cleanup timer (Correction 6)
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(15000, cancellationToken).ConfigureAwait(false);

            long nowTicks = Stopwatch.GetTimestamp();
            foreach (var kvp in _sessions)
            {
                var session = kvp.Value;
                double idleSec = (double)(nowTicks - session.LastActivityTicks) / Stopwatch.Frequency;
                if (idleSec >= 60.0)
                {
                    if (_sessions.TryRemove(kvp.Key, out var expiredSession))
                    {
                        _logger?.LogInformation("Removing expired server session {SessionId} (Idle {IdleSec:F1}s)", kvp.Key, idleSec);
                        expiredSession.Dispose();
                        ActiveClientsChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
        }
    }

    private sealed class ServerSession : IDisposable
    {
        public Guid SessionId { get; }
        public StreamTestProtocol Protocol { get; }
        public StreamTestDirection Direction { get; }
        public long TargetBitsPerSecond { get; }
        public EndPoint? LastRemoteEndPoint { get; set; }
        public long StartTicks { get; } = Stopwatch.GetTimestamp();

        public UdpSequenceTracker SequenceTracker { get; } = new();
        public JitterCalculator JitterCalc { get; } = new();
        public StatReportMessage? LastClientStatReport { get; set; }

        private readonly object _sessionLock = new();
        private long _generation;
        private CancellationTokenSource _generationCts = new();
        private Socket? _currentSocket;
        private long _bytesTransferred;
        private long _lastActivityTicks = Stopwatch.GetTimestamp();

        public long BytesTransferred => Interlocked.Read(ref _bytesTransferred);
        public long LastActivityTicks => Interlocked.Read(ref _lastActivityTicks);

        public ServerSession(Guid sessionId, StreamTestProtocol protocol, StreamTestDirection direction, long targetBps, EndPoint? remoteEp = null)
        {
            SessionId = sessionId;
            Protocol = protocol;
            Direction = direction;
            TargetBitsPerSecond = targetBps;
            LastRemoteEndPoint = remoteEp;
        }

        public void TouchActivity()
        {
            Interlocked.Exchange(ref _lastActivityTicks, Stopwatch.GetTimestamp());
        }

        public void AddDataBytesRead(long count)
        {
            Interlocked.Add(ref _bytesTransferred, count);
            TouchActivity();
        }

        public long AtomicallyHandoverTransport(Socket socket)
        {
            lock (_sessionLock)
            {
                _generation++;
                _currentSocket = socket;
                _generationCts.Cancel();
                _generationCts = new CancellationTokenSource();
                TouchActivity();
                return _generation;
            }
        }

        public CancellationTokenSource? GetGenerationCts(long generation)
        {
            lock (_sessionLock)
            {
                if (_generation == generation) return _generationCts;
                return null;
            }
        }

        public bool IsCurrentGeneration(long generation, Socket? socket = null)
        {
            lock (_sessionLock)
            {
                if (_generation != generation) return false;
                if (socket != null && _currentSocket != null && _currentSocket != socket) return false;
                return true;
            }
        }

        public bool IsCurrentSocket(Socket socket)
        {
            lock (_sessionLock)
            {
                return _currentSocket == null || _currentSocket == socket;
            }
        }

        public void Dispose()
        {
            lock (_sessionLock)
            {
                _generationCts.Cancel();
            }
        }
    }
}
