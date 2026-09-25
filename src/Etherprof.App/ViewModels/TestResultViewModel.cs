using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Etherprof.Contracts.Models;

namespace Etherprof.App.ViewModels;

public sealed class PingTickItem
{
    public bool IsSuccess { get; init; }
    public double LatencyMs { get; init; }
    public string ToolTipText { get; init; } = "";
    public string Brush => IsSuccess ? "#16A34A" : "#DC2626"; // Rich green or vivid red
    public double BarHeight => IsSuccess ? Math.Clamp(Math.Max(3.0, (LatencyMs / 100.0) * 16.0), 3.0, 16.0) : 16.0;
}

public sealed class TestResultViewModel : INotifyPropertyChanged
{
    public Guid TargetId { get; init; }
    public string TargetName { get; init; } = "";

    private TestStatus _status = TestStatus.Untested;
    public TestStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusSymbol));
                OnPropertyChanged(nameof(StatusBrush));
                OnPropertyChanged(nameof(StatusBadgeText));
            }
        }
    }

    private string _latencyText = "";
    public string LatencyText
    {
        get => _latencyText;
        set { if (_latencyText != value) { _latencyText = value; OnPropertyChanged(); } }
    }

    public string StatusSymbol => Status switch
    {
        TestStatus.Success => "●",
        TestStatus.Failure => "✕",
        TestStatus.Testing => "◌",
        _ => "○"
    };

    public string StatusBrush => Status switch
    {
        TestStatus.Success => "#16A34A",  // Rich dominant green
        TestStatus.Failure => "#DC2626",  // Vivid dominant red
        TestStatus.Testing => "#2563EB",  // Blue
        _ => "#64748B"                   // Slate / Gray
    };

    public string StatusBadgeText => Status switch
    {
        TestStatus.Success => "OK",
        TestStatus.Failure => "FAIL",
        TestStatus.Testing => "...",
        _ => "IDLE"
    };

    public ObservableCollection<PingTickItem> HistoryTicks { get; } = new();
    private const int MaxHistoryTicks = 22;

    public void AddHistoryTick(TestResult result)
    {
        if (result.Status is TestStatus.Success or TestStatus.Failure)
        {
            var item = new PingTickItem
            {
                IsSuccess = result.Status == TestStatus.Success,
                LatencyMs = result.Latency?.TotalMilliseconds ?? 0,
                ToolTipText = result.Status == TestStatus.Success
                    ? $"{result.Latency?.TotalMilliseconds:0} ms"
                    : result.Error ?? "Failed / Timeout"
            };
            HistoryTicks.Add(item);
            while (HistoryTicks.Count > MaxHistoryTicks)
            {
                HistoryTicks.RemoveAt(0);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
