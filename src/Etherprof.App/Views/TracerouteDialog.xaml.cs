using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;

namespace Etherprof.App.Views;

public partial class TracerouteDialog : Window
{
    private readonly string _targetHost;
    private Process? _traceProcess;
    private bool _isRunning;
    private readonly object _lock = new();

    public TracerouteDialog(string targetHost)
    {
        InitializeComponent();
        _targetHost = targetHost.Trim();
        Title = $"Traceroute — {_targetHost}";
        targetHostText.Text = _targetHost;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        StartTraceroute();
    }

    private void OnStartStopClick(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            StopTraceroute();
        }
        else
        {
            StartTraceroute();
        }
    }

    private void StartTraceroute()
    {
        lock (_lock)
        {
            if (_isRunning) return;
            StopProcessInternal();

            _isRunning = true;
            startStopButton.Content = "■ Stop";
            startStopButton.ToolTip = "Stop traceroute";
            statusText.Text = $"Status: Tracing route to {_targetHost}...";
            outputBox.Clear();

            bool resolve = resolveNamesCheck.IsChecked == true;
            string args = resolve ? $"{_targetHost}" : $"-d {_targetHost}";

            var psi = new ProcessStartInfo("tracert.exe", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            try
            {
                _traceProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };

                _traceProcess.OutputDataReceived += (_, ea) =>
                {
                    if (ea.Data == null) return;
                    Dispatcher.InvokeAsync(() =>
                    {
                        outputBox.AppendText(ea.Data + Environment.NewLine);
                        outputBox.ScrollToEnd();
                    });
                };

                _traceProcess.ErrorDataReceived += (_, ea) =>
                {
                    if (ea.Data == null) return;
                    Dispatcher.InvokeAsync(() =>
                    {
                        outputBox.AppendText("[Error] " + ea.Data + Environment.NewLine);
                        outputBox.ScrollToEnd();
                    });
                };

                _traceProcess.Exited += (_, _) =>
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        lock (_lock)
                        {
                            _isRunning = false;
                            startStopButton.Content = "▶ Start";
                            startStopButton.ToolTip = "Start traceroute";
                            statusText.Text = "Status: Trace complete.";
                        }
                    });
                };

                _traceProcess.Start();
                _traceProcess.BeginOutputReadLine();
                _traceProcess.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                _isRunning = false;
                startStopButton.Content = "▶ Start";
                statusText.Text = $"Status: Failed to start tracert ({ex.Message})";
                outputBox.Text = $"Error executing tracert.exe: {ex.Message}";
            }
        }
    }

    private void StopTraceroute()
    {
        lock (_lock)
        {
            StopProcessInternal();
            _isRunning = false;
            startStopButton.Content = "▶ Start";
            statusText.Text = "Status: Trace stopped by user.";
        }
    }

    private void StopProcessInternal()
    {
        if (_traceProcess != null)
        {
            try
            {
                if (!_traceProcess.HasExited)
                {
                    _traceProcess.Kill(entireProcessTree: true);
                }
            }
            catch { /* best effort */ }
            finally
            {
                _traceProcess.Dispose();
                _traceProcess = null;
            }
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(outputBox.Text))
        {
            Clipboard.SetText(outputBox.Text);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        StopProcessInternal();
    }
}
