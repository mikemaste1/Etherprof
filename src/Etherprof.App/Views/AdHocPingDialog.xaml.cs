using System.Windows;
using System.Windows.Threading;
using Etherprof.Core;

namespace Etherprof.App.Views;

public partial class AdHocPingDialog : Window
{
    public string TargetIp { get; private set; } = "";
    public string? TargetLabel { get; private set; }
    private readonly bool _focusLastOctet;

    public AdHocPingDialog(string prefilledIp, bool focusLastOctet = true)
    {
        InitializeComponent();
        _focusLastOctet = focusLastOctet;
        ipBox.Text = prefilledIp;

        if (!string.IsNullOrEmpty(prefilledIp))
        {
            int lastDot = prefilledIp.LastIndexOf('.');
            if (lastDot >= 0 && focusLastOctet)
            {
                subnetHintText.Text = $"Network preselected: {prefilledIp.Substring(0, lastDot + 1)}x · Type last octet to ping similar host";
            }
            else
            {
                subnetHintText.Text = $"Default gateway / proposed target: {prefilledIp}";
            }
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_focusLastOctet)
            {
                FocusYoungerByte();
            }
            else
            {
                ipBox.Focus();
                ipBox.SelectAll();
            }
        });
    }

    private void FocusYoungerByte()
    {
        ipBox.Focus();
        string text = ipBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) return;

        int lastDot = text.LastIndexOf('.');
        if (lastDot >= 0 && lastDot < text.Length - 1)
        {
            // Select the younger byte (after the last dot) so typing immediately replaces it
            ipBox.Select(lastDot + 1, text.Length - (lastDot + 1));
        }
        else
        {
            ipBox.SelectAll();
        }
    }

    private void OnPasteIp(object sender, RoutedEventArgs e)
    {
        if (Clipboard.ContainsText())
        {
            string text = Clipboard.GetText().Trim();
            if (!string.IsNullOrEmpty(text))
            {
                ipBox.Text = text;
                FocusYoungerByte();
            }
        }
    }

    private string? GetValidatedHost()
    {
        errorText.Text = "";
        string host = ipBox.Text.Trim();

        if (string.IsNullOrEmpty(host))
        {
            errorText.Text = "Target IP or hostname is required";
            return null;
        }

        if (!IPv4Validator.IsValidHostOrAddress(host))
        {
            errorText.Text = $"Invalid IP address or host: '{host}'";
            return null;
        }

        return host;
    }

    private void OnOpenBrowser(object sender, RoutedEventArgs e)
    {
        string? host = GetValidatedHost();
        if (host == null) return;

        var dialog = new OpenBrowserDialog(host) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnRunTraceroute(object sender, RoutedEventArgs e)
    {
        string? host = GetValidatedHost();
        if (host == null) return;

        var dialog = new TracerouteDialog(host) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnStartPing(object sender, RoutedEventArgs e)
    {
        string? host = GetValidatedHost();
        if (host == null) return;

        TargetIp = host;
        TargetLabel = string.IsNullOrWhiteSpace(labelBox.Text) ? null : labelBox.Text.Trim();

        DialogResult = true;
    }
}
