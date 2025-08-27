using AutoConnect.Client.Helpers;
using AutoConnect.Client.Services;
using AutoConnect.Client.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Windows;

namespace AutoConnect.Client.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IApiService _apiService;
    private readonly IVehicleService _vehicleService;
    private readonly IVpnService _vpnService;
    private readonly ILogger<MainViewModel> _logger;
    private readonly IErrorHandlingService? _errorHandlingService;
    private readonly INotificationService? _notificationService;
    private readonly IConfiguration _configuration;


    // Connection Status Properties
    [ObservableProperty]
    private string connectionStatus = "Initializing...";

    [ObservableProperty]
    private bool isVpnConnected = false;

    [ObservableProperty]
    private bool isVehicleConnected = false;

    [ObservableProperty]
    private bool isOverallConnected = false;

    // Vehicle Data Properties
    [ObservableProperty]
    private string vehicleVin = "Not Available";

    [ObservableProperty]
    private string batteryVoltage = "0.0V";

    [ObservableProperty]
    private string kl15Status = "OFF";

    [ObservableProperty]
    private string kl30Status = "OFF";

    [ObservableProperty]
    private string engineRpm = "0";

    [ObservableProperty]
    private string vehicleSpeed = "0 km/h";

    [ObservableProperty]
    private string ignitionStatus = "OFF";

    // VPN Data Properties
    [ObservableProperty]
    private string localIpAddress = "Not Available";

    [ObservableProperty]
    private int pingLatency = 0;

    [ObservableProperty]
    private string serverLocation = "Connecting...";

    [ObservableProperty]
    private string sessionDuration = "00:00:00";

    [ObservableProperty]
    private string dataUsage = "0.0 KB";

    // UI State Properties
    [ObservableProperty]
    private string currentView = "Dashboard";

    [ObservableProperty]
    private bool isConnecting = false;

    [ObservableProperty]
    private string lastUpdateTime = "";

    [ObservableProperty]
    private string connectionMode = "Unknown";

    // Statistics and Internal Fields
    private DateTime _sessionStartTime;
    private long _totalDataBytes = 0;
    private readonly Timer _uiUpdateTimer;
    private readonly Timer _sessionTimer;

    public MainViewModel(
        IApiService apiService,
        IVehicleService vehicleService,
        IVpnService vpnService,
           IConfiguration configuration,
        ILogger<MainViewModel> logger)
    {
        _apiService = apiService;
        _configuration = configuration;
        _vehicleService = vehicleService;
        _vpnService = vpnService;
        _logger = logger;

        // Try to get optional services - using a safer approach
        try
        {
            if (Application.Current is App app && app.ServiceProvider != null)
            {
                _errorHandlingService = app.ServiceProvider.GetService<IErrorHandlingService>();
                _notificationService = app.ServiceProvider.GetService<INotificationService>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not resolve optional services");
        }

        // Subscribe to events
        _vpnService.StatusChanged += OnVpnStatusChanged;
        _vehicleService.DataReceived += OnVehicleDataReceived;

        // Start UI update timer
        _uiUpdateTimer = new Timer(UpdateUi, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        _sessionTimer = new Timer(UpdateSessionDuration, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

        // Initialize connection with error handling
        _ = InitializeWithErrorHandlingAsync();
    }

    private async Task InitializeWithErrorHandlingAsync()
    {
        try
        {
            await InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initialization");

            if (_errorHandlingService != null)
            {
                await _errorHandlingService.HandleErrorAsync("Initialization", ex, ErrorSeverity.Error);
            }

            ConnectionStatus = "Initialization failed";
            IsConnecting = false;
            UpdateLastUpdateTime();
        }
    }

    private async Task InitializeAsync()
    {
        try
        {
            IsConnecting = true;
            ConnectionStatus = "Connecting to VPN...";
            _sessionStartTime = DateTime.Now;

            // Connect to VPN first
            var vpnConnected = await _vpnService.ConnectAsync();
            IsVpnConnected = vpnConnected;

            if (vpnConnected)
            {
                LocalIpAddress = await _vpnService.GetLocalIpAsync() ?? "Unknown";
                PingLatency = await _vpnService.GetPingLatencyAsync();
                ServerLocation = DetermineServerLocation(LocalIpAddress);

                ConnectionStatus = "VPN Connected - Connecting to vehicle...";

                // Connect to vehicle
                var vehicleConnected = await _vehicleService.ConnectAsync();
                IsVehicleConnected = vehicleConnected;

                if (vehicleConnected)
                {
                    // Initial vehicle data read
                    await RefreshVehicleDataAsync();

                    ConnectionStatus = "Fully Connected";
                    IsOverallConnected = true;
                    ConnectionMode = "Hardware + VPN";
                }
                else
                {
                    ConnectionStatus = "VPN Connected - Vehicle connection failed";
                    ConnectionMode = "VPN Only";
                }
            }
            else
            {
                ConnectionStatus = "Connection failed";
                ConnectionMode = "Offline";
            }

            IsConnecting = false;
            UpdateLastUpdateTime();

            _logger.LogInformation("Initialization completed - VPN: {VpnStatus}, Vehicle: {VehicleStatus}",
                IsVpnConnected, IsVehicleConnected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initialization");
            ConnectionStatus = "Connection error";
            IsConnecting = false;
            UpdateLastUpdateTime();
            throw; // Re-throw for error handling wrapper
        }
    }

    private void OnVpnStatusChanged(object? sender, VpnStatusEventArgs e)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                IsVpnConnected = e.IsConnected;

                if (e.IsConnected)
                {
                    LocalIpAddress = e.LocalIp ?? "Unknown";
                    PingLatency = e.Latency;
                    ServerLocation = DetermineServerLocation(LocalIpAddress);

                    if (IsVehicleConnected)
                    {
                        ConnectionStatus = "Fully Connected";
                        IsOverallConnected = true;
                    }
                    else
                    {
                        ConnectionStatus = "VPN Connected - Vehicle disconnected";
                        IsOverallConnected = false;
                    }
                }
                else
                {
                    LocalIpAddress = "Not Available";
                    PingLatency = 0;
                    ServerLocation = "Disconnected";
                    ConnectionStatus = "VPN Disconnected";
                    IsOverallConnected = false;

                    if (!string.IsNullOrEmpty(e.ErrorMessage))
                    {
                        _logger.LogWarning("VPN connection issue: {Error}", e.ErrorMessage);
                    }
                }

                UpdateConnectionMode();
                UpdateLastUpdateTime();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling VPN status change");
        }
    }

    private void OnVehicleDataReceived(object? sender, VehicleDataEventArgs e)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Update vehicle data
                if (!string.IsNullOrEmpty(e.Vin) && e.Vin != "VIN_READ_FAILED")
                {
                    VehicleVin = e.Vin;
                }

                if (e.BatteryVoltage.HasValue)
                {
                    BatteryVoltage = $"{e.BatteryVoltage:F1}V";

                    // Update KL30 status based on voltage (KL30 = constant power)
                    Kl30Status = e.BatteryVoltage > 11.0m ? "ON" : "OFF";
                }

                // Update ignition status
                IgnitionStatus = e.IgnitionOn ? "ON" : "OFF";
                Kl15Status = e.IgnitionOn ? "ON" : "OFF"; // KL15 = ignition switched power

                // Simulate some additional data for demo
                if (e.IgnitionOn)
                {
                    EngineRpm = Random.Shared.Next(650, 2200).ToString();
                    VehicleSpeed = Random.Shared.Next(0, 120).ToString() + " km/h";
                }
                else
                {
                    EngineRpm = "0";
                    VehicleSpeed = "0 km/h";
                }

                // Mark as vehicle connected if we're receiving data
                if (!IsVehicleConnected)
                {
                    IsVehicleConnected = true;
                    UpdateConnectionStatus();
                    UpdateConnectionMode();
                }

                // Update data usage simulation
                _totalDataBytes += Random.Shared.Next(100, 500);
                UpdateDataUsage();
                UpdateLastUpdateTime();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling vehicle data");
        }
    }

    private void UpdateConnectionStatus()
    {
        if (IsVpnConnected && IsVehicleConnected)
        {
            ConnectionStatus = "Fully Connected";
            IsOverallConnected = true;
        }
        else if (IsVpnConnected && !IsVehicleConnected)
        {
            ConnectionStatus = "VPN Connected - Vehicle disconnected";
            IsOverallConnected = false;
        }
        else if (!IsVpnConnected && IsVehicleConnected)
        {
            ConnectionStatus = "Vehicle Connected - VPN disconnected";
            IsOverallConnected = false;
        }
        else
        {
            ConnectionStatus = "Disconnected";
            IsOverallConnected = false;
        }
    }

    private void UpdateConnectionMode()
    {
        if (IsVpnConnected && IsVehicleConnected)
        {
            ConnectionMode = VehicleVin.Contains("Simulated") || VehicleVin.Contains("BMW") ? "Simulation + VPN" : "Hardware + VPN";
        }
        else if (IsVpnConnected)
        {
            ConnectionMode = "VPN Only";
        }
        else if (IsVehicleConnected)
        {
            ConnectionMode = "Vehicle Only";
        }
        else
        {
            ConnectionMode = "Offline";
        }
    }

    private string DetermineServerLocation(string ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress) || ipAddress == "Not Available")
            return "Unknown";

        // Simple geolocation based on IP ranges (for demo purposes)
        if (ipAddress.StartsWith("10.26."))
            return "United Kingdom";
        else if (ipAddress.StartsWith("10.27."))
            return "Germany";
        else if (ipAddress.StartsWith("10.28."))
            return "United States";
        else if (ipAddress.StartsWith("192.168."))
            return "Local Network";
        else
            return "Unknown Location";
    }

    private void UpdateDataUsage()
    {
        if (_totalDataBytes < 1024)
        {
            DataUsage = $"{_totalDataBytes} B";
        }
        else if (_totalDataBytes < 1024 * 1024)
        {
            DataUsage = $"{_totalDataBytes / 1024.0:F1} KB";
        }
        else
        {
            DataUsage = $"{_totalDataBytes / (1024.0 * 1024.0):F1} MB";
        }
    }

    private void UpdateLastUpdateTime()
    {
        LastUpdateTime = DateTime.Now.ToString("HH:mm:ss");
    }

    private void UpdateUi(object? state)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    // Update ping latency periodically if connected
                    if (IsVpnConnected)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var newLatency = await _vpnService.GetPingLatencyAsync();
                                if (newLatency > 0)
                                {
                                    Application.Current.Dispatcher.Invoke(() => PingLatency = newLatency);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Error updating ping latency");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in UI update");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating UI");
        }
    }

    private void UpdateSessionDuration(object? state)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var duration = DateTime.Now - _sessionStartTime;
                SessionDuration = $"{duration.Hours:D2}:{duration.Minutes:D2}:{duration.Seconds:D2}";
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error updating session duration");
        }
    }

    private async Task RefreshVehicleDataAsync()
    {
        try
        {
            if (IsVehicleConnected)
            {
                // These calls will trigger the DataReceived event
                var vin = await _vehicleService.ReadVinAsync();
                var voltage = await _vehicleService.ReadBatteryVoltageAsync();
                var ignitionOn = await _vehicleService.IsIgnitionOnAsync();

                // Manual update if event doesn't fire
                if (!string.IsNullOrEmpty(vin))
                    VehicleVin = vin;
                if (voltage.HasValue)
                    BatteryVoltage = $"{voltage:F1}V";

                IgnitionStatus = ignitionOn ? "ON" : "OFF";
                Kl15Status = ignitionOn ? "ON" : "OFF";
                Kl30Status = voltage > 11.0m ? "ON" : "OFF";

                UpdateLastUpdateTime();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing vehicle data");
            throw; // Re-throw for command error handling
        }
    }

    [RelayCommand]
    private void NavigateTo(string viewName)
    {
        try
        {
            CurrentView = viewName;
            _logger.LogInformation("Navigated to {ViewName}", viewName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error navigating to {ViewName}", viewName);
        }
    }

    [RelayCommand]
    private async Task RefreshData()
    {
        try
        {
            UpdateLastUpdateTime();

            if (IsVpnConnected)
            {
                var newIp = await _vpnService.GetLocalIpAsync();
                if (!string.IsNullOrEmpty(newIp))
                    LocalIpAddress = newIp;

                PingLatency = await _vpnService.GetPingLatencyAsync();
            }

            await RefreshVehicleDataAsync();

            // Show success notification
            if (_notificationService != null)
            {
                await _notificationService.ShowTemporaryNotificationAsync("Data refreshed successfully", NotificationType.Success, 2);
            }

            _logger.LogInformation("Data refreshed manually");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing data");

            if (_errorHandlingService != null)
            {
                await _errorHandlingService.HandleErrorAsync("Data Refresh", ex, ErrorSeverity.Warning);
            }
        }
    }

    [RelayCommand]
    private async Task Reconnect()
    {
        try
        {
            IsConnecting = true;
            ConnectionStatus = "Reconnecting...";

            // Disconnect first
            if (IsVpnConnected)
                await _vpnService.DisconnectAsync();

            if (IsVehicleConnected)
                await _vehicleService.DisconnectAsync();

            // Wait a moment
            await Task.Delay(2000);

            // Reconnect
            await InitializeAsync();

            // Show success notification
            if (_notificationService != null)
            {
                await _notificationService.ShowTemporaryNotificationAsync("Reconnection successful", NotificationType.Success, 3);
            }

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during reconnection");
            ConnectionStatus = "Reconnection failed";
            IsConnecting = false;

            if (_errorHandlingService != null)
            {
                await _errorHandlingService.HandleErrorAsync("Reconnection", ex, ErrorSeverity.Error);
            }
        }
    }

    [RelayCommand]
    private async Task Disconnect()
    {
        try
        {
            ConnectionStatus = "Disconnecting...";

            await _vpnService.DisconnectAsync();
            await _vehicleService.DisconnectAsync();

            IsVpnConnected = false;
            IsVehicleConnected = false;
            IsOverallConnected = false;
            ConnectionStatus = "Disconnected";
            ConnectionMode = "Offline";

            // Reset data
            LocalIpAddress = "Not Available";
            VehicleVin = "Not Available";
            BatteryVoltage = "0.0V";
            IgnitionStatus = "OFF";
            Kl15Status = "OFF";
            Kl30Status = "OFF";
            EngineRpm = "0";
            PingLatency = 0;
            ServerLocation = "Disconnected";

            UpdateLastUpdateTime();
            _logger.LogInformation("Disconnected all services");

            // Show notification
            if (_notificationService != null)
            {
                await _notificationService.ShowTemporaryNotificationAsync("Disconnected successfully", NotificationType.Info, 2);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect");

            if (_errorHandlingService != null)
            {
                await _errorHandlingService.HandleErrorAsync("Disconnect", ex, ErrorSeverity.Warning);
            }
        }
    }

    [RelayCommand]
    private async Task OpenVpnSetup()
    {
        try
        {
            // Get required services
            if (Application.Current is App app && app.ServiceProvider != null)
            {
                var logger = app.ServiceProvider.GetService<ILogger<OpenVpnSetupWindow>>();
                var configuration = app.ServiceProvider.GetRequiredService<IConfiguration>();

                // Create config helper
                var configHelperLogger = app.ServiceProvider.GetService<ILogger<OpenVpnConfigHelper>>();
                var configHelper = new OpenVpnConfigHelper(configHelperLogger!);

                var setupWindow = new OpenVpnSetupWindow(logger!, configuration, configHelper)
                {
                    Owner = Application.Current.MainWindow
                };

                var result = setupWindow.ShowDialog();

                if (result == true && setupWindow.ConfigurationSaved)
                {
                    // Configuration was saved, we might want to reconnect VPN
                    if (_notificationService != null)
                    {
                        await _notificationService.ShowTemporaryNotificationAsync(
                            "OpenVPN configuration updated. Reconnecting...",
                            NotificationType.Info, 3);
                    }

                    // Disconnect current VPN and reconnect with new config
                    if (IsVpnConnected)
                    {
                        await _vpnService.DisconnectAsync();
                        await Task.Delay(2000); // Brief delay

                        var reconnected = await _vpnService.ConnectAsync();
                        if (reconnected && _notificationService != null)
                        {
                            await _notificationService.ShowTemporaryNotificationAsync(
                                "Successfully connected with new OpenVPN configuration",
                                NotificationType.Success, 3);
                        }
                    }

                    _logger.LogInformation("OpenVPN configuration updated via setup window");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error opening VPN setup window");

            if (_errorHandlingService != null)
            {
                await _errorHandlingService.HandleErrorAsync("VPN Setup", ex, ErrorSeverity.Warning);
            }
        }
    }

    // Also add this method to check VPN setup status
    private async Task<bool> CheckVpnConfigurationAsync()
    {
        try
        {
            var configPath = _configuration["VpnSettings:ConfigPath"] ??
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vpn-configs", "client.ovpn");

            if (!File.Exists(configPath))
            {
                _logger.LogWarning("OpenVPN configuration file not found: {ConfigPath}", configPath);
                return false;
            }

            // Basic validation - just check if file has required content
            var content = await File.ReadAllTextAsync(configPath);
            return content.Contains("client") && content.Contains("remote");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking VPN configuration");
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            _uiUpdateTimer?.Dispose();
            _sessionTimer?.Dispose();

            // Unsubscribe from events
            _vpnService.StatusChanged -= OnVpnStatusChanged;
            _vehicleService.DataReceived -= OnVehicleDataReceived;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during MainViewModel disposal");
        }
    }
}