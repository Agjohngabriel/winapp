using AutoConnect.Client.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace AutoConnect.Client.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IApiService _apiService;
    private readonly IVehicleService _vehicleService;
    private readonly IVpnService _vpnService;
    private readonly ILogger<MainViewModel> _logger;

    [ObservableProperty]
    private string connectionStatus = "Connecting...";

    [ObservableProperty]
    private string vehicleVin = "Not Available";

    [ObservableProperty]
    private string batteryVoltage = "0.0V";

    [ObservableProperty]
    private string localIpAddress = "Not Available";

    [ObservableProperty]
    private int pingLatency = 0;

    [ObservableProperty]
    private bool isConnected = false;

    [ObservableProperty]
    private string currentView = "Dashboard";

    public MainViewModel(
        IApiService apiService,
        IVehicleService vehicleService,
        IVpnService vpnService,
        ILogger<MainViewModel> logger)
    {
        _apiService = apiService;
        _vehicleService = vehicleService;
        _vpnService = vpnService;
        _logger = logger;

        // Subscribe to events
        _vpnService.StatusChanged += OnVpnStatusChanged;
        _vehicleService.DataReceived += OnVehicleDataReceived;

        // Initialize connection
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            ConnectionStatus = "Connecting to VPN...";
            
            // Connect to VPN
            var vpnConnected = await _vpnService.ConnectAsync();
            if (vpnConnected)
            {
                LocalIpAddress = await _vpnService.GetLocalIpAsync() ?? "Unknown";
                PingLatency = await _vpnService.GetPingLatencyAsync();
                
                ConnectionStatus = "Connecting to vehicle...";
                
                // Connect to vehicle
                var vehicleConnected = await _vehicleService.ConnectAsync();
                if (vehicleConnected)
                {
                    VehicleVin = await _vehicleService.ReadVinAsync() ?? "Unknown";
                    var voltage = await _vehicleService.ReadBatteryVoltageAsync();
                    BatteryVoltage = voltage.HasValue ? $"{voltage:F1}V" : "N/A";
                    
                    ConnectionStatus = "Connected";
                    IsConnected = true;
                }
                else
                {
                    ConnectionStatus = "Vehicle connection failed";
                }
            }
            else
            {
                ConnectionStatus = "VPN connection failed";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during initialization");
            ConnectionStatus = "Connection error";
        }
    }

    private void OnVpnStatusChanged(object? sender, VpnStatusEventArgs e)
    {
        IsConnected = e.IsConnected;
        if (e.IsConnected)
        {
            LocalIpAddress = e.LocalIp ?? "Unknown";
            PingLatency = e.Latency;
            ConnectionStatus = "Connected";
        }
        else
        {
            ConnectionStatus = "Disconnected";
        }
    }

    private void OnVehicleDataReceived(object? sender, VehicleDataEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Vin))
            VehicleVin = e.Vin;
        
        if (e.BatteryVoltage.HasValue)
            BatteryVoltage = $"{e.BatteryVoltage:F1}V";
    }

    [RelayCommand]
    private void NavigateTo(string viewName)
    {
        CurrentView = viewName;
        _logger.LogInformation("Navigated to {ViewName}", viewName);
    }

    [RelayCommand]
    private async Task RefreshData()
    {
        try
        {
            if (IsConnected)
            {
                var voltage = await _vehicleService.ReadBatteryVoltageAsync();
                BatteryVoltage = voltage.HasValue ? $"{voltage:F1}V" : "N/A";
                
                PingLatency = await _vpnService.GetPingLatencyAsync();
                
                _logger.LogInformation("Data refreshed");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing data");
        }
    }
}