namespace Etherprof.Network.Windows;

using System.Net.NetworkInformation;
using System.Net.Sockets;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Microsoft.Extensions.Logging;

public sealed class WindowsAdapterProvider : INetworkAdapterProvider
{
    private readonly PowerShellRunner _ps;
    private readonly ILogger<WindowsAdapterProvider> _logger;
    private Timer? _fallbackTimer;
    private bool _monitoring;
    private bool _disposed;

    public event EventHandler<NetworkAdapterChangedEventArgs>? AdapterChanged;

    public WindowsAdapterProvider(PowerShellRunner ps, ILogger<WindowsAdapterProvider> logger)
    {
        _ps = ps;
        _logger = logger;
    }

    public Task<IReadOnlyList<NetworkAdapter>> GetAdaptersAsync()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        var adapters = interfaces.Select(ni => new NetworkAdapter
        {
            Id = ni.Id,
            Name = ni.Name,
            Description = ni.Description,
            InterfaceType = ni.NetworkInterfaceType.ToString(),
            MacAddress = FormatMac(ni.GetPhysicalAddress())
        }).ToList();

        return Task.FromResult<IReadOnlyList<NetworkAdapter>>(adapters);
    }

    public async Task<NetworkAdapterState> GetStateAsync(string adapterId)
    {
        var ni = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Id == adapterId);

        if (ni is null)
        {
            return new NetworkAdapterState { AdapterId = adapterId, IsAvailable = false };
        }

        var props = ni.GetIPProperties();
        var ipv4Addr = props.UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                && !a.Address.ToString().StartsWith("169.254")); // skip link-local

        var gateway = props.GatewayAddresses
            .FirstOrDefault(g => g.Address.AddressFamily == AddressFamily.InterNetwork
                && g.Address.ToString() != "0.0.0.0");

        var dnsServers = props.DnsAddresses
            .Where(d => d.AddressFamily == AddressFamily.InterNetwork)
            .Select(d => d.ToString())
            .ToList();

        // Get authoritative DHCP and DNS state via PowerShell
        int interfaceIndex = GetInterfaceIndex(ni);
        var (isDhcpEnabled, isDnsAutomatic) = await QueryAuthoritativeStateAsync(adapterId, interfaceIndex);

        var linkSpeed = ni.OperationalStatus == OperationalStatus.Up
            ? FormatLinkSpeed(ni.Speed)
            : null;

        return new NetworkAdapterState
        {
            AdapterId = adapterId,
            IsAvailable = true,
            IsConnected = ni.OperationalStatus == OperationalStatus.Up,
            IsDhcpEnabled = isDhcpEnabled,
            IsDnsAutomatic = isDnsAutomatic,
            IPv4Address = ipv4Addr?.Address.ToString(),
            PrefixLength = ipv4Addr is not null ? (byte?)GetPrefixLength(ipv4Addr) : null,
            Gateway = gateway?.Address.ToString(),
            DnsServers = dnsServers,
            LinkSpeed = linkSpeed
        };
    }

    private async Task<(bool isDhcp, bool isDnsAutomatic)> QueryAuthoritativeStateAsync(string adapterId, int interfaceIndex)
    {
        bool isDhcp = false;
        bool isDnsAutomatic = true;

        try
        {
            // Query DHCP state from Get-NetIPInterface
            var dhcpResult = await _ps.ExecuteAsync(
                $"(Get-NetIPInterface -InterfaceIndex {interfaceIndex} -AddressFamily IPv4 -ErrorAction SilentlyContinue).Dhcp",
                "QueryDhcpState",
                new Dictionary<string, string> { ["InterfaceIndex"] = interfaceIndex.ToString() });

            if (dhcpResult.IsSuccess)
            {
                isDhcp = dhcpResult.Output.Trim().Equals("Enabled", StringComparison.OrdinalIgnoreCase);
            }

            // Query DNS state via registry - empty NameServer = automatic
            // The registry key HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\{GUID}\NameServer
            // is empty when DNS is automatic, and contains comma-separated IPs when static
            var guidForRegistry = adapterId.Trim('{', '}');
            var dnsResult = await _ps.ExecuteAsync(
                $"(Get-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces\\{{{guidForRegistry}}}' -Name NameServer -ErrorAction SilentlyContinue).NameServer",
                "QueryDnsState",
                new Dictionary<string, string> { ["AdapterId"] = adapterId });

            if (dnsResult.IsSuccess)
            {
                isDnsAutomatic = string.IsNullOrWhiteSpace(dnsResult.Output.Trim());
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query authoritative DHCP/DNS state for adapter {AdapterId}", adapterId);
        }

        return (isDhcp, isDnsAutomatic);
    }

    public void StartMonitoring()
    {
        if (_monitoring) return;
        _monitoring = true;

        NetworkChange.NetworkAddressChanged += OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;

        // Fallback timer at ~10s using System.Threading.Timer (NO WPF dependency)
        _fallbackTimer = new Timer(_ => RaiseAdapterChanged(null), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

        _logger.LogInformation("Adapter monitoring started");
    }

    public void StopMonitoring()
    {
        if (!_monitoring) return;
        _monitoring = false;

        NetworkChange.NetworkAddressChanged -= OnNetworkChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;

        _fallbackTimer?.Dispose();
        _fallbackTimer = null;

        _logger.LogInformation("Adapter monitoring stopped");
    }

    private void OnNetworkChanged(object? sender, EventArgs e) => RaiseAdapterChanged(null);
    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => RaiseAdapterChanged(null);

    private void RaiseAdapterChanged(string? adapterId)
    {
        // Fires on arbitrary thread - UI must dispatch
        AdapterChanged?.Invoke(this, new NetworkAdapterChangedEventArgs { AdapterId = adapterId });
    }

    private static int GetInterfaceIndex(NetworkInterface ni)
    {
        try
        {
            return ni.GetIPProperties().GetIPv4Properties().Index;
        }
        catch
        {
            // Fallback: try IPv6 index or return -1
            try { return ni.GetIPProperties().GetIPv6Properties().Index; }
            catch { return -1; }
        }
    }

    private static int GetPrefixLength(UnicastIPAddressInformation addr)
    {
        try { return addr.PrefixLength; }
        catch { return 24; } // fallback
    }

    private static string FormatMac(PhysicalAddress mac)
    {
        var bytes = mac.GetAddressBytes();
        return bytes.Length == 0 ? "" : string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    private static string FormatLinkSpeed(long bitsPerSecond)
    {
        return bitsPerSecond switch
        {
            >= 1_000_000_000 => $"{bitsPerSecond / 1_000_000_000.0:0.#} Gbps",
            >= 1_000_000 => $"{bitsPerSecond / 1_000_000.0:0.#} Mbps",
            >= 1_000 => $"{bitsPerSecond / 1_000.0:0.#} Kbps",
            _ => $"{bitsPerSecond} bps"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopMonitoring();
    }
}
