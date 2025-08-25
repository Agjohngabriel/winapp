// src/AutoConnect.Client/Services/NotificationService.cs
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;

namespace AutoConnect.Client.Services;

public interface INotificationService
{
    ObservableCollection<NotificationItem> Notifications { get; }
    Task ShowNotificationAsync(string title, string message, NotificationType type, int durationSeconds = 5);
    Task ShowTemporaryNotificationAsync(string message, NotificationType type = NotificationType.Info, int durationSeconds = 3);
    void ClearNotification(NotificationItem notification);
    void ClearAllNotifications();
    event EventHandler<NotificationItem>? NotificationAdded;
}

public class NotificationItem : INotifyPropertyChanged
{
    private bool _isVisible = true;
    private double _opacity = 1.0;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public NotificationType Type { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public int DurationSeconds { get; set; } = 5;

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    public double Opacity
    {
        get => _opacity;
        set
        {
            _opacity = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Opacity)));
        }
    }

    public string Icon => Type switch
    {
        NotificationType.Success => "✅",
        NotificationType.Warning => "⚠️",
        NotificationType.Error => "❌",
        NotificationType.Info => "ℹ️",
        _ => "ℹ️"
    };

    public string TypeColor => Type switch
    {
        NotificationType.Success => "#22C55E",
        NotificationType.Warning => "#F59E0B",
        NotificationType.Error => "#EF4444",
        NotificationType.Info => "#3B82F6",
        _ => "#6B7280"
    };

    public string FormattedTimestamp => Timestamp.ToString("HH:mm:ss");

    public event PropertyChangedEventHandler? PropertyChanged;
}

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private readonly DispatcherTimer _cleanupTimer;
    private const int MaxNotifications = 10;

    public ObservableCollection<NotificationItem> Notifications { get; } = new();
    public event EventHandler<NotificationItem>? NotificationAdded;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;

        // Setup cleanup timer to remove old notifications
        _cleanupTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _cleanupTimer.Tick += CleanupTimer_Tick;
        _cleanupTimer.Start();
    }

    public async Task ShowNotificationAsync(string title, string message, NotificationType type, int durationSeconds = 5)
    {
        try
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var notification = new NotificationItem
                {
                    Title = title,
                    Message = message,
                    Type = type,
                    DurationSeconds = durationSeconds
                };

                // Add to beginning of collection (most recent first)
                Notifications.Insert(0, notification);

                // Trim notifications if too many
                while (Notifications.Count > MaxNotifications)
                {
                    Notifications.RemoveAt(Notifications.Count - 1);
                }

                NotificationAdded?.Invoke(this, notification);

                _logger.LogDebug("Showed notification: {Title} - {Message} ({Type})", title, message, type);

                // Auto-remove after duration (if not permanent)
                if (durationSeconds > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(durationSeconds * 1000);
                        await FadeOutNotificationAsync(notification);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error showing notification: {Title}", title);
        }
    }

    public async Task ShowTemporaryNotificationAsync(string message, NotificationType type = NotificationType.Info, int durationSeconds = 3)
    {
        await ShowNotificationAsync("", message, type, durationSeconds);
    }

    public void ClearNotification(NotificationItem notification)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                if (Notifications.Contains(notification))
                {
                    Notifications.Remove(notification);
                    _logger.LogDebug("Cleared notification: {Title}", notification.Title);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing notification");
        }
    }

    public void ClearAllNotifications()
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var count = Notifications.Count;
                Notifications.Clear();
                _logger.LogDebug("Cleared {Count} notifications", count);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing all notifications");
        }
    }

    private async Task FadeOutNotificationAsync(NotificationItem notification)
    {
        try
        {
            if (!Notifications.Contains(notification)) return;

            // Fade out animation
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                for (double opacity = 1.0; opacity >= 0; opacity -= 0.1)
                {
                    notification.Opacity = opacity;
                    await Task.Delay(50);
                }

                notification.IsVisible = false;
                await Task.Delay(500); // Wait a bit before removing

                if (Notifications.Contains(notification))
                {
                    Notifications.Remove(notification);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fading out notification");
        }
    }

    private void CleanupTimer_Tick(object? sender, EventArgs e)
    {
        try
        {
            var now = DateTime.Now;
            var itemsToRemove = new List<NotificationItem>();

            foreach (var notification in Notifications.ToList())
            {
                var age = now - notification.Timestamp;
                if (age.TotalSeconds > notification.DurationSeconds + 5) // Add buffer time
                {
                    itemsToRemove.Add(notification);
                }
            }

            foreach (var item in itemsToRemove)
            {
                Notifications.Remove(item);
            }

            if (itemsToRemove.Any())
            {
                _logger.LogDebug("Cleaned up {Count} old notifications", itemsToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during notification cleanup");
        }
    }
}