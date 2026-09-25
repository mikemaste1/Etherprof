namespace Etherprof.App.Views;

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Windows;
using Etherprof.Core;

public sealed class DiscoveredHostItem : INotifyPropertyChanged
{
    public string IpAddress { get; init; } = "";
    public double LatencyMs { get; set; }
    public string LatencyText => LatencyMs < 1 ? "< 1 ms" : $"{LatencyMs:0} ms";

    private bool _isSelected = true;
    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public partial class PingPrefixDialog : Window
{
    private static int _scanCounter = 0;

    public int ScanId { get; } = Interlocked.Increment(ref _scanCounter);
    public DateTime ScanTime { get; } = DateTime.Now;
    public Action<List<string>>? OnStartContinuousPing { get; set; }

    public ObservableCollection<DiscoveredHostItem> DiscoveredHosts { get; } = new();
    public List<string> SelectedHostsToPing { get; private set; } = new();

    private CancellationTokenSource? _scanCts;
    private bool _isScanning;

    public PingPrefixDialog(string proposedPrefix)
    {
        InitializeComponent();
        prefixBox.Text = proposedPrefix;
        hostsListView.ItemsSource = DiscoveredHosts;

        Title = $"[#{ScanId}] Subnet Discovery — {proposedPrefix} ({ScanTime:HH:mm:ss})";
        notebookHeader.Text = $"Notebook Scan #{ScanId}";
        timestampHeader.Text = $"Started: {ScanTime:yyyy-MM-dd HH:mm:ss}";
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        prefixBox.Focus();
        prefixBox.SelectAll();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        CancelScan();
    }

    private void OnPastePrefix(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            string text = Clipboard.GetText().Trim();
            if (!string.IsNullOrEmpty(text))
            {
                prefixBox.Text = text;
            }
        }
    }

    private async void OnScanToggle(object sender, RoutedEventArgs e)
    {
        if (_isScanning)
        {
            CancelScan();
            return;
        }

        string cidr = prefixBox.Text.Trim();
        errorText.Visibility = Visibility.Collapsed;

        var validation = IPv4Validator.ParseCidr(cidr);
        if (!validation.IsValid)
        {
            errorText.Text = validation.ErrorMessage ?? "Invalid CIDR prefix";
            errorText.Visibility = Visibility.Visible;
            return;
        }

        var hostsToScan = SubnetCalculator.GetHostAddresses(cidr, maxHosts: 512);
        if (hostsToScan.Count == 0)
        {
            errorText.Text = "No host addresses found in specified prefix";
            errorText.Visibility = Visibility.Visible;
            return;
        }

        DiscoveredHosts.Clear();
        emptyNotice.Visibility = Visibility.Collapsed;
        _isScanning = true;
        scanButton.Content = "■ Stop Scan";
        scanButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226));
        scanButton.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(252, 165, 165));
        scanButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
        startPingButton.IsEnabled = false;

        _scanCts = new CancellationTokenSource();
        var ct = _scanCts.Token;

        progressBar.Maximum = hostsToScan.Count;
        progressBar.Value = 0;
        statusText.Text = $"Pinging {hostsToScan.Count} hosts...";
        countText.Text = "0 found";

        int scanned = 0;
        int found = 0;
        var semaphore = new SemaphoreSlim(40); // 40 concurrent pings for fast, network-friendly sweep

        try
        {
            var tasks = hostsToScan.Select(async hostIp =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    if (ct.IsCancellationRequested) return;

                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(hostIp, 400); // 400ms timeout
                    if (reply.Status == IPStatus.Success)
                    {
                        var item = new DiscoveredHostItem
                        {
                            IpAddress = hostIp,
                            LatencyMs = reply.RoundtripTime
                        };

                        Dispatcher.Invoke(() =>
                        {
                            DiscoveredHosts.Add(item);
                            found++;
                            countText.Text = $"{found} found";
                            startPingButton.IsEnabled = true;
                        });
                    }
                }
                catch
                {
                    // Ignore ping failure / unreachable
                }
                finally
                {
                    semaphore.Release();
                    Dispatcher.Invoke(() =>
                    {
                        scanned++;
                        progressBar.Value = scanned;
                        statusText.Text = $"Scanning ({scanned}/{hostsToScan.Count})...";
                    });
                }
            });

            await Task.WhenAll(tasks);
            statusText.Text = $"Scan completed: {hostsToScan.Count} hosts scanned.";
        }
        catch (OperationCanceledException)
        {
            statusText.Text = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            statusText.Text = $"Scan error: {ex.Message}";
        }
        finally
        {
            _isScanning = false;
            scanButton.Content = "🔍 Scan Subnet";
            scanButton.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 252, 231));
            scanButton.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(134, 239, 172));
            scanButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(21, 128, 61));
            startPingButton.IsEnabled = DiscoveredHosts.Count > 0;
            if (DiscoveredHosts.Count == 0)
            {
                emptyNotice.Text = "No responding hosts found on this subnet prefix.";
                emptyNotice.Visibility = Visibility.Visible;
            }
        }
    }

    private void CancelScan()
    {
        if (_scanCts != null && !_scanCts.IsCancellationRequested)
        {
            _scanCts.Cancel();
            _scanCts.Dispose();
            _scanCts = null;
        }
        _isScanning = false;
    }

    private void OnCopyHosts(object sender, RoutedEventArgs e)
    {
        if (DiscoveredHosts.Count == 0) return;
        var ips = DiscoveredHosts.Where(h => h.IsSelected).Select(h => h.IpAddress).ToList();
        if (ips.Count == 0) ips = DiscoveredHosts.Select(h => h.IpAddress).ToList();
        Clipboard.SetText(string.Join(Environment.NewLine, ips));
        statusText.Text = $"Copied {ips.Count} host IPs to clipboard.";
    }

    private void OnStartPingHosts(object sender, RoutedEventArgs e)
    {
        var selected = DiscoveredHosts.Where(h => h.IsSelected).Select(h => h.IpAddress).ToList();
        if (selected.Count == 0) selected = DiscoveredHosts.Select(h => h.IpAddress).ToList();

        SelectedHostsToPing = selected;
        if (OnStartContinuousPing != null)
        {
            OnStartContinuousPing.Invoke(selected);
            statusText.Text = $"▶ Started continuous ping on {selected.Count} hosts in Main Window.";
        }
        else
        {
            DialogResult = true;
        }
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        CancelScan();
        Close();
    }
}
