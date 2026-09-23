using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Kvertis.App.Services;

/// <summary>
/// Opt-in diagnostic log (ADR-009): plain text files in LocalFolder/logs, one per day, written only while the
/// user has enabled logging in the settings. Nothing is ever sent anywhere.
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    private const int MaxLogFiles = 7;

    private readonly Func<bool> _isEnabled;
    private readonly string _directory;
    private readonly object _gate = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new(StringComparer.Ordinal);

    public FileLoggerProvider(Func<bool> isEnabled, string directory)
    {
        _isEnabled = isEnabled ?? throw new ArgumentNullException(nameof(isEnabled));
        _directory = directory;
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    public void Dispose() => _loggers.Clear();

    internal bool IsEnabled => _isEnabled();

    internal void Write(string category, LogLevel level, string message, Exception? exception)
    {
        if (!IsEnabled)
        {
            return;
        }
        var line = new StringBuilder()
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
            .Append(" [").Append(level).Append("] ")
            .Append(category).Append(": ")
            .Append(message);
        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }
        line.AppendLine();

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var file = Path.Combine(_directory, "kvertis-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");
                var isNew = !File.Exists(file);
                File.AppendAllText(file, line.ToString());
                if (isNew)
                {
                    PruneOldFiles();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never break the app.
            }
        }
    }

    private void PruneOldFiles()
    {
        foreach (var old in Directory.EnumerateFiles(_directory, "kvertis-*.log")
                     .OrderByDescending(f => f, StringComparer.Ordinal)
                     .Skip(MaxLogFiles))
        {
            File.Delete(old);
        }
    }

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information && _provider.IsEnabled;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }
            ArgumentNullException.ThrowIfNull(formatter);
            _provider.Write(_category, logLevel, formatter(state, exception), exception);
        }
    }
}
