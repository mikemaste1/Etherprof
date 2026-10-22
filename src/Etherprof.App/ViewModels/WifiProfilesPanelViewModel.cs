namespace Etherprof.App.ViewModels;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Etherprof.Core;
using Microsoft.Extensions.Logging;

public sealed class WifiProfilesPanelViewModel : INotifyPropertyChangedHelper
{
    private readonly IWifiHistoryRepository? _historyRepo;
    private readonly ILogger<WifiProfilesPanelViewModel> _logger;
    private readonly Dispatcher _dispatcher;

    private readonly ConcurrentDictionary<string, WifiNetworkHistoryRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText != value)
            {
                _searchText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSearchText));
                FilterProfiles();
            }
        }
    }

    public bool HasSearchText => !string.IsNullOrEmpty(_searchText);

    public ObservableCollection<WifiHistoryItemViewModel> FilteredProfiles { get; } = new();

    public int TotalCount => _records.Count;
    public int FilteredCount => FilteredProfiles.Count;
    public string CountSummaryText => string.IsNullOrEmpty(SearchText)
        ? $"{TotalCount} saved profiles"
        : $"Showing {FilteredCount} of {TotalCount} profiles";

    // Selected profile view card
    private string? _selectedSsid;
    public string? SelectedSsid
    {
        get => _selectedSsid;
        private set
        {
            if (_selectedSsid != value)
            {
                _selectedSsid = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelectedProfile));
            }
        }
    }

    public bool HasSelectedProfile => !string.IsNullOrEmpty(SelectedSsid);

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

    public bool HasKey => !string.IsNullOrEmpty(WifiKey);

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

    public Action<string, string>? LogAction { get; set; }

    public ICommand ClearSearchCommand { get; }
    public ICommand ExtractOrToggleKeyCommand { get; }
    public ICommand CopyKeyCommand { get; }
    public ICommand ToggleQrCodeCommand { get; }
    public ICommand CloseSelectedProfileCommand { get; }
    public ICommand CopySsidCommand { get; }

    public WifiProfilesPanelViewModel(
        IWifiHistoryRepository? historyRepo,
        ILogger<WifiProfilesPanelViewModel> logger)
    {
        _historyRepo = historyRepo;
        _logger = logger;
        _dispatcher = Dispatcher.CurrentDispatcher;

        ClearSearchCommand = new RelayCommand(() => SearchText = "");

        ExtractOrToggleKeyCommand = new RelayCommand(_ =>
        {
            if (string.IsNullOrEmpty(WifiKey))
            {
                if (!string.IsNullOrEmpty(SelectedSsid))
                    _ = ExtractWifiKeyAsync(SelectedSsid);
            }
            else
            {
                IsKeyRevealed = !IsKeyRevealed;
                if (IsKeyRevealed)
                    UpdateQrCode();
                else
                    IsQrCodeVisible = false;
            }
        });

        ToggleQrCodeCommand = new RelayCommand(_ =>
        {
            if (!IsQrCodeVisible)
            {
                if (string.IsNullOrEmpty(WifiKey))
                {
                    if (!string.IsNullOrEmpty(SelectedSsid))
                        _ = ExtractWifiKeyAsync(SelectedSsid);
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

        CopyKeyCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrEmpty(WifiKey) && !WifiKey.StartsWith("("))
            {
                try
                {
                    Clipboard.SetText(WifiKey);
                    KeyStatusMessage = "Copied!";
                    LogAction?.Invoke("WIFI", $"Copied Wi-Fi key for '{SelectedSsid}' to clipboard.");
                }
                catch { }
            }
        });

        CopySsidCommand = new RelayCommand(_ =>
        {
            if (!string.IsNullOrEmpty(SelectedSsid))
            {
                try
                {
                    Clipboard.SetText(SelectedSsid);
                    LogAction?.Invoke("WIFI", $"Copied SSID '{SelectedSsid}' to clipboard.");
                }
                catch { }
            }
        });

        CloseSelectedProfileCommand = new RelayCommand(() =>
        {
            SelectedSsid = null;
            WifiKey = null;
            IsKeyRevealed = false;
            IsQrCodeVisible = false;
            WifiQrCodeImage = null;
            KeyStatusMessage = null;

            foreach (var item in FilteredProfiles)
            {
                item.IsCurrent = false;
            }
        });
    }

    public async Task InitializeAsync()
    {
        if (_historyRepo != null)
        {
            try
            {
                var history = await _historyRepo.LoadAsync();
                _records.Clear();
                foreach (var rec in history)
                {
                    if (!string.IsNullOrWhiteSpace(rec.Ssid))
                    {
                        _records[rec.Ssid] = rec;
                    }
                }
                FilterProfiles();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed loading Wi-Fi profiles");
            }
        }
    }

    public void AddOrUpdateRecord(string ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return;

        var rec = _records.AddOrUpdate(
            ssid.Trim(),
            s => new WifiNetworkHistoryRecord { Ssid = s, LastConnected = DateTimeOffset.UtcNow },
            (s, existing) => { existing.LastConnected = DateTimeOffset.UtcNow; return existing; });

        if (_historyRepo != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _historyRepo.SaveAsync(_records.Values.ToList());
                }
                catch { }
            });
        }

        FilterProfiles();
    }

    public void SelectAndRevealProfile(string ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return;

        string targetSsid = ssid.Trim();
        SelectedSsid = targetSsid;

        foreach (var item in FilteredProfiles)
        {
            item.IsCurrent = string.Equals(item.Ssid, targetSsid, StringComparison.OrdinalIgnoreCase);
        }

        WifiKey = null;
        IsKeyRevealed = false;
        IsQrCodeVisible = false;
        WifiQrCodeImage = null;
        KeyStatusMessage = null;

        _ = ExtractWifiKeyAsync(targetSsid);
    }

    private void FilterProfiles()
    {
        _dispatcher.BeginInvoke(() =>
        {
            var query = _records.Values.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string term = _searchText.Trim();
                query = query.Where(r => r.Ssid.Contains(term, StringComparison.OrdinalIgnoreCase));
            }

            var ordered = query.OrderByDescending(r => r.LastConnected).ToList();

            FilteredProfiles.Clear();
            foreach (var rec in ordered)
            {
                bool isCurrent = string.Equals(rec.Ssid, SelectedSsid, StringComparison.OrdinalIgnoreCase);
                var item = new WifiHistoryItemViewModel(
                    rec.Ssid,
                    rec.LastConnected,
                    isCurrent,
                    onSelect: s => SelectAndRevealProfile(s),
                    onLog: LogAction);
                FilteredProfiles.Add(item);
            }

            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(FilteredCount));
            OnPropertyChanged(nameof(CountSummaryText));
        });
    }

    private void UpdateQrCode()
    {
        if (IsKeyRevealed && !string.IsNullOrWhiteSpace(SelectedSsid) && !string.IsNullOrWhiteSpace(WifiKey))
        {
            string qrText = WifiQrFormatter.Format(SelectedSsid, WifiKey);
            WifiQrCodeImage = QrCodeImageHelper.CreateQrCodeBitmap(qrText, scale: 6, margin: 3);
            IsQrCodeVisible = WifiQrCodeImage != null;
        }
        else
        {
            IsQrCodeVisible = false;
        }
    }

    public async Task ExtractWifiKeyAsync(string ssid)
    {
        if (string.IsNullOrWhiteSpace(ssid)) return;

        try
        {
            IsExtractingKey = true;
            KeyStatusMessage = "Reading key...";
            OnPropertyChanged(nameof(DisplayKeyText));

            string? key = await Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("netsh", $"wlan show profile name=\"{ssid}\" key=clear")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var proc = Process.Start(psi);
                    if (proc == null) return null;

                    string output = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);

                    return WifiKeyParser.ExtractKeyFromNetshOutput(output);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed running netsh to extract key for {Ssid}", ssid);
                    return null;
                }
            });

            if (!string.IsNullOrEmpty(key))
            {
                WifiKey = key;
                IsKeyRevealed = true;
                KeyStatusMessage = key.StartsWith("(") ? null : "Key revealed";
                LogAction?.Invoke("WIFI", $"Extracted Wi-Fi key for profile '{ssid}'.");
                UpdateQrCode();
            }
            else
            {
                WifiKey = null;
                KeyStatusMessage = "Key not found in Windows profile.";
                WifiQrCodeImage = null;
                IsQrCodeVisible = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed extracting key for {Ssid}", ssid);
            KeyStatusMessage = "Error reading key.";
        }
        finally
        {
            IsExtractingKey = false;
            OnPropertyChanged(nameof(DisplayKeyText));
        }
    }
}
