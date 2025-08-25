using AutoConnect.Shared.DTOs;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;

namespace AutoConnect.Client.Services;

// API Service Implementation
public class ApiService : IApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ApiService> _logger;

    public ApiService(HttpClient httpClient, ILogger<ApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ApiResponse<T>> GetAsync<T>(string endpoint)
    {
        try
        {
            var response = await _httpClient.GetAsync(endpoint);
            var content = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                var result = JsonConvert.DeserializeObject<ApiResponse<T>>(content);
                return result ?? ApiResponse<T>.ErrorResult("Failed to deserialize response");
            }
            
            return ApiResponse<T>.ErrorResult($"API Error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling GET {Endpoint}", endpoint);
            return ApiResponse<T>.ErrorResult($"Network error: {ex.Message}");
        }
    }

    public async Task<ApiResponse<T>> PostAsync<T>(string endpoint, object data)
    {
        try
        {
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PostAsync(endpoint, content);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                var result = JsonConvert.DeserializeObject<ApiResponse<T>>(responseContent);
                return result ?? ApiResponse<T>.ErrorResult("Failed to deserialize response");
            }
            
            return ApiResponse<T>.ErrorResult($"API Error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling POST {Endpoint}", endpoint);
            return ApiResponse<T>.ErrorResult($"Network error: {ex.Message}");
        }
    }

    public async Task<ApiResponse<T>> PutAsync<T>(string endpoint, object data)
    {
        try
        {
            var json = JsonConvert.SerializeObject(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            
            var response = await _httpClient.PutAsync(endpoint, content);
            var responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                var result = JsonConvert.DeserializeObject<ApiResponse<T>>(responseContent);
                return result ?? ApiResponse<T>.ErrorResult("Failed to deserialize response");
            }
            
            return ApiResponse<T>.ErrorResult($"API Error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling PUT {Endpoint}", endpoint);
            return ApiResponse<T>.ErrorResult($"Network error: {ex.Message}");
        }
    }

    public async Task<ApiResponse> DeleteAsync(string endpoint)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(endpoint);
            
            if (response.IsSuccessStatusCode)
            {
                return ApiResponse.CreateSuccess("Deleted successfully");
            }
            
            return ApiResponse.CreateError($"API Error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling DELETE {Endpoint}", endpoint);
            return ApiResponse.CreateError($"Network error: {ex.Message}");
        }
    }
}

// Vehicle Service Implementation (Stub for now)
public class VehicleService : IVehicleService
{
    private readonly ILogger<VehicleService> _logger;
    public event EventHandler<VehicleDataEventArgs>? DataReceived;

    public VehicleService(ILogger<VehicleService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ConnectAsync()
    {
        await Task.Delay(1000); // Simulate connection time
        _logger.LogInformation("Vehicle service connected (simulated)");
        return true;
    }

    public async Task DisconnectAsync()
    {
        await Task.Delay(500);
        _logger.LogInformation("Vehicle service disconnected");
    }

    public async Task<string?> ReadVinAsync()
    {
        await Task.Delay(100);
        return "WBAXA72010DN13703"; // Simulated VIN
    }

    public async Task<decimal?> ReadBatteryVoltageAsync()
    {
        await Task.Delay(100);
        return 12.6m; // Simulated voltage
    }

    public async Task<bool> IsIgnitionOnAsync()
    {
        await Task.Delay(100);
        return true; // Simulated ignition status
    }
}

// VPN Service Implementation (Stub for now)
public class VpnService : IVpnService
{
    private readonly ILogger<VpnService> _logger;
    public event EventHandler<VpnStatusEventArgs>? StatusChanged;

    public VpnService(ILogger<VpnService> logger)
    {
        _logger = logger;
    }

    public async Task<bool> ConnectAsync()
    {
        await Task.Delay(2000); // Simulate connection time
        _logger.LogInformation("VPN connected (simulated)");
        StatusChanged?.Invoke(this, new VpnStatusEventArgs { IsConnected = true, LocalIp = "10.26.241.3" });
        return true;
    }

    public async Task DisconnectAsync()
    {
        await Task.Delay(500);
        _logger.LogInformation("VPN disconnected");
        StatusChanged?.Invoke(this, new VpnStatusEventArgs { IsConnected = false });
    }

    public async Task<bool> IsConnectedAsync()
    {
        await Task.Delay(10);
        return true; // Simulated connection status
    }

    public async Task<string?> GetLocalIpAsync()
    {
        await Task.Delay(10);
        return "10.26.241.3"; // Simulated IP
    }

    public async Task<int> GetPingLatencyAsync()
    {
        await Task.Delay(10);
        return 19; // Simulated latency
    }
}

// Navigation Service Implementation
public class NavigationService : INavigationService
{
    public event EventHandler<NavigationEventArgs>? NavigationChanged;

    public void NavigateTo(string viewName)
    {
        NavigationChanged?.Invoke(this, new NavigationEventArgs { ViewName = viewName });
    }
}