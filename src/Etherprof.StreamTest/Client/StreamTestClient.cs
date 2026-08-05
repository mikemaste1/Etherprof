namespace Etherprof.StreamTest.Client;

using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Etherprof.Network.Windows;
using Etherprof.StreamTest.Abstractions;
using Etherprof.StreamTest.Metrics;
using Etherprof.StreamTest.Models;
using Etherprof.StreamTest.Pacing;
using Etherprof.StreamTest.Protocol;
using Microsoft.Extensions.Logging;

public sealed class StreamTestClient : IStreamTestClient
{
    private readonly ILogger? _logger;
    private readonly BoundedStreamEventLog _eventLog = new(100);
    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private StreamTestState _state = StreamTestState.Idle;
    private StreamTestStatistics _currentStats = new();
    private readonly object _stateLock = new();

    public StreamTestClient(ILogger<StreamTestClient>? logger = null)
    {
        _logger = logger;
    }

    public bool IsRunning
    {
        get { lock (_stateLock) return _isRunning; }
    }

    public StreamTestState State
    {
        get { lock (_stateLock) return _state; }
        private set
        {
            StreamTestState oldState;
            lock (_stateLock)
            {
                if (_state == value) return;
                oldState = _state;
                _state = value;
            }
            StateChanged?.Invoke(this, new StreamTestStateChangedEventArgs(oldState, value));
        }
    }

    public StreamTestStatistics CurrentStatistics
    {
        get { lock (_stateLock) return _currentStats; }
    }

    public IReadOnlyList<StreamRuntimeEvent> EventLog => _eventLog.GetAll();

    public event EventHandler<StreamTestProgressEventArgs>? ProgressChanged;
    public event EventHandler<StreamTestStateChangedEventArgs>? StateChanged;

    public void AddEventLog(StreamRuntimeEvent evt)
    {
        _eventLog.Add(evt);
    }

    public async Task StartAsync(StreamTestRequest request, CancellationToken cancellationToken = default)
    {
        request.Validate();

        await StopAsync().ConfigureAwait(false);

        lock (_stateLock)
        {
            _cts = new CancellationTokenSource();
            _isRunning = true;
            _eventLog.Clear();
            _currentStats = new StreamTestStatistics
            {
                StartedAt = DateTimeOffset.Now,
                TargetBitsPerSecond = request.TargetBitsPerSecond
            };
        }

        State = StreamTestState.Connecting;
        _eventLog.Add(new StreamRuntimeEvent { Type = StreamRuntimeEventType.Started, Message = $"Starting {request.Protocol} {request.Direction} stream to {request.ServerHost}:{request.Port}" });

        _ = Task.Run(() => RunClientLoopAsync(request, _cts.Token), CancellationToken.None);
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

        State = StreamTestState.Stopping;
        cts?.Cancel();

        _eventLog.Add(new StreamRuntimeEvent { Type = StreamRuntimeEventType.Stopped, Message = "Stream test stopped by user" });
        State = StreamTestState.Idle;

        _logger?.LogInformation("StreamTestClient stopped");
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }

    private async Task RunClientLoopAsync(StreamTestRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.Protocol == StreamTestProtocol.Tcp)
            {
                await RunTcpClientAsync(request, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await RunUdpClientAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Fatal error in StreamTestClient");
            State = StreamTestState.Failed;
            _eventLog.Add(new StreamRuntimeEvent { Type = StreamRuntimeEventType.Error, Message = ex.Message });
        }
    }

    private async Task RunTcpClientAsync(StreamTestRequest request, CancellationToken cancellationToken)
    {
        var localIp = SelectedAdapterSocketFactory.ParseOrResolveLocalIPv4(request.LocalIpAddress);
        var targetIp = await SelectedAdapterSocketFactory.ResolveTargetIPv4Async(request.ServerHost, cancellationToken).ConfigureAwait(false);

        DateTimeOffset startedAt = DateTimeOffset.Now;
        long startTicks = Stopwatch.GetTimestamp();
        int reconnectCount = 0;
        TimeSpan? lastInterruption = null;
        TimeSpan? longestInterruption = null;
        long cumulativeBytesDelivered = 0;

        // Preserved logical SessionId across reconnects
        Guid logicalSessionId = Guid.NewGuid();

        while (!cancellationToken.IsCancellationRequested)
        {
            Socket? socket = null;
            NetworkStream? stream = null;
            long disconnectTicks = 0;

            try
            {
                socket = SelectedAdapterSocketFactory.CreateBoundTcpSocket(localIp);
                await socket.ConnectAsync(new IPEndPoint(targetIp, request.Port), cancellationToken).ConfigureAwait(false);
                stream = new NetworkStream(socket, ownsSocket: true);

                // Handshake HELLO with explicit framing
                var helloMsg = new HelloMessage
                {
                    SessionId = logicalSessionId,
                    Protocol = StreamTestProtocol.Tcp,
                    Direction = request.Direction,
                    TargetBitsPerSecond = request.TargetBitsPerSecond,
                    IsReconnect = reconnectCount > 0
                };

                byte[] helloFrame = StreamProtocolSerializer.SerializeHello(helloMsg);
                await stream.WriteAsync(helloFrame, cancellationToken).ConfigureAwait(false);

                // Read READY frame
                var readyFrame = await StreamProtocolSerializer.ReadNextFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                if (readyFrame == null || readyFrame.Type != MessageType.Ready)
                {
                    throw new InvalidOperationException("Invalid READY frame from server.");
                }

                if (!StreamProtocolSerializer.TryDeserializeReadyPayload(readyFrame.Payload, out var ready) || ready == null || ready.SessionId != logicalSessionId)
                {
                    throw new InvalidOperationException("Invalid READY payload from server.");
                }

                if (reconnectCount > 0 && disconnectTicks > 0)
                {
                    TimeSpan interruptDuration = TimeSpan.FromSeconds((double)(Stopwatch.GetTimestamp() - disconnectTicks) / Stopwatch.Frequency);
                    lastInterruption = interruptDuration;
                    if (longestInterruption == null || interruptDuration > longestInterruption)
                    {
                        longestInterruption = interruptDuration;
                    }
                    _eventLog.Add(new StreamRuntimeEvent
                    {
                        Type = StreamRuntimeEventType.TcpReconnected,
                        Message = $"TCP reconnected after {interruptDuration.TotalSeconds:F2}s (Reconnect #{reconnectCount})",
                        Duration = interruptDuration
                    });
                }
                else if (reconnectCount == 0)
                {
                    _eventLog.Add(new StreamRuntimeEvent { Type = StreamRuntimeEventType.TcpConnected, Message = "TCP Connected" });
                }

                State = StreamTestState.Active;

                var rateLimiter = new TokenBucketRateLimiter(request.TargetBitsPerSecond);
                int dataPayloadSize = Math.Clamp((int)(request.TargetBitsPerSecond / 8 / 10), 1024, 32768); // Dynamic DATA payload size (Correction 4)
                byte[] rawData = new byte[dataPayloadSize];
                if (request.Direction == StreamTestDirection.Send)
                {
                    Random.Shared.NextBytes(rawData);
                }

                long lastReportTicks = Stopwatch.GetTimestamp();
                long lastBytesCount = cumulativeBytesDelivered;
                long receiverReportedBytes = cumulativeBytesDelivered;
                long localWrittenBytes = 0;

                using var tcpStatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

                if (request.Direction == StreamTestDirection.Send)
                {
                    // Client -> Server (TCP SEND): Client sends DATA frames, reads STAT_REPORTs from Server
                    _ = Task.Run(async () =>
                    {
                        while (!tcpStatCts.Token.IsCancellationRequested)
                        {
                            try
                            {
                                var frame = await StreamProtocolSerializer.ReadNextFrameAsync(stream, tcpStatCts.Token).ConfigureAwait(false);
                                if (frame == null) break;
                                if (frame.Type == MessageType.StatReport)
                                {
                                    if (StreamProtocolSerializer.TryDeserializeStatReportPayload(frame.Payload, out var stat) && stat != null && stat.SessionId == logicalSessionId)
                                    {
                                        Interlocked.Exchange(ref receiverReportedBytes, stat.BytesReceived);
                                    }
                                }
                            }
                            catch { break; }
                        }
                    }, tcpStatCts.Token);

                    long sequence = 0;
                    while (!cancellationToken.IsCancellationRequested)
                    {
                        sequence++;
                        long sendMicro = (Stopwatch.GetTimestamp() - startTicks) * 1_000_000 / Stopwatch.Frequency;
                        var dataMsg = new DataMessage
                        {
                            SessionId = logicalSessionId,
                            SequenceNumber = sequence,
                            SendTimestampMicroseconds = sendMicro,
                            Payload = rawData
                        };

                        byte[] dataFrame = StreamProtocolSerializer.SerializeData(dataMsg, dataPayloadSize + 36 + StreamProtocolConstants.HeaderSize);

                        // Rate budget applies ONLY to generated DATA payload bytes (Correction 3)
                        await rateLimiter.PaceAsync(rawData.Length, cancellationToken).ConfigureAwait(false);
                        await stream.WriteAsync(dataFrame, cancellationToken).ConfigureAwait(false);

                        localWrittenBytes += rawData.Length;
                        long rxReported = Interlocked.Read(ref receiverReportedBytes);
                        cumulativeBytesDelivered = rxReported > 0 ? rxReported : localWrittenBytes;

                        long nowTicks = Stopwatch.GetTimestamp();
                        double elapsedSec = (double)(nowTicks - lastReportTicks) / Stopwatch.Frequency;
                        double recentBps = _currentStats.ActualBitsPerSecond;

                        if (elapsedSec >= 0.20)
                        {
                            recentBps = ((cumulativeBytesDelivered - lastBytesCount) * 8.0) / elapsedSec;
                            lastReportTicks = nowTicks;
                            lastBytesCount = cumulativeBytesDelivered;
                        }

                        TimeSpan duration = TimeSpan.FromSeconds((double)(nowTicks - startTicks) / Stopwatch.Frequency);
                        UpdateStats(new StreamTestStatistics
                        {
                            StartedAt = startedAt,
                            Duration = duration,
                            TargetBitsPerSecond = request.TargetBitsPerSecond,
                            ActualBitsPerSecond = recentBps,
                            BytesTransferred = cumulativeBytesDelivered,
                            ReconnectCount = reconnectCount,
                            CurrentInterruption = null,
                            LastInterruption = lastInterruption,
                            LongestInterruption = longestInterruption
                        });
                    }
                }
                else
                {
                    // Client <- Server (TCP RECEIVE): Client reads DATA frames, sends periodic STAT_REPORT to Server
                    _ = Task.Run(async () =>
                    {
                        while (!tcpStatCts.Token.IsCancellationRequested)
                        {
                            await Task.Delay(300, tcpStatCts.Token).ConfigureAwait(false);
                            var stat = new StatReportMessage
                            {
                                SessionId = logicalSessionId,
                                BytesReceived = Interlocked.Read(ref cumulativeBytesDelivered)
                            };
                            byte[] repFrame = StreamProtocolSerializer.SerializeStatReport(stat);
                            try { await stream.WriteAsync(repFrame, tcpStatCts.Token).ConfigureAwait(false); } catch { break; }
                        }
                    }, tcpStatCts.Token);

                    while (!cancellationToken.IsCancellationRequested)
                    {
                        var frame = await StreamProtocolSerializer.ReadNextFrameAsync(stream, cancellationToken).ConfigureAwait(false);
                        if (frame == null) throw new SocketException((int)SocketError.ConnectionReset);

                        if (frame.Type == MessageType.Data)
                        {
                            if (StreamProtocolSerializer.TryDeserializeDataPayload(frame.Payload, out var dataMsg) && dataMsg != null)
                            {
                                // Count ONLY DATA payload bytes (Correction 3)
                                cumulativeBytesDelivered = Interlocked.Add(ref cumulativeBytesDelivered, dataMsg.Payload.Length);
                            }
                        }

                        long nowTicks = Stopwatch.GetTimestamp();
                        double elapsedSec = (double)(nowTicks - lastReportTicks) / Stopwatch.Frequency;
                        if (elapsedSec >= 0.20)
                        {
                            double recentBps = ((cumulativeBytesDelivered - lastBytesCount) * 8.0) / elapsedSec;
                            lastReportTicks = nowTicks;
                            lastBytesCount = cumulativeBytesDelivered;

                            TimeSpan duration = TimeSpan.FromSeconds((double)(nowTicks - startTicks) / Stopwatch.Frequency);
                            UpdateStats(new StreamTestStatistics
                            {
                                StartedAt = startedAt,
                                Duration = duration,
                                TargetBitsPerSecond = request.TargetBitsPerSecond,
                                ActualBitsPerSecond = recentBps,
                                BytesTransferred = cumulativeBytesDelivered,
                                ReconnectCount = reconnectCount,
                                CurrentInterruption = null,
                                LastInterruption = lastInterruption,
                                LongestInterruption = longestInterruption
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested) break;

                disconnectTicks = Stopwatch.GetTimestamp();
                reconnectCount++;

                State = StreamTestState.Reconnecting;
                _eventLog.Add(new StreamRuntimeEvent { Type = StreamRuntimeEventType.TcpLost, Message = $"TCP connection lost: {ex.Message}" });

                stream?.Dispose();
                socket?.Dispose();

                // Live increasing CurrentInterruption while reconnecting
                int retryDelayMs = reconnectCount == 1 ? 50 : 300;
                long delayStartTicks = Stopwatch.GetTimestamp();
                while (Stopwatch.GetTimestamp() - delayStartTicks < (retryDelayMs * Stopwatch.Frequency / 1000) && !cancellationToken.IsCancellationRequested)
                {
                    long nowTicks = Stopwatch.GetTimestamp();
                    TimeSpan currentInterrupt = TimeSpan.FromSeconds((double)(nowTicks - disconnectTicks) / Stopwatch.Frequency);
                    lock (_stateLock)
                    {
                        _currentStats = new StreamTestStatistics
                        {
                            StartedAt = _currentStats.StartedAt,
                            Duration = TimeSpan.FromSeconds((double)(nowTicks - startTicks) / Stopwatch.Frequency),
                            TargetBitsPerSecond = request.TargetBitsPerSecond,
                            ActualBitsPerSecond = 0,
                            BytesTransferred = cumulativeBytesDelivered,
                            ReconnectCount = reconnectCount,
                            CurrentInterruption = currentInterrupt,
                            LastInterruption = lastInterruption,
                            LongestInterruption = longestInterruption
                        };
                    }
                    ProgressChanged?.Invoke(this, new StreamTestProgressEventArgs(CurrentStatistics));
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                stream?.Dispose();
                socket?.Dispose();
            }
        }
    }

    private async Task RunUdpClientAsync(StreamTestRequest request, CancellationToken cancellationToken)
    {
        var localIp = SelectedAdapterSocketFactory.ParseOrResolveLocalIPv4(request.LocalIpAddress);
        var targetIp = await SelectedAdapterSocketFactory.ResolveTargetIPv4Async(request.ServerHost, cancellationToken).ConfigureAwait(false);

        DateTimeOffset startedAt = DateTimeOffset.Now;
        long startTicks = Stopwatch.GetTimestamp();
        TimeSpan? lastInterruption = null;
        TimeSpan? longestInterruption = null;

        using var socket = SelectedAdapterSocketFactory.CreateBoundUdpSocket(localIp);
        EndPoint remoteEp = new IPEndPoint(targetIp, request.Port);

        Guid logicalSessionId = Guid.NewGuid();

        // Send HELLO
        var helloMsg = new HelloMessage
        {
            SessionId = logicalSessionId,
            Protocol = StreamTestProtocol.Udp,
            Direction = request.Direction,
            TargetBitsPerSecond = request.TargetBitsPerSecond
        };

        byte[] helloBytes = StreamProtocolSerializer.SerializeHello(helloMsg);
        await socket.SendToAsync(helloBytes, SocketFlags.None, remoteEp, cancellationToken).ConfigureAwait(false);

        // Wait for READY response with 3s timeout
        byte[] readyBuf = new byte[128];
        bool readyReceived = false;
        long readyDeadline = Stopwatch.GetTimestamp() + (Stopwatch.Frequency * 3);

        while (Stopwatch.GetTimestamp() < readyDeadline && !cancellationToken.IsCancellationRequested)
        {
            if (socket.Available > 0)
            {
                var r = await socket.ReceiveAsync(readyBuf, SocketFlags.None, cancellationToken).ConfigureAwait(false);
                if (r >= StreamProtocolConstants.HeaderSize && readyBuf[6] == (byte)MessageType.Ready)
                {
                    if (StreamProtocolSerializer.TryDeserializeReadyPayload(readyBuf.AsSpan(StreamProtocolConstants.HeaderSize, r - StreamProtocolConstants.HeaderSize), out var ready) && ready != null && ready.SessionId == logicalSessionId)
                    {
                        readyReceived = true;
                        break;
                    }
                }
            }
            await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        if (!readyReceived)
        {
            State = StreamTestState.ServerLost;
            _eventLog.Add(new StreamRuntimeEvent { Type = StreamRuntimeEventType.ServerLost, Message = "UDP Server lost — no response to HELLO" });
        }
        else
        {
            State = StreamTestState.Active;
            _eventLog.Add(new StreamRuntimeEvent { Type = StreamRuntimeEventType.Started, Message = "UDP Session Active" });
        }

        var seqTracker = new UdpSequenceTracker();
        var jitterCalc = new JitterCalculator();
        var rateLimiter = new TokenBucketRateLimiter(request.TargetBitsPerSecond);

        long packetsSent = 0;
        long totalDataBytesTransferred = 0;
        long lastResponseTicks = Stopwatch.GetTimestamp();
        long interruptionStartTicks = 0;
        long lastReportTicks = Stopwatch.GetTimestamp();
        long lastBytesCount = 0;
        long lastProbeTicks = Stopwatch.GetTimestamp();

        StatReportMessage? latestServerStatReport = null;

        // Background receive loop for UDP
        _ = Task.Run(async () =>
        {
            byte[] recvBuf = new byte[2048];
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var result = await socket.ReceiveFromAsync(recvBuf, SocketFlags.None, remoteEp, cancellationToken).ConfigureAwait(false);
                    int len = result.ReceivedBytes;
                    if (len < StreamProtocolConstants.HeaderSize) continue;

                    byte msgType = recvBuf[6];
                    long nowTicks = Stopwatch.GetTimestamp();
                    lastResponseTicks = nowTicks;

                    if (msgType == (byte)MessageType.Data)
                    {
                        if (StreamProtocolSerializer.TryDeserializeDataPayload(recvBuf.AsSpan(StreamProtocolConstants.HeaderSize, len - StreamProtocolConstants.HeaderSize), out var dataMsg) && dataMsg != null && dataMsg.SessionId == logicalSessionId)
                        {
                            long nowMicro = (nowTicks - startTicks) * 1_000_000 / Stopwatch.Frequency;
                            seqTracker.ProcessPacket(dataMsg.SequenceNumber, nowTicks);
                            jitterCalc.AddSample(dataMsg.SendTimestampMicroseconds, nowMicro);
                            Interlocked.Add(ref totalDataBytesTransferred, dataMsg.Payload.Length);
                        }
                    }
                    else if (msgType == (byte)MessageType.StatReport)
                    {
                        if (StreamProtocolSerializer.TryDeserializeStatReportPayload(recvBuf.AsSpan(StreamProtocolConstants.HeaderSize, len - StreamProtocolConstants.HeaderSize), out var statMsg) && statMsg != null && statMsg.SessionId == logicalSessionId)
                        {
                            latestServerStatReport = statMsg;
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }, cancellationToken);

        // Background stat report loop for RECEIVE mode
        if (request.Direction == StreamTestDirection.Receive)
        {
            _ = Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(300, cancellationToken).ConfigureAwait(false);
                    var statMsg = new StatReportMessage
                    {
                        SessionId = logicalSessionId,
                        HighestSequenceReceived = seqTracker.ExpectedPackets,
                        UniquePacketsReceived = seqTracker.UniquePacketsReceived,
                        FinalizedPacketsLost = seqTracker.FinalizedPacketsLost,
                        DuplicatePackets = seqTracker.DuplicatePackets,
                        OutOfOrderPackets = seqTracker.OutOfOrderPackets,
                        BytesReceived = Interlocked.Read(ref totalDataBytesTransferred),
                        JitterMs = jitterCalc.CurrentJitterMs,
                        LongestGapMs = seqTracker.LongestGap.TotalMilliseconds
                    };
                    byte[] reportBytes = StreamProtocolSerializer.SerializeStatReport(statMsg);
                    try { await socket.SendToAsync(reportBytes, SocketFlags.None, remoteEp, cancellationToken).ConfigureAwait(false); } catch { }
                }
            }, cancellationToken);
        }

        // Main UDP traffic & pacing loop
        long sequence = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            long nowTicks = Stopwatch.GetTimestamp();

            // Evaluate remote peer liveness & generic interruption timing (Correction 9)
            double secondsSinceLastResponse = (double)(nowTicks - lastResponseTicks) / Stopwatch.Frequency;
            TimeSpan? currentInterruption = null;

            if (secondsSinceLastResponse > 5.0 && State != StreamTestState.ServerLost)
            {
                if (interruptionStartTicks == 0) interruptionStartTicks = nowTicks;
                State = StreamTestState.ServerLost;
                _eventLog.Add(new StreamRuntimeEvent { Type = StreamRuntimeEventType.ServerLost, Message = "UDP Server Lost (>5s no response)" });
            }
            else if (secondsSinceLastResponse > 2.0 && secondsSinceLastResponse <= 5.0 && State != StreamTestState.NoResponse && State != StreamTestState.ServerLost)
            {
                if (interruptionStartTicks == 0) interruptionStartTicks = nowTicks;
                State = StreamTestState.NoResponse;
                _eventLog.Add(new StreamRuntimeEvent { Type = StreamRuntimeEventType.UdpNoResponse, Message = "UDP No Response (>2s gap)" });
            }
            else if (secondsSinceLastResponse <= 2.0 && (State == StreamTestState.NoResponse || State == StreamTestState.ServerLost))
            {
                if (interruptionStartTicks > 0)
                {
                    TimeSpan duration = TimeSpan.FromSeconds((double)(nowTicks - interruptionStartTicks) / Stopwatch.Frequency);
                    lastInterruption = duration;
                    if (longestInterruption == null || duration > longestInterruption)
                    {
                        longestInterruption = duration;
                    }
                    interruptionStartTicks = 0;
                }
                State = StreamTestState.Active;
                _eventLog.Add(new StreamRuntimeEvent { Type = StreamRuntimeEventType.UdpRecovered, Message = "UDP Connection Recovered" });
            }

            if (interruptionStartTicks > 0)
            {
                currentInterruption = TimeSpan.FromSeconds((double)(nowTicks - interruptionStartTicks) / Stopwatch.Frequency);
            }

            // Continuous HELLO recovery probes when NO RESPONSE / SERVER LOST (Correction 8 & 10)
            if (State is StreamTestState.NoResponse or StreamTestState.ServerLost)
            {
                double secondsSinceLastProbe = (double)(nowTicks - lastProbeTicks) / Stopwatch.Frequency;
                if (secondsSinceLastProbe >= 0.50)
                {
                    lastProbeTicks = nowTicks;
                    var recoveryHello = new HelloMessage
                    {
                        SessionId = logicalSessionId,
                        Protocol = StreamTestProtocol.Udp,
                        Direction = request.Direction,
                        TargetBitsPerSecond = request.TargetBitsPerSecond,
                        IsReconnect = true
                    };
                    byte[] probeBytes = StreamProtocolSerializer.SerializeHello(recoveryHello);
                    try { await socket.SendToAsync(probeBytes, SocketFlags.None, remoteEp, cancellationToken).ConfigureAwait(false); } catch { }
                }
            }

            if (request.Direction == StreamTestDirection.Send)
            {
                sequence++;
                long sendMicro = (nowTicks - startTicks) * 1_000_000 / Stopwatch.Frequency;
                int dataPayloadSize = StreamProtocolConstants.DefaultUdpPayloadSize - 32 - StreamProtocolConstants.HeaderSize;
                var dataMsg = new DataMessage
                {
                    SessionId = logicalSessionId,
                    SequenceNumber = sequence,
                    SendTimestampMicroseconds = sendMicro,
                    Payload = new byte[dataPayloadSize]
                };

                byte[] packet = StreamProtocolSerializer.SerializeData(dataMsg, StreamProtocolConstants.DefaultUdpPayloadSize);

                // Rate limiting applies ONLY to generated DATA payload bytes (Correction 3)
                await rateLimiter.PaceAsync(dataPayloadSize, cancellationToken).ConfigureAwait(false);
                try
                {
                    await socket.SendToAsync(packet, SocketFlags.None, remoteEp, cancellationToken).ConfigureAwait(false);
                    packetsSent++;
                    Interlocked.Add(ref totalDataBytesTransferred, dataPayloadSize);
                }
                catch { }
            }
            else
            {
                await Task.Delay(20, cancellationToken).ConfigureAwait(false);
            }

            double elapsedSec = (double)(nowTicks - lastReportTicks) / Stopwatch.Frequency;
            if (elapsedSec >= 0.20)
            {
                long currentBytes = Interlocked.Read(ref totalDataBytesTransferred);
                double recentBps = ((currentBytes - lastBytesCount) * 8.0) / elapsedSec;
                lastReportTicks = nowTicks;
                lastBytesCount = currentBytes;

                TimeSpan duration = TimeSpan.FromSeconds((double)(nowTicks - startTicks) / Stopwatch.Frequency);

                long recvdPackets = request.Direction == StreamTestDirection.Receive ? seqTracker.UniquePacketsReceived : (latestServerStatReport?.UniquePacketsReceived ?? 0);
                long lostPackets = request.Direction == StreamTestDirection.Receive ? seqTracker.FinalizedPacketsLost : (latestServerStatReport?.FinalizedPacketsLost ?? 0);
                long dupPackets = request.Direction == StreamTestDirection.Receive ? seqTracker.DuplicatePackets : (latestServerStatReport?.DuplicatePackets ?? 0);
                long outOfOrderPackets = request.Direction == StreamTestDirection.Receive ? seqTracker.OutOfOrderPackets : (latestServerStatReport?.OutOfOrderPackets ?? 0);
                double jitter = request.Direction == StreamTestDirection.Receive ? jitterCalc.CurrentJitterMs : (latestServerStatReport?.JitterMs ?? 0);
                TimeSpan gap = request.Direction == StreamTestDirection.Receive ? seqTracker.LongestGap : TimeSpan.FromMilliseconds(latestServerStatReport?.LongestGapMs ?? 0);

                double? lossPct = (recvdPackets + lostPackets) > 0 ? ((double)lostPackets / (recvdPackets + lostPackets) * 100.0) : 0.0;

                UpdateStats(new StreamTestStatistics
                {
                    StartedAt = startedAt,
                    Duration = duration,
                    TargetBitsPerSecond = request.TargetBitsPerSecond,
                    ActualBitsPerSecond = recentBps,
                    BytesTransferred = currentBytes,
                    PacketsSent = packetsSent,
                    PacketsReceived = recvdPackets,
                    PacketsLost = lostPackets,
                    DuplicatePackets = dupPackets,
                    OutOfOrderPackets = outOfOrderPackets,
                    LossPercent = lossPct,
                    JitterMs = jitter,
                    LongestGap = gap,
                    CurrentInterruption = currentInterruption,
                    LastInterruption = lastInterruption,
                    LongestInterruption = longestInterruption
                });
            }
        }
    }

    private void UpdateStats(StreamTestStatistics stats)
    {
        lock (_stateLock)
        {
            _currentStats = stats;
        }
        ProgressChanged?.Invoke(this, new StreamTestProgressEventArgs(stats));
    }
}
