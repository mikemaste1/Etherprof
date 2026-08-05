namespace Etherprof.Network.Windows;

using System.Diagnostics;
using Microsoft.Extensions.Logging;

public sealed class PowerShellResult
{
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public string Error { get; init; } = "";
    public bool IsSuccess => ExitCode == 0;
}

public sealed class PowerShellRunner
{
    private readonly ILogger<PowerShellRunner> _logger;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    public PowerShellRunner(ILogger<PowerShellRunner> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Executes a PowerShell command using powershell.exe with no visible window.
    /// </summary>
    public async Task<PowerShellResult> ExecuteAsync(
        string command,
        string operationName,
        IDictionary<string, string>? logParameters = null,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;

        // Structured logging: log operation and parameters, NOT the raw command
        _logger.LogInformation("[{Operation}] Starting", operationName);
        if (logParameters is not null)
        {
            foreach (var (key, value) in logParameters)
            {
                _logger.LogInformation("[{Operation}] {Key}={Value}", operationName, key, value);
            }
        }

        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -Command \"{EscapeCommand(command)}\"",
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi };
        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) outputBuilder.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) errorBuilder.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                _logger.LogWarning("[{Operation}] Timed out after {Timeout}s, killing process", operationName, effectiveTimeout.TotalSeconds);
                SafeKill(process);
                return new PowerShellResult { ExitCode = -1, Error = $"Operation timed out after {effectiveTimeout.TotalSeconds}s" };
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("[{Operation}] Cancelled", operationName);
                SafeKill(process);
                throw;
            }

            var result = new PowerShellResult
            {
                ExitCode = process.ExitCode,
                Output = outputBuilder.ToString().Trim(),
                Error = errorBuilder.ToString().Trim()
            };

            _logger.LogInformation("[{Operation}] ExitCode={ExitCode}, Duration=completed", operationName, result.ExitCode);

            if (!string.IsNullOrWhiteSpace(result.Error))
            {
                _logger.LogWarning("[{Operation}] StdErr output detected (may be non-fatal)", operationName);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "[{Operation}] Failed to execute", operationName);
            return new PowerShellResult { ExitCode = -1, Error = ex.Message };
        }
    }

    private static string EscapeCommand(string command)
    {
        // Escape double quotes inside the command for the -Command parameter
        return command.Replace("\"", "\\\"");
    }

    private static void SafeKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }
}
