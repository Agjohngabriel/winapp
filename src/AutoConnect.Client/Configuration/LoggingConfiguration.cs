// src/AutoConnect.Client/Configuration/LoggingConfiguration.cs
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace AutoConnect.Client.Configuration;

public static class LoggingConfiguration
{
    public static IServiceCollection AddEnhancedLogging(this IServiceCollection services, IConfiguration configuration)
    {
        // Ensure logs directory exists
        var logsDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDirectory);

        services.AddLogging(builder =>
        {
            builder.ClearProviders();

            // Add console logging for development
            builder.AddConsole(options =>
            {
                options.IncludeScopes = true;
                options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss] ";
            });

            // Add debug logging for development
            builder.AddDebug();

            // Add custom file logging
            builder.AddProvider(new FileLoggerProvider(logsDirectory));

            // Configure log levels from appsettings
            builder.AddConfiguration(configuration.GetSection("Logging"));

            // Set minimum level based on environment
            var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";
            var minimumLevel = environment.ToLower() switch
            {
                "development" => LogLevel.Debug,
                "staging" => LogLevel.Information,
                _ => LogLevel.Warning
            };

            builder.SetMinimumLevel(minimumLevel);

            // Add filtering for noisy components
            builder.AddFilter("Microsoft", LogLevel.Warning);
            builder.AddFilter("System", LogLevel.Warning);
            builder.AddFilter("Microsoft.Extensions.Http", LogLevel.Warning);
        });

        return services;
    }
}

public class FileLoggerProvider : ILoggerProvider
{
    private readonly string _logDirectory;
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string logDirectory)
    {
        _logDirectory = logDirectory;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new FileLogger(name, _logDirectory));
    }

    public void Dispose()
    {
        foreach (var logger in _loggers.Values)
        {
            logger.Dispose();
        }
        _loggers.Clear();
    }
}

public class FileLogger : ILogger, IDisposable
{
    private readonly string _categoryName;
    private readonly string _logFilePath;
    private readonly object _lock = new object();
    private bool _disposed = false;

    public FileLogger(string categoryName, string logDirectory)
    {
        _categoryName = categoryName;

        // Create log file with current date and process ID to avoid conflicts
        var processId = Environment.ProcessId;
        var fileName = $"autoconnect-{DateTime.Now:yyyy-MM-dd}-{processId}.log";
        _logFilePath = Path.Combine(logDirectory, fileName);
    }

    public IDisposable BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel) || _disposed) return;

        var message = formatter(state, exception);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var level = logLevel.ToString().ToUpper();

        var logEntry = $"[{timestamp}] [{level}] [{_categoryName}] {message}";

        if (exception != null)
        {
            logEntry += Environment.NewLine + exception.ToString();
        }

        // Use file append with proper disposal to avoid locking issues
        lock (_lock)
        {
            try
            {
                File.AppendAllText(_logFilePath, logEntry + Environment.NewLine);
            }
            catch (IOException)
            {
                // If file is locked, try with a different filename
                try
                {
                    var backupPath = _logFilePath.Replace(".log", $"-{DateTime.Now.Ticks}.log");
                    File.AppendAllText(backupPath, logEntry + Environment.NewLine);
                }
                catch
                {
                    // Fail silently if we can't write to any log file
                }
            }
            catch
            {
                // Fail silently for other exceptions
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();
        public void Dispose() { }
    }
}