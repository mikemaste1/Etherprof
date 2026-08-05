using System.Windows;
using Etherprof.Contracts.Models;
using Etherprof.Core;

namespace Etherprof.App.Views;

public partial class ProfileEditorDialog : Window
{
    private readonly NetworkProfile? _existing;
    public NetworkProfile? Result { get; private set; }

    public ProfileEditorDialog(NetworkProfile? existing)
    {
        InitializeComponent();
        _existing = existing;

        if (existing is not null)
        {
            Title = "Edit Profile";
            nameBox.Text = existing.Name;

            if (existing.Type == NetworkProfileType.Dhcp)
            {
                dhcpRadio.IsChecked = true;
            }
            else if (existing.Type == NetworkProfileType.DnsOnly)
            {
                dnsOnlyRadio.IsChecked = true;
            }
            else
            {
                staticRadio.IsChecked = true;
                if (existing.IPv4 is not null)
                {
                    addressBox.Text = existing.IPv4.Address;
                    prefixBox.Text = existing.IPv4.PrefixLength.ToString();
                    gatewayBox.Text = existing.IPv4.Gateway ?? "";
                }
            }

            changeDnsCheck.IsChecked = existing.ApplyDns;
            if (existing.ApplyDns && existing.Dns is not null)
            {
                if (existing.Dns.Mode == DnsMode.Static)
                {
                    dnsStaticRadio.IsChecked = true;
                    if (existing.Dns.Servers.Count > 0)
                        dns1Box.Text = existing.Dns.Servers[0];
                    if (existing.Dns.Servers.Count > 1)
                        dns2Box.Text = existing.Dns.Servers[1];
                }
                else
                {
                    dnsAutoRadio.IsChecked = true;
                }
            }
            else
            {
                dnsAutoRadio.IsChecked = true;
            }
        }
        else
        {
            Title = "New Profile";
            dhcpRadio.IsChecked = true;
            prefixBox.Text = "24";
            dnsAutoRadio.IsChecked = true;
        }

        UpdateVisibility();
    }

    private void OnIpTypeChanged(object sender, RoutedEventArgs e) => UpdateVisibility();
    private void OnDnsCheckChanged(object sender, RoutedEventArgs e) => UpdateVisibility();
    private void OnDnsModeChanged(object sender, RoutedEventArgs e) => UpdateVisibility();

    private void UpdateVisibility()
    {
        if (dhcpRadio is null || staticRadio is null || dnsOnlyRadio is null) return;

        bool isStatic = staticRadio.IsChecked == true;
        bool isDnsOnly = dnsOnlyRadio.IsChecked == true;

        if (isDnsOnly)
        {
            changeDnsCheck.IsChecked = true;
            changeDnsCheck.IsEnabled = false;
        }
        else
        {
            changeDnsCheck.IsEnabled = true;
        }

        bool changeDns = changeDnsCheck.IsChecked == true;
        bool manualDns = dnsStaticRadio.IsChecked == true;

        staticPanel.Visibility = isStatic ? Visibility.Visible : Visibility.Collapsed;
        dnsPanel.Visibility = changeDns ? Visibility.Visible : Visibility.Collapsed;
        dnsServersPanel.Visibility = (changeDns && manualDns) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        errorText.Text = "";

        var name = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            errorText.Text = "Name is required";
            return;
        }

        bool isStatic = staticRadio.IsChecked == true;
        bool isDhcp = dhcpRadio.IsChecked == true;
        bool isDnsOnly = dnsOnlyRadio.IsChecked == true;
        bool changeDns = changeDnsCheck.IsChecked == true;
        bool manualDns = dnsStaticRadio.IsChecked == true;

        NetworkProfileType profileType = isDnsOnly
            ? NetworkProfileType.DnsOnly
            : (isStatic ? NetworkProfileType.Static : NetworkProfileType.Dhcp);

        IPv4Configuration? ipv4 = null;
        if (isStatic)
        {
            if (!IPv4Validator.IsValidAddress(addressBox.Text))
            {
                errorText.Text = "Invalid IPv4 address";
                return;
            }

            if (!int.TryParse(prefixBox.Text, out var pl) || !IPv4Validator.IsValidPrefixLength(pl))
            {
                errorText.Text = "Prefix length must be 0-32";
                return;
            }

            string? gateway = string.IsNullOrWhiteSpace(gatewayBox.Text) ? null : gatewayBox.Text.Trim();
            if (gateway is not null && !IPv4Validator.IsValidAddress(gateway))
            {
                errorText.Text = "Invalid gateway address";
                return;
            }

            ipv4 = new IPv4Configuration
            {
                Address = addressBox.Text.Trim(),
                PrefixLength = (byte)pl,
                Gateway = gateway
            };
        }

        DnsConfiguration? dns = null;
        if (changeDns)
        {
            if (manualDns)
            {
                var servers = new List<string>();
                var d1 = dns1Box.Text.Trim();
                if (string.IsNullOrEmpty(d1))
                {
                    errorText.Text = "At least one DNS server is required for Manual DNS";
                    return;
                }
                if (!IPv4Validator.IsValidAddress(d1))
                {
                    errorText.Text = "Invalid DNS 1 address";
                    return;
                }
                servers.Add(d1);

                var d2 = dns2Box.Text.Trim();
                if (!string.IsNullOrEmpty(d2))
                {
                    if (!IPv4Validator.IsValidAddress(d2))
                    {
                        errorText.Text = "Invalid DNS 2 address";
                        return;
                    }
                    servers.Add(d2);
                }

                dns = new DnsConfiguration { Mode = DnsMode.Static, Servers = servers };
            }
            else
            {
                dns = new DnsConfiguration { Mode = DnsMode.Automatic };
            }
        }

        Result = new NetworkProfile
        {
            Id = _existing?.Id ?? Guid.NewGuid(),
            Name = name,
            Type = profileType,
            IPv4 = ipv4,
            ApplyDns = changeDns,
            Dns = dns,
            SortOrder = _existing?.SortOrder ?? 0
        };

        DialogResult = true;
    }
}
