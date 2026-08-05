namespace Etherprof.App.ViewModels;

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows.Input;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.StreamTest.Abstractions;
using Microsoft.Extensions.Logging;

public sealed class StreamServerPanelViewModel : ViewModelBase
{
    private readonly IStreamTestServer _streamServer;
    private readonly ISettingsRepository _settingsRepo;
    private readonly AppSettings _settings;
    private readonly ILogger? _logger;

    private int _port = 49100;
    private string? _statusMessage;

    public StreamServerPanelViewModel(
        IStreamTestServer streamServer,
        ISettingsRepository settingsRepo,
        AppSettings settings,
        ILogger<StreamServerPanelViewModel>? logger = null)
    {
        _streamServer = streamServer;
        _settingsRepo = settingsRepo;
        _settings = settings;
        _logger = logger;

        if (settings.StreamServerPort > 0)
        {
            _port = settings.StreamServerPort;
        }

        StartServerCommand = new RelayCommand(async _ => await StartServerAsync(), _ => !IsRunning);
        StopServerCommand = new RelayCommand(async _ => await StopServerAsync(), _ => IsRunning);

        _streamServer.ActiveClientsChanged += (s, e) =>
        {
            App.Current?.Dispatcher.InvokeAsync(() =>
            {
                OnPropertyChanged(nameof(ActiveClientCount));
                OnPropertyChanged(nameof(ActiveClientsDisplay));
            });
        };
    }

    public ICommand StartServerCommand { get; }
    public ICommand StopServerCommand { get; }

    public int Port
    {
        get => _port;
        set
        {
            if (SetProperty(ref _port, value))
            {
                _settings.StreamServerPort = value;
                _ = _settingsRepo.SaveAsync(_settings);
            }
        }
    }

    public bool IsRunning => _streamServer.IsRunning;
    public bool IsStopped => !IsRunning;

    public int ActiveClientCount => _streamServer.ActiveClientCount;
    public string ActiveClientsDisplay => $"Clients: {ActiveClientCount}";

    public string StatusSymbol => IsRunning ? "●" : "○";
    public string StatusText => IsRunning ? "RUNNING" : "STOPPED";
    public string StatusBrush => IsRunning ? "#16A34A" : "#6B7280";

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public IReadOnlyList<string> LocalIPv4Addresses
    {
        get
        {
            var addresses = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    var ipProps = ni.GetIPProperties();
                    foreach (var unicast in ipProps.UnicastAddresses)
                    {
                        if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
                        {
                            string ipStr = unicast.Address.ToString();
                            if (!ipStr.StartsWith("169.254.") && !addresses.Contains(ipStr))
                            {
                                addresses.Add(ipStr);
                            }
                        }
                    }
                }
            }
            catch { }

            if (addresses.Count == 0) addresses.Add("127.0.0.1");
            return addresses;
        }
    }

    public async Task StartServerAsync()
    {
        StatusMessage = null;
        try
        {
            await _streamServer.StartAsync(Port);
            OnPropertyChanged(nameof(IsRunning));
            OnPropertyChanged(nameof(IsStopped));
            OnPropertyChanged(nameof(StatusSymbol));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(LocalIPv4Addresses));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to start server: {ex.Message}";
            _logger?.LogError(ex, "Failed to start stream server on port {Port}", Port);
        }
    }

    public async Task StopServerAsync()
    {
        await _streamServer.StopAsync();
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(StatusSymbol));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBrush));
    }
}
