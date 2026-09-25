namespace Etherprof.App.ViewModels;

using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.Core;
using Microsoft.Extensions.Logging;

public sealed class SpeedTestPanelViewModel : INotifyPropertyChangedHelper
{
    private readonly ISpeedTestRunner _runner;
    private readonly WifiPanelViewModel _wifiPanel;
    private readonly ILogger<SpeedTestPanelViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    private long _selectedTransferBytes = SpeedCalculationHelper.Size10MiB; // Default 10 MiB
    private bool _isRunning;
    private SpeedTestDirection? _activeDirection;
    private double _currentMbps;
    private long _bytesTransferred;
    private long _totalBytes;
    private double _percentComplete;

    private string _statusText = "";
    private string _progressDetailText = "";
    private string _activeAdapterId = "";

    public long SelectedTransferBytes
    {
        get => _selectedTransferBytes;
        set
        {
            _selectedTransferBytes = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsSize1MiB));
            OnPropertyChanged(nameof(IsSize10MiB));
            OnPropertyChanged(nameof(IsSize100MiB));
        }
    }

    public bool IsSize1MiB => SelectedTransferBytes == SpeedCalculationHelper.Size1MiB;
    public bool IsSize10MiB => SelectedTransferBytes == SpeedCalculationHelper.Size10MiB;
    public bool IsSize100MiB => SelectedTransferBytes == SpeedCalculationHelper.Size100MiB;

    public bool IsRunning
    {
        get => _isRunning;
        private set { _isRunning = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); }
    }

    public bool IsIdle => !IsRunning;

    public SpeedTestDirection? ActiveDirection
    {
        get => _activeDirection;
        private set
        {
            _activeDirection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDownloading));
            OnPropertyChanged(nameof(IsUploading));
        }
    }

    public bool IsDownloading => IsRunning && ActiveDirection == SpeedTestDirection.Download;
    public bool IsUploading => IsRunning && ActiveDirection == SpeedTestDirection.Upload;

    public double CurrentMbps
    {
        get => _currentMbps;
        private set { _currentMbps = value; OnPropertyChanged(); }
    }

    public long BytesTransferred
    {
        get => _bytesTransferred;
        private set { _bytesTransferred = value; OnPropertyChanged(); }
    }

    public long TotalBytes
    {
        get => _totalBytes;
        private set { _totalBytes = value; OnPropertyChanged(); }
    }

    public double PercentComplete
    {
        get => _percentComplete;
        private set { _percentComplete = value; OnPropertyChanged(); }
    }

    public string StatusText
    {
        get => _statusText;
        private set { _statusText = value; OnPropertyChanged(); }
    }

    public string ProgressDetailText
    {
        get => _progressDetailText;
        private set { _progressDetailText = value; OnPropertyChanged(); }
    }

    public ObservableCollection<SpeedTestResultViewModel> RecentResults { get; } = new();

    public ICommand SelectSizeCommand { get; }
    public ICommand RunDownloadCommand { get; }
    public ICommand RunUploadCommand { get; }
    public ICommand StopCommand { get; }

    public SpeedTestPanelViewModel(
        ISpeedTestRunner runner,
        WifiPanelViewModel wifiPanel,
        ILogger<SpeedTestPanelViewModel> logger)
    {
        _runner = runner;
        _wifiPanel = wifiPanel;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;

        SelectSizeCommand = new RelayCommand(p =>
        {
            if (p is string s && long.TryParse(s, out var bytes))
                SelectedTransferBytes = bytes;
            else if (p is long l)
                SelectedTransferBytes = l;
        });

        RunDownloadCommand = new RelayCommand(_ => _ = StartOrToggleTestAsync(SpeedTestDirection.Download));
        RunUploadCommand = new RelayCommand(_ => _ = StartOrToggleTestAsync(SpeedTestDirection.Upload));
        StopCommand = new RelayCommand(_ => StopTest());

        _runner.ProgressChanged += OnProgressChanged;
        _runner.Completed += OnTestCompleted;
    }

    public void SetActiveAdapterId(string adapterId)
    {
        if (_activeAdapterId != adapterId && IsRunning)
        {
            _logger.LogInformation("Selected adapter changed while speedtest running — cancelling test");
            StopTest();
        }
        _activeAdapterId = adapterId;
    }

    public void OnNetworkProfileChanged()
    {
        if (IsRunning)
        {
            _logger.LogInformation("Network profile changed while speedtest running — cancelling test");
            StopTest();
        }
    }

    private async Task StartOrToggleTestAsync(SpeedTestDirection direction)
    {
        if (IsRunning)
        {
            if (ActiveDirection == direction)
            {
                // Clicking active direction button acts as STOP
                StopTest();
                return;
            }

            // Switching direction: cancel current test first
            StopTest();
            await Task.Delay(100);
        }

        IsRunning = true;
        ActiveDirection = direction;
        CurrentMbps = 0;
        BytesTransferred = 0;
        TotalBytes = SelectedTransferBytes;
        PercentComplete = 0;

        string dirSymbol = direction == SpeedTestDirection.Download ? "↓" : "↑";
        StatusText = $"{dirSymbol} Starting...";
        ProgressDetailText = $"0.0 / {SpeedCalculationHelper.FormatSizeMiB(SelectedTransferBytes)}";

        _ = Task.Run(async () =>
        {
            try
            {
                await _runner.StartAsync(direction, SelectedTransferBytes, _activeAdapterId, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled error starting speedtest");
            }
        });
    }

    private void StopTest()
    {
        _runner.Stop();
    }

    private void OnProgressChanged(object? sender, SpeedTestProgressEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            BytesTransferred = e.Progress.BytesTransferred;
            TotalBytes = e.Progress.TotalBytes;
            CurrentMbps = e.Progress.CurrentMbps;
            PercentComplete = e.Progress.PercentComplete;

            string dirSymbol = ActiveDirection == SpeedTestDirection.Download ? "↓" : "↑";
            StatusText = $"{dirSymbol} {SpeedCalculationHelper.FormatMbps(e.Progress.CurrentMbps)}";

            double transferredMiB = e.Progress.BytesTransferred / (1024.0 * 1024.0);
            double totalMiB = e.Progress.TotalBytes / (1024.0 * 1024.0);
            ProgressDetailText = $"{transferredMiB:F1} / {totalMiB:F1} MiB";
        });
    }

    public event EventHandler<SpeedTestResultViewModel>? TestCompleted;

    private void OnTestCompleted(object? sender, SpeedTestCompletedEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsRunning = false;
            ActiveDirection = null;

            var res = e.Result;
            string dirSymbol = res.Direction == SpeedTestDirection.Download ? "DOWNLOAD" : "UPLOAD";

            if (res.Cancelled)
            {
                StatusText = $"{dirSymbol}";
                ProgressDetailText = "Cancelled";
            }
            else if (!res.Success)
            {
                StatusText = $"{dirSymbol}";
                ProgressDetailText = $"✕ {res.Error ?? "Failed"}";
            }
            else
            {
                StatusText = $"{dirSymbol}";
                ProgressDetailText = $"✓ {SpeedCalculationHelper.FormatMbps(res.AverageMbps)}  ({SpeedCalculationHelper.FormatSizeMiB(res.BytesTransferred)} · {res.Elapsed.TotalSeconds:F2} s)";

                // Add to recent results collection
                var vm = new SpeedTestResultViewModel(res, _wifiPanel.GetAlias);
                RecentResults.Insert(0, vm); // Newest first

                while (RecentResults.Count > BoundedSpeedTestResultLog.Capacity)
                {
                    RecentResults.RemoveAt(RecentResults.Count - 1);
                }

                TestCompleted?.Invoke(this, vm);
            }
        });
    }
}
