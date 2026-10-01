namespace Etherprof.App.ViewModels;

using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.Core;
using Microsoft.Extensions.Logging;

public sealed class WifiPanelViewModel : INotifyPropertyChangedHelper
{
    private readonly IWifiMonitor _wifiMonitor;
    private readonly IWifiBssidRepository _bssidRepo;
    private readonly BoundedWifiEventLog _eventLog = new();
    private readonly ILogger<WifiPanelViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    private readonly ConcurrentDictionary<string, WifiBssidRecord> _bssidCache = new(StringComparer.OrdinalIgnoreCase);

    private WifiConnectionState? _state;
    private bool _isWifiAdapter;
    private string _activeAdapterId = "";

    public Action<string, string?>? OpenAliasEditor { get; set; }

    public bool IsWifiAdapter
    {
        get => _isWifiAdapter;
        set { _isWifiAdapter = value; OnPropertyChanged(); }
    }

    public WifiConnectionState? State
    {
        get => _state;
        private set
        {
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(Availability));
            OnPropertyChanged(nameof(Ssid));
            OnPropertyChanged(nameof(Bssid));
            OnPropertyChanged(nameof(CurrentBssidAlias));
            OnPropertyChanged(nameof(CurrentBssidDisplayName));
            OnPropertyChanged(nameof(HasAlias));
            OnPropertyChanged(nameof(RssiText));
            OnPropertyChanged(nameof(QualityText));
            OnPropertyChanged(nameof(SignalCombinedText));
            OnPropertyChanged(nameof(BandChannelText));
            OnPropertyChanged(nameof(StatusMessage));
            OnPropertyChanged(nameof(IsPermissionDenied));
            OnPropertyChanged(nameof(IsNotWifiAdapter));
        }
    }

    public bool IsConnected => State?.IsConnected ?? false;
    public WifiMonitorAvailability Availability => State?.Availability ?? WifiMonitorAvailability.NotWifiAdapter;
    public bool IsPermissionDenied => Availability == WifiMonitorAvailability.PermissionDenied;
    public bool IsNotWifiAdapter => !IsWifiAdapter;

    public string Ssid => State?.Ssid ?? "";
    public string Bssid => State?.Bssid ?? "";

    public string? CurrentBssidAlias => GetAlias(Bssid);
    public string CurrentBssidDisplayName => !string.IsNullOrEmpty(CurrentBssidAlias) ? CurrentBssidAlias : Bssid;
    public bool HasAlias => !string.IsNullOrEmpty(CurrentBssidAlias);

    public string RssiText => State?.RssiDbm.HasValue == true ? $"{State.RssiDbm.Value} dBm" : "";
    public string QualityText => State?.SignalQualityPercent.HasValue == true ? $"{State.SignalQualityPercent.Value}%" : "";

    public string SignalCombinedText
    {
        get
        {
            if (!string.IsNullOrEmpty(RssiText) && !string.IsNullOrEmpty(QualityText))
                return $"{RssiText}    {QualityText}";
            if (!string.IsNullOrEmpty(RssiText))
                return RssiText;
            return QualityText;
        }
    }

    public string BandChannelText
    {
        get
        {
            if (State is null || !State.IsConnected) return "";

            string bandStr = State.Band switch
            {
                WifiBand.GHz2_4 => "2.4 GHz",
                WifiBand.GHz5 => "5 GHz",
                WifiBand.GHz6 => "6 GHz",
                _ => ""
            };

            string chStr = State.Channel.HasValue ? $"Ch {State.Channel.Value}" : "";

            if (!string.IsNullOrEmpty(bandStr) && !string.IsNullOrEmpty(chStr))
                return $"{bandStr} · {chStr}";
            if (!string.IsNullOrEmpty(bandStr))
                return bandStr;
            return chStr;
        }
    }

    public string StatusMessage
    {
        get
        {
            return Availability switch
            {
                WifiMonitorAvailability.NotWifiAdapter => "Select a Wi-Fi adapter to monitor.",
                WifiMonitorAvailability.AdapterUnavailable => "Selected Wi-Fi adapter unavailable.",
                WifiMonitorAvailability.Disconnected => "○ Disconnected",
                WifiMonitorAvailability.PermissionDenied => "⚠ Windows privacy settings prevent Etherprof from reading Wi-Fi details.",
                WifiMonitorAvailability.Error => State?.Error ?? "Wi-Fi monitor error",
                _ => ""
            };
        }
    }

    public ObservableCollection<WifiEventViewModel> WifiEvents { get; } = new();

    public Action<string, string>? LogAction { get; set; }

    public ICommand EditAliasCommand { get; }
    public ICommand OpenLocationSettingsCommand { get; }
    public ICommand CopySsidCommand { get; }
    public ICommand CopyBssidCommand { get; }
    public ICommand ExtractOrToggleKeyCommand { get; }
    public ICommand CopyKeyCommand { get; }

    private string? _wifiKey;
    public string? WifiKey
    {
        get => _wifiKey;
        private set
        {
            _wifiKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasKey));
            OnPropertyChanged(nameof(DisplayKeyText));
        }
    }

    private bool _isKeyRevealed;
    public bool IsKeyRevealed
    {
        get => _isKeyRevealed;
        set
        {
            _isKeyRevealed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayKeyText));
            OnPropertyChanged(nameof(KeyVisibilityIcon));
        }
    }

    private bool _isExtractingKey;
    public bool IsExtractingKey
    {
        get => _isExtractingKey;
        private set
        {
            _isExtractingKey = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayKeyText));
        }
    }

    private string? _keyStatusMessage;
    public string? KeyStatusMessage
    {
        get => _keyStatusMessage;
        set
        {
            _keyStatusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasKeyStatusMessage));
        }
    }

    public bool HasKeyStatusMessage => !string.IsNullOrEmpty(KeyStatusMessage);

    public bool HasKey => !string.IsNullOrEmpty(WifiKey);

    public string KeyVisibilityIcon => IsKeyRevealed ? "🙈 Hide" : "👁 Reveal";

    public string DisplayKeyText
    {
        get
        {
            if (string.IsNullOrEmpty(WifiKey))
            {
                return IsExtractingKey ? "Reading key..." : "(Click '👁 Reveal' to show key)";
            }
            if (WifiKey.StartsWith("("))
            {
                return WifiKey;
            }
            return IsKeyRevealed ? WifiKey : "••••••••••••";
        }
    }

    public WifiPanelViewModel(
        IWifiMonitor wifiMonitor,
        IWifiBssidRepository bssidRepo,
        ILogger<WifiPanelViewModel> logger)
    {
        _wifiMonitor = wifiMonitor;
        _bssidRepo = bssidRepo;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;

        EditAliasCommand = new RelayCommand(_ => TriggerEditAlias());
        OpenLocationSettingsCommand = new RelayCommand(_ => OpenWindowsLocationSettings());

        CopySsidCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrEmpty(Ssid))
            {
                try
                {
                    System.Windows.Clipboard.SetText(Ssid);
                    LogAction?.Invoke("WIFI", $"Copied SSID to clipboard: {Ssid}");
                }
                catch { }
            }
        });

        CopyBssidCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrEmpty(Bssid))
            {
                try
                {
                    System.Windows.Clipboard.SetText(Bssid);
                    LogAction?.Invoke("WIFI", $"Copied BSSID (MAC) to clipboard: {Bssid}");
                }
                catch { }
            }
        });

        ExtractOrToggleKeyCommand = new RelayCommand(_ =>
        {
            if (string.IsNullOrEmpty(WifiKey))
            {
                _ = ExtractWifiKeyAsync();
            }
            else
            {
                IsKeyRevealed = !IsKeyRevealed;
            }
        });

        CopyKeyCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrEmpty(WifiKey) && !WifiKey.StartsWith("("))
            {
                try
                {
                    System.Windows.Clipboard.SetText(WifiKey);
                    KeyStatusMessage = "Copied!";
                    LogAction?.Invoke("WIFI", $"Copied Wi-Fi key for '{Ssid}' to clipboard.");
                }
                catch { }
            }
            else if (string.IsNullOrEmpty(WifiKey))
            {
                Task.Run(async () =>
                {
                    await ExtractWifiKeyAsync();
                    if (!string.IsNullOrEmpty(WifiKey) && !WifiKey.StartsWith("("))
                    {
                        await _dispatcher.BeginInvoke(() =>
                        {
                            try
                            {
                                System.Windows.Clipboard.SetText(WifiKey);
                                KeyStatusMessage = "Copied!";
                            }
                            catch { }
                        });
                    }
                });
            }
        });

        _wifiMonitor.StateChanged += OnWifiStateChanged;
        _wifiMonitor.Roamed += OnWifiRoamed;
        _wifiMonitor.ConnectionEvent += OnWifiConnectionEvent;
    }

    public async Task ExtractWifiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(Ssid))
        {
            KeyStatusMessage = "Not connected to Wi-Fi.";
            return;
        }

        try
        {
            IsExtractingKey = true;
            KeyStatusMessage = "Reading key...";
            OnPropertyChanged(nameof(DisplayKeyText));

            string? currentSsid = Ssid;
            string? key = await Task.Run(() =>
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("netsh", $"wlan show profile name=\"{currentSsid}\" key=clear")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = System.Diagnostics.Process.Start(psi);
                    if (proc == null) return null;

                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);

                    return WifiKeyParser.ExtractKeyFromNetshOutput(output);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed running netsh to extract key");
                }
                return null;
            });

            if (!string.IsNullOrEmpty(key))
            {
                WifiKey = key;
                IsKeyRevealed = true;
                KeyStatusMessage = key.StartsWith("(") ? null : "Key revealed";
                LogAction?.Invoke("WIFI", $"Extracted Wi-Fi key for network '{currentSsid}'.");
            }
            else
            {
                WifiKey = null;
                KeyStatusMessage = "Key not found.";
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract Wi-Fi key for SSID '{Ssid}'", Ssid);
            KeyStatusMessage = "Error reading key";
        }
        finally
        {
            IsExtractingKey = false;
            OnPropertyChanged(nameof(DisplayKeyText));
        }
    }

    public async Task InitializeAsync()
    {
        try
        {
            var records = await _bssidRepo.LoadAsync();
            _bssidCache.Clear();
            foreach (var rec in records)
            {
                if (BssidNormalizer.TryNormalize(rec.Bssid, out var norm))
                {
                    _bssidCache[norm] = new WifiBssidRecord { Bssid = norm, Alias = rec.Alias };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing BSSID cache");
        }
    }

    public async Task UpdateSelectedAdapterAsync(string adapterId)
    {
        _activeAdapterId = adapterId;

        if (string.IsNullOrEmpty(adapterId))
        {
            IsWifiAdapter = false;
            await _wifiMonitor.StopAsync();
            State = new WifiConnectionState
            {
                AdapterId = adapterId,
                Availability = WifiMonitorAvailability.NotWifiAdapter,
                IsConnected = false,
                Timestamp = DateTimeOffset.UtcNow
            };
            return;
        }

        await _wifiMonitor.StartAsync(adapterId, CancellationToken.None);
        var cur = _wifiMonitor.CurrentState;
        IsWifiAdapter = cur?.Availability != WifiMonitorAvailability.NotWifiAdapter;
        State = cur;
    }

    public async Task SetAliasAsync(string bssid, string? alias)
    {
        if (!BssidNormalizer.TryNormalize(bssid, out var norm))
            return;

        alias = string.IsNullOrWhiteSpace(alias) ? null : alias.Trim();

        _bssidCache.AddOrUpdate(
            norm,
            new WifiBssidRecord { Bssid = norm, Alias = alias },
            (_, existing) => { existing.Alias = alias; return existing; });

        try
        {
            await _bssidRepo.SaveAsync(_bssidCache.Values.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist updated BSSID alias");
        }

        OnPropertyChanged(nameof(CurrentBssidAlias));
        OnPropertyChanged(nameof(CurrentBssidDisplayName));
        OnPropertyChanged(nameof(HasAlias));

        // Propagate alias change instantly to all displayed RAM event log rows (Requirement 43)
        foreach (var evVm in WifiEvents)
        {
            evVm.NotifyAliasChanged();
        }
    }

    public string? GetAlias(string? bssid)
    {
        if (string.IsNullOrEmpty(bssid)) return null;
        if (BssidNormalizer.TryNormalize(bssid, out var norm))
        {
            if (_bssidCache.TryGetValue(norm, out var record))
                return record.Alias;
        }
        return null;
    }

    private void TriggerEditAlias()
    {
        if (!string.IsNullOrEmpty(Bssid))
        {
            OpenAliasEditor?.Invoke(Bssid, CurrentBssidAlias);
        }
    }

    private static void OpenWindowsLocationSettings()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:privacy-location",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void OnWifiStateChanged(object? sender, WifiStateChangedEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            IsWifiAdapter = e.State.Availability != WifiMonitorAvailability.NotWifiAdapter;
            State = e.State;

            // Automatic BSSID registration (Requirement 14)
            if (e.State.IsConnected && !string.IsNullOrEmpty(e.State.Bssid))
            {
                RegisterBssidIfUnseen(e.State.Bssid);
            }
        });
    }

    private void OnWifiRoamed(object? sender, WifiRoamEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            AddEventToLog(e.Event);
            if (!string.IsNullOrEmpty(e.Event.CurrentBssid))
            {
                RegisterBssidIfUnseen(e.Event.CurrentBssid);
            }
        });
    }

    private void OnWifiConnectionEvent(object? sender, WifiConnectionEventArgs e)
    {
        _dispatcher.BeginInvoke(() =>
        {
            AddEventToLog(e.Event);
            if (!string.IsNullOrEmpty(e.Event.CurrentBssid))
            {
                RegisterBssidIfUnseen(e.Event.CurrentBssid);
            }
        });
    }

    public event EventHandler<WifiEventViewModel>? EventLogged;

    private void AddEventToLog(WifiEvent wifiEvent)
    {
        _eventLog.Add(wifiEvent);

        var vm = new WifiEventViewModel(wifiEvent, GetAlias);
        WifiEvents.Insert(0, vm); // Newest first

        // Keep maximum 100 entries in ObservableCollection UI
        while (WifiEvents.Count > BoundedWifiEventLog.Capacity)
        {
            WifiEvents.RemoveAt(WifiEvents.Count - 1);
        }

        EventLogged?.Invoke(this, vm);
    }

    private void RegisterBssidIfUnseen(string bssid)
    {
        if (!BssidNormalizer.TryNormalize(bssid, out var norm))
            return;

        if (!_bssidCache.ContainsKey(norm))
        {
            var record = new WifiBssidRecord { Bssid = norm, Alias = null };
            if (_bssidCache.TryAdd(norm, record))
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await _bssidRepo.SaveAsync(_bssidCache.Values.ToList());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed auto-saving new BSSID record {Bssid}", norm);
                    }
                });
            }
        }
    }
}
