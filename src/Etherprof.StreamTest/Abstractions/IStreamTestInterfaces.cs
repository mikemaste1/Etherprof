namespace Etherprof.StreamTest.Abstractions;

using Etherprof.StreamTest.Models;

public interface IStreamTestClient : IAsyncDisposable
{
    bool IsRunning { get; }

    StreamTestState State { get; }

    StreamTestStatistics CurrentStatistics { get; }

    IReadOnlyList<StreamRuntimeEvent> EventLog { get; }

    event EventHandler<StreamTestProgressEventArgs>? ProgressChanged;
    event EventHandler<StreamTestStateChangedEventArgs>? StateChanged;

    Task StartAsync(StreamTestRequest request, CancellationToken cancellationToken = default);

    Task StopAsync();
}

public interface IStreamTestServer : IAsyncDisposable
{
    bool IsRunning { get; }

    int ActiveClientCount { get; }

    int BoundPort { get; }

    event EventHandler? ActiveClientsChanged;

    Task StartAsync(int port = 49100, CancellationToken cancellationToken = default);

    Task StopAsync();
}
