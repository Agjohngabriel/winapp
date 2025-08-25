using AutoConnect.Shared.DTOs;

namespace AutoConnect.Client.Services;

// API Service Interface
public interface IApiService
{
    Task<ApiResponse<T>> GetAsync<T>(string endpoint);
    Task<ApiResponse<T>> PostAsync<T>(string endpoint, object data);
    Task<ApiResponse<T>> PutAsync<T>(string endpoint, object data);
    Task<ApiResponse> DeleteAsync(string endpoint);
}

// Vehicle Service Interface
public interface IVehicleService
{
    Task<bool> ConnectAsync();
    Task DisconnectAsync();
    Task<string?> ReadVinAsync();
    Task<decimal?> ReadBatteryVoltageAsync();
    Task<bool> IsIgnitionOnAsync();
    event EventHandler<VehicleDataEventArgs> DataReceived;
}

// VPN Service Interface
public interface IVpnService
{
    Task<bool> ConnectAsync();
    Task DisconnectAsync();
    Task<bool> IsConnectedAsync();
    Task<string?> GetLocalIpAsync();
    Task<int> GetPingLatencyAsync();
    event EventHandler<VpnStatusEventArgs> StatusChanged;
}

// Navigation Service Interface
public interface INavigationService
{
    void NavigateTo(string viewName);
    event EventHandler<NavigationEventArgs> NavigationChanged;
}

// Event Args
public class VehicleDataEventArgs : EventArgs
{
    public string? Vin { get; set; }
    public decimal? BatteryVoltage { get; set; }
    public bool IgnitionOn { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public class VpnStatusEventArgs : EventArgs
{
    public bool IsConnected { get; set; }
    public string? LocalIp { get; set; }
    public int Latency { get; set; }
    public string? ErrorMessage { get; set; }
}

public class NavigationEventArgs : EventArgs
{
    public string ViewName { get; set; } = string.Empty;
}