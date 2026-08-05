namespace Etherprof.Network.Windows;

using System.Net.NetworkInformation;
using Etherprof.Contracts.Interfaces;
using Etherprof.Contracts.Models;
using Microsoft.Extensions.Logging;

public sealed class PowerShellNetworkConfigurationService : INetworkConfigurationService
{
    private readonly PowerShellRunner _ps;
    private readonly INetworkAdapterProvider _adapterProvider;
    private readonly ILogger<PowerShellNetworkConfigurationService> _logger;

    public PowerShellNetworkConfigurationService(
        PowerShellRunner ps,
        INetworkAdapterProvider adapterProvider,
        ILogger<PowerShellNetworkConfigurationService> logger)
    {
        _ps = ps;
        _adapterProvider = adapterProvider;
        _logger = logger;
    }

    public async Task<ApplyResult> ApplyProfileAsync(
        string adapterId, NetworkProfile profile, CancellationToken ct = default)
    {
        int interfaceIndex = ResolveInterfaceIndex(adapterId);
        if (interfaceIndex < 0)
            return ApplyResult.Failed($"Adapter '{adapterId}' not found or has no valid interface index");

        _logger.LogInformation("[ApplyProfile] Profile={Name}, Type={Type}, AdapterId={AdapterId}, InterfaceIndex={Index}",
            profile.Name, profile.Type, adapterId, interfaceIndex);

        try
        {
            if (profile.Type == NetworkProfileType.Dhcp)
            {
                await ApplyDhcpAsync(interfaceIndex, ct);
            }
            else if (profile.Type == NetworkProfileType.Static)
            {
                if (profile.IPv4 is null)
                    return ApplyResult.Failed("Static profile has no IPv4 configuration");

                await ApplyStaticAsync(interfaceIndex, profile.IPv4, ct);
            }
            // If DnsOnly: do not modify IP or gateway, leave current IP configuration untouched

            // DNS handling - only if ApplyDns is true
            if (profile.ApplyDns && profile.Dns is not null)
            {
                await ApplyDnsAsync(interfaceIndex, profile.Dns, ct);
            }
            // If ApplyDns is false: absolutely no DNS commands

            // Verification
            await Task.Delay(1000, ct); // brief delay for Windows to settle
            var state = await _adapterProvider.GetStateAsync(adapterId);
            var verified = VerifyProfile(state, profile);

            if (!verified)
            {
                _logger.LogWarning("[ApplyProfile] Verification failed for profile {Name}", profile.Name);
                return ApplyResult.VerificationFailed("Adapter configuration does not match requested profile after apply");
            }

            _logger.LogInformation("[ApplyProfile] Success for profile {Name}", profile.Name);
            return ApplyResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ApplyProfile] Failed for profile {Name}", profile.Name);
            return ApplyResult.Failed(ex.Message);
        }
    }

    public async Task<ApplyResult> ApplyTemporaryAddressAsync(
        string adapterId, IPv4Configuration config, CancellationToken ct = default)
    {
        int interfaceIndex = ResolveInterfaceIndex(adapterId);
        if (interfaceIndex < 0)
            return ApplyResult.Failed($"Adapter '{adapterId}' not found");

        _logger.LogInformation("[ApplyTempAddress] Address={Address}/{Prefix}, AdapterId={AdapterId}, InterfaceIndex={Index}",
            config.Address, config.PrefixLength, adapterId, interfaceIndex);

        try
        {
            // Conservative cleanup + apply address only. No gateway. No DNS.
            await RemoveStaleAddressesAsync(interfaceIndex, ct);

            var applyCmd = $"New-NetIPAddress -InterfaceIndex {interfaceIndex} -IPAddress '{config.Address}' -PrefixLength {config.PrefixLength} -ErrorAction Stop";
            var result = await _ps.ExecuteAsync(applyCmd, "ApplyTempAddress",
                new Dictionary<string, string>
                {
                    ["InterfaceIndex"] = interfaceIndex.ToString(),
                    ["Address"] = config.Address,
                    ["PrefixLength"] = config.PrefixLength.ToString()
                }, ct: ct);

            if (!result.IsSuccess)
                return ApplyResult.Failed($"Failed to apply address: {result.Error}");

            // Verification
            await Task.Delay(500, ct);
            var state = await _adapterProvider.GetStateAsync(adapterId);

            if (!string.Equals(state.IPv4Address, config.Address, StringComparison.OrdinalIgnoreCase)
                || state.PrefixLength != config.PrefixLength)
            {
                return ApplyResult.VerificationFailed(
                    $"Expected {config.Address}/{config.PrefixLength}, got {state.IPv4Address}/{state.PrefixLength}");
            }

            _logger.LogInformation("[ApplyTempAddress] Success: {Address}/{Prefix}", config.Address, config.PrefixLength);
            return ApplyResult.Succeeded();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ApplyTempAddress] Failed");
            return ApplyResult.Failed(ex.Message);
        }
    }

    public async Task<NetworkProfile> CaptureCurrentAsync(string adapterId)
    {
        var state = await _adapterProvider.GetStateAsync(adapterId);

        var profile = new NetworkProfile
        {
            Id = Guid.NewGuid(),
            Type = state.IsDhcpEnabled ? NetworkProfileType.Dhcp : NetworkProfileType.Static,
            ApplyDns = false // default: don't touch DNS
        };

        if (!state.IsDhcpEnabled && state.IPv4Address is not null)
        {
            profile.IPv4 = new IPv4Configuration
            {
                Address = state.IPv4Address,
                PrefixLength = state.PrefixLength ?? 24,
                Gateway = state.Gateway
            };
        }

        // Capture DNS state for reference but leave ApplyDns = false
        if (state.DnsServers.Count > 0 && !state.IsDnsAutomatic)
        {
            profile.Dns = new DnsConfiguration
            {
                Mode = DnsMode.Static,
                Servers = state.DnsServers.ToList()
            };
        }
        else
        {
            profile.Dns = new DnsConfiguration { Mode = DnsMode.Automatic };
        }

        return profile;
    }

    private async Task ApplyDhcpAsync(int interfaceIndex, CancellationToken ct)
    {
        // Enable DHCP
        var enableCmd = $"Set-NetIPInterface -InterfaceIndex {interfaceIndex} -Dhcp Enabled -ErrorAction Stop";
        var result = await _ps.ExecuteAsync(enableCmd, "EnableDhcp",
            new Dictionary<string, string> { ["InterfaceIndex"] = interfaceIndex.ToString() }, ct: ct);

        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to enable DHCP: {result.Error}");

        // Remove stale static addresses (conservative)
        await RemoveStaleAddressesAsync(interfaceIndex, ct);

        // Remove stale default route
        await RemoveDefaultRouteAsync(interfaceIndex, ct);
    }

    private async Task ApplyStaticAsync(int interfaceIndex, IPv4Configuration ipv4, CancellationToken ct)
    {
        // Conservative cleanup
        await RemoveStaleAddressesAsync(interfaceIndex, ct);
        await RemoveDefaultRouteAsync(interfaceIndex, ct);

        // Apply new address
        var cmd = $"New-NetIPAddress -InterfaceIndex {interfaceIndex} -IPAddress '{ipv4.Address}' -PrefixLength {ipv4.PrefixLength}";

        if (!string.IsNullOrEmpty(ipv4.Gateway))
        {
            cmd += $" -DefaultGateway '{ipv4.Gateway}'";
        }

        cmd += " -ErrorAction Stop";

        var result = await _ps.ExecuteAsync(cmd, "ApplyStaticIp",
            new Dictionary<string, string>
            {
                ["InterfaceIndex"] = interfaceIndex.ToString(),
                ["Address"] = ipv4.Address,
                ["PrefixLength"] = ipv4.PrefixLength.ToString(),
                ["Gateway"] = ipv4.Gateway ?? "(none)"
            }, ct: ct);

        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to apply static IP: {result.Error}");

        // Disable DHCP after setting static IP
        await _ps.ExecuteAsync(
            $"Set-NetIPInterface -InterfaceIndex {interfaceIndex} -Dhcp Disabled -ErrorAction SilentlyContinue",
            "DisableDhcp",
            new Dictionary<string, string> { ["InterfaceIndex"] = interfaceIndex.ToString() }, ct: ct);
    }

    private async Task ApplyDnsAsync(int interfaceIndex, DnsConfiguration dns, CancellationToken ct)
    {
        string cmd;
        if (dns.Mode == DnsMode.Automatic)
        {
            cmd = $"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ResetServerAddresses -ErrorAction Stop";
        }
        else
        {
            var servers = string.Join(",", dns.Servers.Select(s => $"'{s}'"));
            cmd = $"Set-DnsClientServerAddress -InterfaceIndex {interfaceIndex} -ServerAddresses @({servers}) -ErrorAction Stop";
        }

        var result = await _ps.ExecuteAsync(cmd, "ApplyDns",
            new Dictionary<string, string>
            {
                ["InterfaceIndex"] = interfaceIndex.ToString(),
                ["DnsMode"] = dns.Mode.ToString(),
                ["Servers"] = dns.Mode == DnsMode.Static ? string.Join(", ", dns.Servers) : "(automatic)"
            }, ct: ct);

        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to apply DNS: {result.Error}");
    }

    /// <summary>
    /// Remove manually/DHCP-configured IPv4 addresses, preserving link-local/APIPA.
    /// </summary>
    private async Task RemoveStaleAddressesAsync(int interfaceIndex, CancellationToken ct)
    {
        var cmd = $"Get-NetIPAddress -InterfaceIndex {interfaceIndex} -AddressFamily IPv4 -ErrorAction SilentlyContinue | " +
                  $"Where-Object {{ $_.PrefixOrigin -ne 'WellKnown' -and $_.SuffixOrigin -ne 'Link' }} | " +
                  $"Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue";

        await _ps.ExecuteAsync(cmd, "RemoveStaleAddresses",
            new Dictionary<string, string> { ["InterfaceIndex"] = interfaceIndex.ToString() }, ct: ct);
    }

    /// <summary>
    /// Remove default route (0.0.0.0/0) only.
    /// </summary>
    private async Task RemoveDefaultRouteAsync(int interfaceIndex, CancellationToken ct)
    {
        var cmd = $"Remove-NetRoute -InterfaceIndex {interfaceIndex} -DestinationPrefix '0.0.0.0/0' -Confirm:$false -ErrorAction SilentlyContinue";
        await _ps.ExecuteAsync(cmd, "RemoveDefaultRoute",
            new Dictionary<string, string> { ["InterfaceIndex"] = interfaceIndex.ToString() }, ct: ct);
    }

    private bool VerifyProfile(NetworkAdapterState state, NetworkProfile profile)
    {
        if (profile.Type == NetworkProfileType.Dhcp)
        {
            if (!state.IsDhcpEnabled) return false;
        }
        else if (profile.Type == NetworkProfileType.Static && profile.IPv4 is not null)
        {
            if (!string.Equals(state.IPv4Address, profile.IPv4.Address, StringComparison.OrdinalIgnoreCase))
                return false;
            if (state.PrefixLength != profile.IPv4.PrefixLength)
                return false;
            if (!string.IsNullOrEmpty(profile.IPv4.Gateway) &&
                !string.Equals(state.Gateway, profile.IPv4.Gateway, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Only verify DNS if ApplyDns was true
        if (profile.ApplyDns && profile.Dns is not null)
        {
            if (profile.Dns.Mode == DnsMode.Automatic && !state.IsDnsAutomatic)
                return false;
            if (profile.Dns.Mode == DnsMode.Static)
            {
                var expected = new HashSet<string>(profile.Dns.Servers, StringComparer.OrdinalIgnoreCase);
                var actual = new HashSet<string>(state.DnsServers, StringComparer.OrdinalIgnoreCase);
                if (!expected.SetEquals(actual)) return false;
            }
        }

        return true;
    }

    private static int ResolveInterfaceIndex(string adapterId)
    {
        var ni = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.Id == adapterId);

        if (ni is null) return -1;

        try { return ni.GetIPProperties().GetIPv4Properties().Index; }
        catch
        {
            try { return ni.GetIPProperties().GetIPv6Properties().Index; }
            catch { return -1; }
        }
    }
}
