using System.Windows;
using Etherprof.Contracts.Models;

namespace Etherprof.App.Views;

public partial class CaptureProfileDialog : Window
{
    private readonly NetworkProfile _captured;
    public NetworkProfile? Result { get; private set; }

    public CaptureProfileDialog(NetworkProfile captured)
    {
        InitializeComponent();
        _captured = captured;

        // Build config summary
        var summary = captured.Type == NetworkProfileType.Dhcp
            ? "DHCP"
            : captured.IPv4 is not null
                ? $"{captured.IPv4.Address}/{captured.IPv4.PrefixLength}"
                  + (captured.IPv4.Gateway is not null ? $"\nGateway: {captured.IPv4.Gateway}" : "")
                : "Unknown";

        configSummary.Text = summary;
        nameBox.Focus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var name = nameBox.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            nameBox.Focus();
            return;
        }

        _captured.Name = name;
        Result = _captured;
        DialogResult = true;
    }
}
