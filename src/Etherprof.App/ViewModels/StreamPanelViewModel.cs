namespace Etherprof.App.ViewModels;

using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Windows.Input;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.StreamTest.Abstractions;
using Etherprof.StreamTest.Client;
using Etherprof.StreamTest.Models;
using Microsoft.Extensions.Logging;

public sealed class StreamPanelViewModel : ViewModelBase
{
    private readonly IStreamTestClient _streamClient;
    private readonly WifiPanelViewModel _wifiPanel;
    private readonly ISettingsRepository _settingsRepo;
    private readonly AppSettings _settings;
    private readonly ILogger? _logger;

    private string _serverHost = "192.168.1.40";
    private int _serverPort = 49100;
    private StreamTestProtocol _protocol = StreamTestProtocol.Tcp;
    private StreamTestDirection _direction = StreamTestDirection.Send;
    private double _rateValue = 2.0;
    private string _rateUnit = "Mbps"; // "Kbps" or "Mbps"

    private StreamTestState _state = StreamTestState.Idle;
    private StreamTestStatistics _stats = new();
    private string? _statusMessage;
    private string? _currentAdapterIp;

    public StreamPanelViewModel(
        IStreamTestClient streamClient,
        WifiPanelViewModel wifiPanel,
        ISettingsRepository settingsRepo,
        AppSettings settings,
        ILogger<StreamPanelViewModel>? logger = null)
    {
        _streamClient = streamClient;
        _wifiPanel = wifiPanel;
        _settingsRepo = settingsRepo;
        _settings = settings;
        _logger = logger;

        // Restore settings
        if (!string.IsNullOrWhiteSpace(settings.StreamServerHost))
            _serverHost = settings.StreamServerHost;
        if (settings.StreamPort > 0)
            _serverPort = settings.StreamPort;
        if (Enum.TryParse<StreamTestProtocol>(settings.StreamProtocol, true, out var p))
            _protocol = p;
        if (Enum.TryParse<StreamTestDirection>(settings.StreamDirection, true, out var d))
            _direction = d;

        if (settings.StreamTargetBitrate >= 1_000_000)
        {
            _rateValue = settings.StreamTargetBitrate / 1_000_000.0;
            _rateUnit = "Mbps";
        }
        else
        {
            _rateValue = settings.StreamTargetBitrate / 1_000.0;
            _rateUnit = "Kbps";
        }

        // Commands
        StartCommand = new RelayCommand(async _ => await StartStreamAsync(), _ => CanStartStream);
        StopCommand = new RelayCommand(async _ => await StopStreamAsync(), _ => IsRunning);
        SetProtocolCommand = new RelayCommand(p => SetProtocol((string)p!));
        SetDirectionCommand = new RelayCommand(d => SetDirection((string)d!));

        // Event handlers
        _streamClient.ProgressChanged += OnClientProgressChanged;
        _streamClient.StateChanged += OnClientStateChanged;

        _wifiPanel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(WifiPanelViewModel.Bssid))
            {
                OnRoamDetected();
            }
        };
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand SetProtocolCommand { get; }
    public ICommand SetDirectionCommand { get; }

    public ObservableCollection<StreamRuntimeEvent> EventLog { get; } = new();

    public string ServerHost
    {
        get => _serverHost;
        set
        {
            if (SetProperty(ref _serverHost, value)) SaveSettings();
        }
    }

    public int ServerPort
    {
        get => _serverPort;
        set
        {
            if (SetProperty(ref _serverPort, value)) SaveSettings();
        }
    }

    public bool IsTcp
    {
        get => _protocol == StreamTestProtocol.Tcp;
        set { if (value) SetProtocol("Tcp"); }
    }

    public bool IsUdp
    {
        get => _protocol == StreamTestProtocol.Udp;
        set { if (value) SetProtocol("Udp"); }
    }

    public bool IsSend
    {
        get => _direction == StreamTestDirection.Send;
        set { if (value) SetDirection("Send"); }
    }

    public bool IsReceive
    {
        get => _direction == StreamTestDirection.Receive;
        set { if (value) SetDirection("Receive"); }
    }

    public double RateValue
    {
        get => _rateValue;
        set
        {
            if (SetProperty(ref _rateValue, value)) SaveSettings();
        }
    }

    public string RateUnit
    {
        get => _rateUnit;
        set
        {
            if (SetProperty(ref _rateUnit, value)) SaveSettings();
        }
    }

    public long TargetBitsPerSecond
    {
        get
        {
            if (RateUnit == "Mbps") return (long)(RateValue * 1_000_000);
            return (long)(RateValue * 1_000);
        }
    }

    public bool IsRunning => _streamClient.IsRunning || State is StreamTestState.Connecting or StreamTestState.Active or StreamTestState.Reconnecting or StreamTestState.NoResponse;
    public bool IsIdle => !IsRunning;

    public bool CanStartStream => !string.IsNullOrWhiteSpace(ServerHost) && TargetBitsPerSecond >= StreamTestRequest.MinTargetBitsPerSecond && TargetBitsPerSecond <= StreamTestRequest.MaxTargetBitsPerSecond;

    public StreamTestState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value))
            {
                OnPropertyChanged(nameof(IsRunning));
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(StatusSymbol));
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(StatusBrush));
            }
        }
    }

    public string StatusSymbol => State switch
    {
        StreamTestState.Active => "●",
        StreamTestState.Connecting => "◌",
        StreamTestState.Reconnecting => "◐",
        StreamTestState.NoResponse => "◐",
        StreamTestState.ServerLost => "✕",
        StreamTestState.Failed => "✕",
        _ => "○"
    };

    public string StatusText => State switch
    {
        StreamTestState.Active => _protocol == StreamTestProtocol.Tcp ? "CONNECTED" : (_direction == StreamTestDirection.Receive ? "RECEIVING" : "ACTIVE"),
        StreamTestState.Connecting => "CONNECTING",
        StreamTestState.Reconnecting => "RECONNECTING",
        StreamTestState.NoResponse => "NO RESPONSE",
        StreamTestState.ServerLost => "SERVER LOST",
        StreamTestState.Failed => "FAILED",
        StreamTestState.Stopping => "STOPPING",
        _ => "IDLE"
    };

    public string StatusBrush => State switch
    {
        StreamTestState.Active => "#16A34A",       // Green
        StreamTestState.Connecting => "#2563EB",   // Blue
        StreamTestState.Reconnecting => "#D97706", // Amber
        StreamTestState.NoResponse => "#D97706",   // Amber
        StreamTestState.ServerLost => "#DC2626",   // Red
        StreamTestState.Failed => "#DC2626",       // Red
        _ => "#6B7280"
    };

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string TargetRateDisplay => $"{RateValue:F2} {RateUnit}";
    public string ActualRateDisplay => $"{_stats.ActualBitsPerSecond / 1_000_000.0:F2} Mbps";
    public string DurationDisplay => $"{_stats.Duration:hh\\:mm\\:ss}";
    public string LossDisplay => $"{_stats.LossPercent ?? 0.0:F2}%";
    public string JitterDisplay => $"{_stats.JitterMs ?? 0.0:F1} ms";
    public string LongestGapDisplay => $"{_stats.LongestGap?.TotalMilliseconds ?? 0:F0} ms";
    public int ReconnectCountDisplay => _stats.ReconnectCount;
    public string CurrentInterruptionDisplay => _stats.CurrentInterruption.HasValue ? $"{_stats.CurrentInterruption.Value.TotalSeconds:F2} s" : "-";
    public string LastInterruptionDisplay => _stats.LastInterruption.HasValue ? $"{_stats.LastInterruption.Value.TotalSeconds:F2} s" : "-";
    public string LongestInterruptionDisplay => _stats.LongestInterruption.HasValue ? $"{_stats.LongestInterruption.Value.TotalSeconds:F2} s" : "-";

    public WifiPanelViewModel WifiPanel => _wifiPanel;

    public void SetAdapterContext(string? localIpAddress)
    {
        _currentAdapterIp = localIpAddress;
        if (IsRunning && string.IsNullOrWhiteSpace(localIpAddress))
        {
            StatusMessage = "Selected adapter has no usable IPv4 address.";
            _ = StopStreamAsync();
        }
    }

    public async Task StartStreamAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentAdapterIp))
        {
            StatusMessage = "Cannot start stream test. Selected adapter has no usable IPv4 address.";
            return;
        }

        StatusMessage = null;

        var request = new StreamTestRequest
        {
            ServerHost = ServerHost,
            Port = ServerPort,
            Protocol = _protocol,
            Direction = _direction,
            TargetBitsPerSecond = TargetBitsPerSecond,
            LocalIpAddress = _currentAdapterIp
        };

        try
        {
            await _streamClient.StartAsync(request);
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    public async Task StopStreamAsync()
    {
        await _streamClient.StopAsync();
    }

    private void SetProtocol(string protocolName)
    {
        if (Enum.TryParse<StreamTestProtocol>(protocolName, true, out var p))
        {
            _protocol = p;
            OnPropertyChanged(nameof(IsTcp));
            OnPropertyChanged(nameof(IsUdp));
            SaveSettings();
        }
    }

    private void SetDirection(string directionName)
    {
        if (Enum.TryParse<StreamTestDirection>(directionName, true, out var d))
        {
            _direction = d;
            OnPropertyChanged(nameof(IsSend));
            OnPropertyChanged(nameof(IsReceive));
            SaveSettings();
        }
    }

    private void OnClientProgressChanged(object? sender, StreamTestProgressEventArgs e)
    {
        _stats = e.Statistics;
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(nameof(TargetRateDisplay));
            OnPropertyChanged(nameof(ActualRateDisplay));
            OnPropertyChanged(nameof(DurationDisplay));
            OnPropertyChanged(nameof(LossDisplay));
            OnPropertyChanged(nameof(JitterDisplay));
            OnPropertyChanged(nameof(LongestGapDisplay));
            OnPropertyChanged(nameof(ReconnectCountDisplay));
            OnPropertyChanged(nameof(CurrentInterruptionDisplay));
            OnPropertyChanged(nameof(LastInterruptionDisplay));
            OnPropertyChanged(nameof(LongestInterruptionDisplay));
            UpdateEventLogUI();
        });
    }

    private void OnClientStateChanged(object? sender, StreamTestStateChangedEventArgs e)
    {
        App.Current?.Dispatcher.InvokeAsync(() =>
        {
            State = e.NewState;
            UpdateEventLogUI();
        });
    }

    private void OnRoamDetected()
    {
        if (IsRunning && _streamClient is StreamTestClient client)
        {
            client.AddEventLog(new StreamRuntimeEvent
            {
                Type = StreamRuntimeEventType.Roam,
                Message = $"ROAM → {_wifiPanel.CurrentBssidAlias ?? _wifiPanel.Bssid}",
                CurrentBssid = _wifiPanel.Bssid
            });
            App.Current?.Dispatcher.InvokeAsync(UpdateEventLogUI);
        }
    }

    private void UpdateEventLogUI()
    {
        EventLog.Clear();
        foreach (var evt in _streamClient.EventLog)
        {
            EventLog.Add(evt);
        }
    }

    private void SaveSettings()
    {
        _settings.StreamServerHost = ServerHost;
        _settings.StreamPort = ServerPort;
        _settings.StreamProtocol = _protocol.ToString();
        _settings.StreamDirection = _direction.ToString();
        _settings.StreamTargetBitrate = TargetBitsPerSecond;
        _ = _settingsRepo.SaveAsync(_settings);
    }
}
