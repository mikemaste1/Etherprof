using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace Etherprof.App.ViewModels;

/// <summary>
/// Represents a single time-stamped Mbit/s sample for the network sparkline.
/// </summary>
public sealed class NetworkTickItem
{
    public double Value { get; init; }      // Mbit/s value for bar height scaling
    public double BarHeight { get; init; }  // Scaled bar height (0–24)
    public string ToolTipText { get; init; } = "";
}

/// <summary>
/// Real-time network load monitor: per-second Mbit/s in/out + session total MiB.
/// Uses System.Net.NetworkInformation to query NIC byte counters every 1 second.
/// </summary>
public sealed class NetworkLoadViewModel : INotifyPropertyChanged
{
    private const int SparklineSamples = 30;
    private const double MaxBarHeight = 24.0;

    private readonly DispatcherTimer _timer;
    private NetworkInterface? _nic;
    private long _lastBytesReceived;
    private long _lastBytesSent;
    private long _sessionBytesReceived;
    private long _sessionBytesSent;
    private bool _hasBaseline;
    private string _adapterName = "";

    // --- Visible properties ---

    private double _downMbps;
    public double DownMbps
    {
        get => _downMbps;
        private set { _downMbps = value; OnPropertyChanged(); OnPropertyChanged(nameof(DownMbpsText)); }
    }

    private double _upMbps;
    public double UpMbps
    {
        get => _upMbps;
        private set { _upMbps = value; OnPropertyChanged(); OnPropertyChanged(nameof(UpMbpsText)); }
    }

    public string DownMbpsText => $"↓ {_downMbps:0.0} Mbit/s";
    public string UpMbpsText   => $"↑ {_upMbps:0.0} Mbit/s";

    private string _sessionDownMiB = "↓ 0.0 MiB";
    public string SessionDownMiB
    {
        get => _sessionDownMiB;
        private set { _sessionDownMiB = value; OnPropertyChanged(); }
    }

    private string _sessionUpMiB = "↑ 0.0 MiB";
    public string SessionUpMiB
    {
        get => _sessionUpMiB;
        private set { _sessionUpMiB = value; OnPropertyChanged(); }
    }

    private bool _isVisible;
    public bool IsVisible
    {
        get => _isVisible;
        set { if (_isVisible != value) { _isVisible = value; OnPropertyChanged(); } }
    }

    // Sparkline collections (last 30 samples)
    public ObservableCollection<NetworkTickItem> DownTicks { get; } = new();
    public ObservableCollection<NetworkTickItem> UpTicks   { get; } = new();

    // Track max seen Mbit/s for relative bar scaling (window of 30 samples)
    private double _downScaleMax = 1.0;
    private double _upScaleMax   = 1.0;

    public NetworkLoadViewModel()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTimerTick;
    }

    // ---- Public API ----

    public void Start(string adapterName)
    {
        _adapterName = adapterName;
        _hasBaseline = false;
        _nic = FindNic(adapterName);
        _timer.Start();
    }

    public void Stop()
    {
        _timer.Stop();
    }

    public void UpdateAdapterName(string adapterName)
    {
        _adapterName = adapterName;
        _nic = FindNic(adapterName);
        _hasBaseline = false; // reset baseline so next tick establishes a new one
    }

    // ---- Timer logic ----

    private void OnTimerTick(object? sender, EventArgs e)
    {
        // Re-resolve NIC if null (adapter may have reconnected)
        _nic ??= FindNic(_adapterName);

        if (_nic == null)
        {
            SetZero();
            return;
        }

        IPv4InterfaceStatistics stats;
        try
        {
            stats = _nic.GetIPv4Statistics();
        }
        catch
        {
            // NIC disappeared; re-resolve next tick
            _nic = null;
            SetZero();
            return;
        }

        long received = stats.BytesReceived;
        long sent     = stats.BytesSent;

        if (!_hasBaseline)
        {
            // First tick — just capture baseline; show 0 until next tick
            _lastBytesReceived = received;
            _lastBytesSent     = sent;
            _hasBaseline       = true;
            SetZero();
            return;
        }

        // Delta bytes since last tick (guard against counter reset)
        long deltaDown = Math.Max(0, received - _lastBytesReceived);
        long deltaUp   = Math.Max(0, sent     - _lastBytesSent);

        _lastBytesReceived = received;
        _lastBytesSent     = sent;

        // Accumulate session totals
        _sessionBytesReceived += deltaDown;
        _sessionBytesSent     += deltaUp;

        // Convert to Mbit/s  (bytes * 8 / 1_000_000)
        double downMbps = deltaDown * 8.0 / 1_000_000.0;
        double upMbps   = deltaUp   * 8.0 / 1_000_000.0;

        DownMbps = downMbps;
        UpMbps   = upMbps;

        SessionDownMiB = $"↓ {_sessionBytesReceived / 1_048_576.0:0.00} MiB";
        SessionUpMiB   = $"↑ {_sessionBytesSent     / 1_048_576.0:0.00} MiB";

        // Update sparklines
        AddTick(DownTicks, downMbps, ref _downScaleMax, "↓");
        AddTick(UpTicks,   upMbps,   ref _upScaleMax,   "↑");
    }

    private void AddTick(ObservableCollection<NetworkTickItem> ticks, double mbps, ref double scaleMax, string dir)
    {
        // Keep rolling max for relative scaling; decay it slowly if current is lower
        if (mbps > scaleMax)
            scaleMax = mbps;
        else
            scaleMax = Math.Max(1.0, scaleMax * 0.97); // gentle decay

        double barHeight = scaleMax > 0 ? Math.Clamp((mbps / scaleMax) * MaxBarHeight, 1.0, MaxBarHeight) : 1.0;

        var item = new NetworkTickItem
        {
            Value      = mbps,
            BarHeight  = barHeight,
            ToolTipText = $"{dir} {mbps:0.00} Mbit/s"
        };

        ticks.Add(item);
        while (ticks.Count > SparklineSamples)
            ticks.RemoveAt(0);
    }

    private void SetZero()
    {
        DownMbps = 0;
        UpMbps   = 0;
        AddTick(DownTicks, 0, ref _downScaleMax, "↓");
        AddTick(UpTicks,   0, ref _upScaleMax,   "↑");
    }

    // ---- NIC resolution ----

    private static NetworkInterface? FindNic(string adapterName)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
            return null;

        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces();

            // 1. Exact match on description or Name
            var match = nics.FirstOrDefault(n =>
                string.Equals(n.Description, adapterName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(n.Name,        adapterName, StringComparison.OrdinalIgnoreCase));

            if (match != null) return match;

            // 2. Substring match (friendly adapter name may be part of description)
            match = nics.FirstOrDefault(n =>
                n.Description.Contains(adapterName, StringComparison.OrdinalIgnoreCase) ||
                adapterName.Contains(n.Name, StringComparison.OrdinalIgnoreCase) ||
                n.Name.Contains(adapterName, StringComparison.OrdinalIgnoreCase));

            return match;
        }
        catch
        {
            return null;
        }
    }

    // ---- INotifyPropertyChanged ----

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
