using System.ComponentModel;
using System.Runtime.CompilerServices;
using Etherprof.Contracts.Models;

namespace Etherprof.App.ViewModels;

public sealed class TestResultViewModel : INotifyPropertyChanged
{
    public Guid TargetId { get; init; }
    public string TargetName { get; init; } = "";

    private TestStatus _status = TestStatus.Untested;
    public TestStatus Status
    {
        get => _status;
        set { if (_status != value) { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusSymbol)); } }
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

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
