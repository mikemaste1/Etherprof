using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Etherprof.App.Views;

public partial class OpenBrowserDialog : Window
{
    private readonly string _targetHost;
    private bool _initialized;

    public string ConstructedUrl { get; private set; } = "";

    public OpenBrowserDialog(string targetHost)
    {
        InitializeComponent();
        _targetHost = targetHost.Trim();
        hostDisplay.Text = $"Target Host: {_targetHost}";
        _initialized = true;
        UpdatePreview();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        portBox.Focus();
        portBox.SelectAll();
    }

    private void OnProtocolChanged(object sender, RoutedEventArgs e)
    {
        if (!_initialized) return;

        bool isHttps = httpsRadio.IsChecked == true;
        string currentPort = portBox.Text.Trim();

        // Auto-switch default port if current port matches the opposite standard port
        if (isHttps && (currentPort == "80" || string.IsNullOrEmpty(currentPort)))
        {
            portBox.Text = "443";
        }
        else if (!isHttps && (currentPort == "443" || string.IsNullOrEmpty(currentPort)))
        {
            portBox.Text = "80";
        }

        UpdatePreview();
    }

    private void OnPortChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initialized) return;
        UpdatePreview();
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string portStr)
        {
            portBox.Text = portStr;
            if (portStr == "443" || portStr == "8443")
            {
                httpsRadio.IsChecked = true;
            }
            else if (portStr == "80" || portStr == "8080")
            {
                httpRadio.IsChecked = true;
            }
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        if (!_initialized || urlPreviewText == null) return;

        bool isHttps = httpsRadio?.IsChecked == true;
        string scheme = isHttps ? "https" : "http";
        string portStr = portBox?.Text.Trim() ?? "";

        if (int.TryParse(portStr, out int port) && port > 0 && port <= 65535)
        {
            if (errorText != null) errorText.Text = "";
            bool isDefaultPort = (scheme == "http" && port == 80) || (scheme == "https" && port == 443);
            ConstructedUrl = isDefaultPort ? $"{scheme}://{_targetHost}/" : $"{scheme}://{_targetHost}:{port}/";
            urlPreviewText.Text = ConstructedUrl;
        }
        else if (string.IsNullOrEmpty(portStr))
        {
            ConstructedUrl = $"{scheme}://{_targetHost}/";
            urlPreviewText.Text = ConstructedUrl;
            if (errorText != null) errorText.Text = "";
        }
        else
        {
            if (errorText != null) errorText.Text = "Port must be a valid number (1-65535)";
            urlPreviewText.Text = "—";
        }
    }

    private void OnOpenBrowser(object sender, RoutedEventArgs e)
    {
        UpdatePreview();

        if (urlPreviewText.Text == "—" || string.IsNullOrEmpty(ConstructedUrl))
        {
            errorText.Text = "Please enter a valid port number";
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(ConstructedUrl) { UseShellExecute = true });
            DialogResult = true;
        }
        catch (Exception ex)
        {
            errorText.Text = $"Failed to launch browser: {ex.Message}";
        }
    }
}
