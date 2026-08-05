using System.IO;
using Microsoft.Extensions.Logging;

namespace Etherprof.App.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;

    public FileLoggerProvider()
    {
        _logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Etherprof", "logs");
        Directory.CreateDirectory(_logDirectory);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new FileLogger(categoryName, _logDirectory);
    }

    public void Dispose() { }
}

public sealed class FileLogger : ILogger
{
    private readonly string _category;
    private readonly string _logDirectory;
    private static readonly object Lock = new();

    public FileLogger(string category, string logDirectory)
    {
        _category = category;
        _logDirectory = logDirectory;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var logFile = Path.Combine(_logDirectory, $"etherprof-{DateTime.Now:yyyy-MM-dd}.log");
        var shortCategory = _category.Contains('.') ? _category[((_category.LastIndexOf('.') + 1))..] : _category;
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{logLevel,-11}] [{shortCategory}] {formatter(state, exception)}";
        if (exception is not null)
            line += Environment.NewLine + exception;

        lock (Lock)
        {
            try { File.AppendAllText(logFile, line + Environment.NewLine); }
            catch { /* best effort */ }
        }
    }
}
