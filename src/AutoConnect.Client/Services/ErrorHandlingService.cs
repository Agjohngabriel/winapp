// src/AutoConnect.Client/Services/ErrorHandlingService.cs
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Windows;

namespace AutoConnect.Client.Services;

public interface IErrorHandlingService
{
    Task HandleErrorAsync(string context, Exception exception, ErrorSeverity severity = ErrorSeverity.Error);
    Task HandleConnectionErrorAsync(string serviceName, Exception exception, bool showNotification = true);
    Task HandleApiErrorAsync(string endpoint, Exception exception, int? statusCode = null);
    Task ShowUserNotificationAsync(string title, string message, NotificationType type = NotificationType.Error);
    void LogPerformanceMetric(string operation, TimeSpan duration, bool success = true);
    IEnumerable<ErrorLogEntry> GetRecentErrors(int count = 50);
    Task<bool> ShouldRetryOperationAsync(string operation, int attemptNumber);
    void ClearErrorHistory();
}

public enum ErrorSeverity
{
    Debug,
    Info,
    Warning,
    Error,
    Critical
}

public enum NotificationType
{
    Info,
    Warning,
    Error,
    Success
}

public class ErrorLogEntry
{
    public DateTime Timestamp { get; set; }
    public string Context { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public ErrorSeverity Severity { get; set; }
    public string? StackTrace { get; set; }
    public string? ServiceName { get; set; }
    public int? StatusCode { get; set; }
}

public class ErrorHandlingService : IErrorHandlingService, INotifyPropertyChanged
{
    private readonly ILogger<ErrorHandlingService> _logger;
    private readonly ConcurrentQueue<ErrorLogEntry> _errorHistory = new();
    private readonly Dictionary<string, int> _retryCounters = new();
    private readonly Dictionary<string, DateTime> _lastRetryTime = new();
    private const int MaxErrorHistoryCount = 1000;
    private const int MaxRetryAttempts = 3;
    private readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<UserNotificationEventArgs>? NotificationRequested;

    public ErrorHandlingService(ILogger<ErrorHandlingService> logger)
    {
        _logger = logger;
    }

    public async Task HandleErrorAsync(string context, Exception exception, ErrorSeverity severity = ErrorSeverity.Error)
    {
        var entry = new ErrorLogEntry
        {
            Timestamp = DateTime.Now,
            Context = context,
            Message = exception.Message,
            Severity = severity,
            StackTrace = exception.StackTrace
        };

        // Add to history
        _errorHistory.Enqueue(entry);
        TrimErrorHistory();

        // Log based on severity
        switch (severity)
        {
            case ErrorSeverity.Debug:
                _logger.LogDebug(exception, "Debug error in {Context}: {Message}", context, exception.Message);
                break;
            case ErrorSeverity.Info:
                _logger.LogInformation("Info in {Context}: {Message}", context, exception.Message);
                break;
            case ErrorSeverity.Warning:
                _logger.LogWarning(exception, "Warning in {Context}: {Message}", context, exception.Message);
                break;
            case ErrorSeverity.Error:
                _logger.LogError(exception, "Error in {Context}: {Message}", context, exception.Message);
                break;
            case ErrorSeverity.Critical:
                _logger.LogCritical(exception, "Critical error in {Context}: {Message}", context, exception.Message);
                await ShowUserNotificationAsync("Critical Error",
                    $"A critical error occurred in {context}: {exception.Message}",
                    NotificationType.Error);
                break;
        }

        // Show user notification for errors and critical issues
        if (severity >= ErrorSeverity.Error)
        {
            var userMessage = GetUserFriendlyErrorMessage(context, exception);
            await ShowUserNotificationAsync("System Error", userMessage, NotificationType.Error);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GetRecentErrors)));
    }

    public async Task HandleConnectionErrorAsync(string serviceName, Exception exception, bool showNotification = true)
    {
        var entry = new ErrorLogEntry
        {
            Timestamp = DateTime.Now,
            Context = $"{serviceName} Connection",
            Message = exception.Message,
            Severity = ErrorSeverity.Error,
            StackTrace = exception.StackTrace,
            ServiceName = serviceName
        };

        _errorHistory.Enqueue(entry);
        TrimErrorHistory();

        _logger.LogError(exception, "Connection error in {ServiceName}: {Message}", serviceName, exception.Message);

        if (showNotification)
        {
            var userMessage = GetConnectionErrorMessage(serviceName, exception);
            await ShowUserNotificationAsync($"{serviceName} Connection Error", userMessage, NotificationType.Error);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GetRecentErrors)));
    }

    public async Task HandleApiErrorAsync(string endpoint, Exception exception, int? statusCode = null)
    {
        var entry = new ErrorLogEntry
        {
            Timestamp = DateTime.Now,
            Context = $"API Call: {endpoint}",
            Message = exception.Message,
            Severity = ErrorSeverity.Warning,
            StackTrace = exception.StackTrace,
            StatusCode = statusCode
        };

        _errorHistory.Enqueue(entry);
        TrimErrorHistory();

        if (statusCode.HasValue)
        {
            _logger.LogWarning(exception, "API error for {Endpoint} (Status: {StatusCode}): {Message}",
                endpoint, statusCode, exception.Message);
        }
        else
        {
            _logger.LogWarning(exception, "API error for {Endpoint}: {Message}", endpoint, exception.Message);
        }

        // Only show notification for severe API errors
        if (statusCode.HasValue && (statusCode >= 500 || statusCode == 401 || statusCode == 403))
        {
            await ShowUserNotificationAsync("API Error",
                GetApiErrorMessage(endpoint, statusCode.Value),
                NotificationType.Warning);
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GetRecentErrors)));
    }

    public async Task ShowUserNotificationAsync(string title, string message, NotificationType type = NotificationType.Error)
    {
        try
        {
            // Raise event for UI to handle
            NotificationRequested?.Invoke(this, new UserNotificationEventArgs
            {
                Title = title,
                Message = message,
                Type = type,
                Timestamp = DateTime.Now
            });

            // Also log the notification
            switch (type)
            {
                case NotificationType.Info:
                    _logger.LogInformation("User notification - {Title}: {Message}", title, message);
                    break;
                case NotificationType.Warning:
                    _logger.LogWarning("User notification - {Title}: {Message}", title, message);
                    break;
                case NotificationType.Error:
                    _logger.LogError("User notification - {Title}: {Message}", title, message);
                    break;
                case NotificationType.Success:
                    _logger.LogInformation("User notification - {Title}: {Message}", title, message);
                    break;
            }

            await Task.Delay(100); // Ensure async operation
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing user notification");
        }
    }

    public void LogPerformanceMetric(string operation, TimeSpan duration, bool success = true)
    {
        try
        {
            var status = success ? "SUCCESS" : "FAILED";
            _logger.LogInformation("Performance - {Operation}: {Duration}ms [{Status}]",
                operation, duration.TotalMilliseconds, status);

            // Log slow operations as warnings
            if (duration.TotalSeconds > 5)
            {
                _logger.LogWarning("Slow operation detected - {Operation} took {Duration}ms",
                    operation, duration.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging performance metric");
        }
    }

    public IEnumerable<ErrorLogEntry> GetRecentErrors(int count = 50)
    {
        return _errorHistory.TakeLast(count).OrderByDescending(e => e.Timestamp);
    }

    public async Task<bool> ShouldRetryOperationAsync(string operation, int attemptNumber)
    {
        try
        {
            if (attemptNumber >= MaxRetryAttempts)
            {
                _logger.LogWarning("Max retry attempts reached for operation: {Operation}", operation);
                return false;
            }

            if (_lastRetryTime.TryGetValue(operation, out var lastRetry))
            {
                var timeSinceLastRetry = DateTime.Now - lastRetry;
                if (timeSinceLastRetry < RetryDelay)
                {
                    var remainingDelay = RetryDelay - timeSinceLastRetry;
                    _logger.LogDebug("Delaying retry for {Operation} by {Delay}ms", operation, remainingDelay.TotalMilliseconds);
                    await Task.Delay(remainingDelay);
                }
            }

            _lastRetryTime[operation] = DateTime.Now;
            _retryCounters[operation] = attemptNumber;

            _logger.LogInformation("Retrying operation {Operation} (attempt {Attempt}/{Max})",
                operation, attemptNumber, MaxRetryAttempts);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in retry logic for operation: {Operation}", operation);
            return false;
        }
    }

    public void ClearErrorHistory()
    {
        try
        {
            while (_errorHistory.TryDequeue(out _)) { }
            _retryCounters.Clear();
            _lastRetryTime.Clear();

            _logger.LogInformation("Error history cleared");
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GetRecentErrors)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing error history");
        }
    }

    private void TrimErrorHistory()
    {
        while (_errorHistory.Count > MaxErrorHistoryCount)
        {
            _errorHistory.TryDequeue(out _);
        }
    }

    private string GetUserFriendlyErrorMessage(string context, Exception exception)
    {
        return context switch
        {
            var c when c.Contains("VPN") => "VPN connection failed. Please check your network connection and try again.",
            var c when c.Contains("Vehicle") || c.Contains("OBD") => "Vehicle connection failed. Please check that your OBD adapter is properly connected.",
            var c when c.Contains("API") => "Unable to connect to the server. Please check your internet connection.",
            var c when c.Contains("Database") => "Database operation failed. The application may continue with limited functionality.",
            _ => $"An error occurred in {context}. Please try again or contact support if the problem persists."
        };
    }

    private string GetConnectionErrorMessage(string serviceName, Exception exception)
    {
        return serviceName.ToLower() switch
        {
            "vpn" => "Unable to establish VPN connection. Please check your VPN configuration and network connection.",
            "vehicle" => "Cannot connect to vehicle. Please ensure your OBD adapter is connected and the vehicle ignition is on.",
            "api" => "Server connection failed. Please check your internet connection and try again.",
            _ => $"Connection to {serviceName} failed. Please check your configuration and try again."
        };
    }

    private string GetApiErrorMessage(int statusCode)
    {
        return statusCode switch
        {
            401 => "Authentication failed. Please check your credentials.",
            403 => "Access denied. You don't have permission to perform this action.",
            404 => "The requested resource was not found.",
            500 => "Server error occurred. Please try again later.",
            502 => "Bad gateway. The server is temporarily unavailable.",
            503 => "Service unavailable. Please try again later.",
            _ => $"API error occurred (Status: {statusCode}). Please try again."
        };
    }

    private string GetApiErrorMessage(string endpoint, int statusCode)
    {
        var baseMessage = GetApiErrorMessage(statusCode);
        return $"Error calling {endpoint}: {baseMessage}";
    }
}

public class UserNotificationEventArgs : EventArgs
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public DateTime Timestamp { get; set; }
}