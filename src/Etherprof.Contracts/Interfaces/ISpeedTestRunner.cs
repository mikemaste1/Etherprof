namespace Etherprof.Contracts.Interfaces;

using Etherprof.Contracts.Models;

public class SpeedTestProgressEventArgs : EventArgs
{
    public SpeedTestProgress Progress { get; }

    public SpeedTestProgressEventArgs(SpeedTestProgress progress)
    {
        Progress = progress;
    }
}

public class SpeedTestCompletedEventArgs : EventArgs
{
    public SpeedTestResult Result { get; }

    public SpeedTestCompletedEventArgs(SpeedTestResult result)
    {
        Result = result;
    }
}

public interface ISpeedTestRunner
{
    bool IsRunning { get; }

    SpeedTestDirection? ActiveDirection { get; }

    event EventHandler<SpeedTestProgressEventArgs>? ProgressChanged;
    event EventHandler<SpeedTestCompletedEventArgs>? Completed;

    Task<SpeedTestResult> StartAsync(
        SpeedTestDirection direction,
        long transferBytes,
        string adapterId,
        CancellationToken cancellationToken);

    void Stop();
}
