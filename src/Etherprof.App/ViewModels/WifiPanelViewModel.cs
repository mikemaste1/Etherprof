namespace Etherprof.App.ViewModels;

using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.Core;
using Microsoft.Extensions.Logging;

public sealed class WifiPanelViewModel : INotifyPropertyChangedHelper
{
    private readonly IWifiMonitor _wifiMonitor;
    private readonly IWifiBssidRepository _bssidRepo;
    private readonly IWifiHistoryRepository? _historyRepo;
    private readonly BoundedWifiEventLog _eventLog = new();
    private readonly ILogger<WifiPanelViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    private readonly ConcurrentDictionary<string, WifiBssidRecord> _bssidCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, WifiNetworkHistoryRecord> _historyRecords = new(StringComparer.OrdinalIgnoreCase);

    private WifiConnectionState? _state;
    private bool _isWifiAdapter;
    private string _activeAdapterId = "";
    private string? _currentObservedSsid;
    private string? _selectedHistorySsid;

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
            OnPropertyChanged(nameof(DisplaySsid));
            OnPropertyChanged(nameof(IsViewingHistoryNetwork));
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
            OnPropertyChanged(nameof(HasActiveOrSelectedNetwork));
            OnPropertyChanged(nameof(CanShowLiveBssidAndSignal));
        }
    }

    public bool IsConnected => State?.IsConnected ?? false;
    public WifiMonitorAvailability Availability => State?.Availability ?? WifiMonitorAvailability.NotWifiAdapter;
    public bool IsPermissionDenied => Availability == WifiMonitorAvailability.PermissionDenied;
    public bool IsNotWifiAdapter => !IsWifiAdapter;

    public string Ssid => State?.Ssid ?? "";
    public string DisplaySsid => !string.IsNullOrEmpty(_selectedHistorySsid) ? _selectedHistorySsid : Ssid;
    public bool IsViewingHistoryNetwork => !string.IsNullOrEmpty(_selectedHistorySsid) && !string.Equals(_selectedHistorySsid, Ssid, StringComparison.OrdinalIgnoreCase);
    public bool HasActiveOrSelectedNetwork => IsConnected || !string.IsNullOrEmpty(_selectedHistorySsid);
    public bool CanShowLiveBssidAndSignal => IsConnected && !IsViewingHistoryNetwork;
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
    public ObservableCollection<WifiHistoryItemViewModel> WifiHistory { get; } = new();
    public bool HasWifiHistory => WifiHistory.Count > 0;

    public Action<string, string>? LogAction { get; set; }

    public ICommand EditAliasCommand { get; }
    public ICommand OpenLocationSettingsCommand { get; }
    public ICommand CopySsidCommand { get; }
    public ICommand CopyBssidCommand { get; }
    public ICommand ExtractOrToggleKeyCommand { get; }
    public ICommand CopyKeyCommand { get; }
    public ICommand ToggleQrCodeCommand { get; }
    public ICommand ResetToActiveSsidCommand { get; }

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

    private BitmapSource? _wifiQrCodeImage;
    public BitmapSource? WifiQrCodeImage
    {
        get => _wifiQrCodeImage;
        private set
        {
            _wifiQrCodeImage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasQrCode));
        }
    }

    public bool HasQrCode => WifiQrCodeImage != null;

    private bool _isQrCodeVisible;
    public bool IsQrCodeVisible
    {
        get => _isQrCodeVisible;
        set
        {
            _isQrCodeVisible = value;
            OnPropertyChanged();
        }
    }

    public WifiPanelViewModel(
        IWifiMonitor wifiMonitor,
        IWifiBssidRepository bssidRepo,
        IWifiHistoryRepository? historyRepo,
        ILogger<WifiPanelViewModel> logger)
    {
        _wifiMonitor = wifiMonitor;
        _bssidRepo = bssidRepo;
        _historyRepo = historyRepo;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;

        EditAliasCommand = new RelayCommand(_ => TriggerEditAlias());
        OpenLocationSettingsCommand = new RelayCommand(_ => OpenWindowsLocationSettings());

        CopySsidCommand = new RelayCommand(_ =>
        {
            string textToCopy = DisplaySsid;
            if (!string.IsNullOrEmpty(textToCopy))
            {
                try
                {
                    System.Windows.Clipboard.SetText(textToCopy);
                    LogAction?.Invoke("WIFI", $"Copied SSID to clipboard: {textToCopy}");
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
                if (IsKeyRevealed)
                {
                    UpdateQrCode();
                }
                else
                {
                    IsQrCodeVisible = false;
                }
            }
        });

        ToggleQrCodeCommand = new RelayCommand(_ =>
        {
            if (!IsQrCodeVisible)
            {
                if (string.IsNullOrEmpty(WifiKey))
                {
                    _ = ExtractWifiKeyAsync();
                }
                else
                {
                    IsKeyRevealed = true;
                    UpdateQrCode();
                }
            }
            else
            {
                IsQrCodeVisible = false;
            }
        });

        ResetToActiveSsidCommand = new RelayCommand(_ =>
        {
            _selectedHistorySsid = null;
            OnPropertyChanged(nameof(DisplaySsid));
            OnPropertyChanged(nameof(IsViewingHistoryNetwork));
            OnPropertyChanged(nameof(HasActiveOrSelectedNetwork));
            OnPropertyChanged(nameof(CanShowLiveBssidAndSignal));

            foreach (var item in WifiHistory)
            {
                item.IsCurrent = string.Equals(item.Ssid, Ssid, StringComparison.OrdinalIgnoreCase);
            }

            WifiKey = null;
            IsKeyRevealed = false;
            IsQrCodeVisible = false;
            WifiQrCodeImage = null;
            KeyStatusMessage = null;

            if (!string.IsNullOrEmpty(Ssid))
            {
                _ = ExtractWifiKeyAsync(Ssid);
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
                    LogAction?.Invoke("WIFI", $"Copied Wi-Fi key for '{DisplaySsid}' to clipboard.");
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

    public WifiPanelViewModel(
        IWifiMonitor wifiMonitor,
        IWifiBssidRepository bssidRepo,
        ILogger<WifiPanelViewModel> logger)
        : this(wifiMonitor, bssidRepo, null, logger)
    {
    }

    public void SelectHistorySsid(string ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return;

        _selectedHistorySsid = ssid.Trim();
        OnPropertyChanged(nameof(DisplaySsid));
        OnPropertyChanged(nameof(IsViewingHistoryNetwork));
        OnPropertyChanged(nameof(HasActiveOrSelectedNetwork));
        OnPropertyChanged(nameof(CanShowLiveBssidAndSignal));

        foreach (var item in WifiHistory)
        {
            item.IsCurrent = string.Equals(item.Ssid, _selectedHistorySsid, StringComparison.OrdinalIgnoreCase);
        }

        WifiKey = null;
        IsKeyRevealed = false;
        IsQrCodeVisible = false;
        WifiQrCodeImage = null;
        KeyStatusMessage = null;

        _ = ExtractWifiKeyAsync(_selectedHistorySsid);
    }

    public void UpdateQrCode()
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (IsKeyRevealed && !string.IsNullOrWhiteSpace(DisplaySsid) && !string.IsNullOrWhiteSpace(WifiKey))
            {
                string qrText = WifiQrFormatter.Format(DisplaySsid, WifiKey);
                WifiQrCodeImage = QrCodeImageHelper.CreateQrCodeBitmap(qrText, scale: 6, margin: 3);
                IsQrCodeVisible = WifiQrCodeImage != null;
            }
            else
            {
                IsQrCodeVisible = false;
            }
        });
    }

    public async Task ExtractWifiKeyAsync(string? targetSsid = null)
    {
        string currentTarget = !string.IsNullOrWhiteSpace(targetSsid) ? targetSsid.Trim() : DisplaySsid;

        if (string.IsNullOrWhiteSpace(currentTarget))
        {
            KeyStatusMessage = "No Wi-Fi network selected.";
            return;
        }

        try
        {
            IsExtractingKey = true;
            KeyStatusMessage = "Reading key...";
            OnPropertyChanged(nameof(DisplayKeyText));

            string? key = await Task.Run(() =>
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("netsh", $"wlan show profile name=\"{currentTarget}\" key=clear")
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
                LogAction?.Invoke("WIFI", $"Extracted Wi-Fi key for network '{currentTarget}'.");

                UpdateQrCode();
            }
            else
            {
                WifiKey = null;
                KeyStatusMessage = "Key not found.";
                WifiQrCodeImage = null;
                IsQrCodeVisible = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract Wi-Fi key for SSID '{Ssid}'", currentTarget);
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

        if (_historyRepo != null)
        {
            try
            {
                var history = await _historyRepo.LoadAsync();
                _historyRecords.Clear();
                foreach (var rec in history)
                {
                    if (!string.IsNullOrWhiteSpace(rec.Ssid))
                    {
                        _historyRecords[rec.Ssid] = rec;
                    }
                }
                SyncWifiHistoryCollection();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing Wi-Fi history");
            }
        }
    }

    public async Task UpdateSelectedAdapterAsync(string adapterId)
    {
        _activeAdapterId = adapterId;
        WifiKey = null;
        IsKeyRevealed = false;
        IsQrCodeVisible = false;
        WifiQrCodeImage = null;
        KeyStatusMessage = null;
        _currentObservedSsid = null;
        _selectedHistorySsid = null;
        OnPropertyChanged(nameof(DisplaySsid));
        OnPropertyChanged(nameof(IsViewingHistoryNetwork));
        OnPropertyChanged(nameof(HasActiveOrSelectedNetwork));
        OnPropertyChanged(nameof(CanShowLiveBssidAndSignal));

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

            if (!e.State.IsConnected || !string.Equals(e.State.Ssid, _currentObservedSsid, StringComparison.Ordinal))
            {
                _currentObservedSsid = e.State.IsConnected ? e.State.Ssid : null;
                if (string.IsNullOrEmpty(_selectedHistorySsid))
                {
                    WifiKey = null;
                    IsKeyRevealed = false;
                    IsQrCodeVisible = false;
                    WifiQrCodeImage = null;
                    KeyStatusMessage = null;
                }
            }

            State = e.State;

            if (e.State.IsConnected && !string.IsNullOrEmpty(e.State.Ssid))
            {
                RecordNetworkConnection(e.State.Ssid);
            }

            // Automatic BSSID registration
            if (e.State.IsConnected && !string.IsNullOrEmpty(e.State.Bssid))
            {
                RegisterBssidIfUnseen(e.State.Bssid);
            }
        });
    }

    private void RecordNetworkConnection(string ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return;

        var rec = _historyRecords.AddOrUpdate(
            ssid,
            s => new WifiNetworkHistoryRecord { Ssid = s, LastConnected = DateTimeOffset.UtcNow },
            (s, existing) => { existing.LastConnected = DateTimeOffset.UtcNow; return existing; });

        if (_historyRepo != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _historyRepo.SaveAsync(_historyRecords.Values.ToList());
                }
                catch { }
            });
        }

        SyncWifiHistoryCollection();
    }

    private void SyncWifiHistoryCollection()
    {
        _dispatcher.BeginInvoke(() =>
        {
            var ordered = _historyRecords.Values.OrderByDescending(r => r.LastConnected).ToList();
            WifiHistory.Clear();
            foreach (var rec in ordered)
            {
                bool isCurrent = string.Equals(rec.Ssid, DisplaySsid, StringComparison.OrdinalIgnoreCase);
                var item = new WifiHistoryItemViewModel(rec.Ssid, rec.LastConnected, isCurrent, SelectHistorySsid, LogAction);
                WifiHistory.Add(item);
            }
            OnPropertyChanged(nameof(HasWifiHistory));
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
