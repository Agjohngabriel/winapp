// src/AutoConnect.Client/Services/ProcessVpnService.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;

namespace AutoConnect.Client.Services;

public class ProcessVpnService : IVpnService, IDisposable
{
    private readonly ILogger<ProcessVpnService> _logger;
    private readonly IConfiguration _configuration;
    private Process? _vpnProcess;
    private readonly Timer _statusTimer;
    private bool _isConnected;

    public event EventHandler<VpnStatusEventArgs>? StatusChanged;

    public ProcessVpnService(ILogger<ProcessVpnService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _statusTimer = new Timer(CheckConnectionStatus, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            var configPath = _configuration["VpnSettings:ConfigPath"] ?? "vpn-configs";
            var configFile = Path.Combine(configPath, "client.ovpn");

            if (!File.Exists(configFile))
            {
                _logger.LogError("VPN config file not found: {ConfigFile}", configFile);

                // Create a sample config file for development
                await CreateSampleConfigAsync(configFile);

                // For MVP, simulate connection
                return await SimulateVpnConnectionAsync();
            }

            // Try to use OpenVPN if available
            var openVpnPath = FindOpenVpnExecutable();
            if (!string.IsNullOrEmpty(openVpnPath))
            {
                return await ConnectWithOpenVpnAsync(openVpnPath, configFile);
            }

            // Fallback to simulation for MVP
            return await SimulateVpnConnectionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to VPN");
            return false;
        }
    }

    private string? FindOpenVpnExecutable()
    {
        var possiblePaths = new[]
        {
            @"C:\Program Files\OpenVPN\bin\openvpn.exe",
            @"C:\Program Files (x86)\OpenVPN\bin\openvpn.exe",
            "openvpn.exe" // Assume it's in PATH
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path) || path == "openvpn.exe")
            {
                try
                {
                    var testProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });

                    testProcess?.WaitForExit(2000);
                    if (testProcess?.ExitCode == 0)
                    {
                        return path;
                    }
                }
                catch
                {
                    // Continue searching
                }
            }
        }

        return null;
    }

    private async Task<bool> ConnectWithOpenVpnAsync(string openVpnPath, string configFile)
    {
        try
        {
            _vpnProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = openVpnPath,
                    Arguments = $"--config \"{configFile}\" --log vpn.log",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            _vpnProcess.Start();

            // Wait a moment for connection
            await Task.Delay(3000);

            // Check if process is still running and connection is established
            if (!_vpnProcess.HasExited && await IsVpnConnectedAsync())
            {
                _isConnected = true;
                StartStatusMonitoring();
                StatusChanged?.Invoke(this, new VpnStatusEventArgs
                {
                    IsConnected = true,
                    LocalIp = await GetLocalIpAsync()
                });
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting OpenVPN process");
            return false;
        }
    }

    private async Task<bool> SimulateVpnConnectionAsync()
    {
        _logger.LogWarning("Simulating VPN connection for MVP demo");

        await Task.Delay(2000); // Simulate connection time

        _isConnected = true;
        StartStatusMonitoring();

        StatusChanged?.Invoke(this, new VpnStatusEventArgs
        {
            IsConnected = true,
            LocalIp = "10.26.241.3", // Simulated VPN IP
            Latency = 15
        });

        return true;
    }

    private async Task CreateSampleConfigAsync(string configPath)
    {
        var configDir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        var sampleConfig = @"# Sample OpenVPN Configuration for AutoConnect MVP
# Replace with actual server configuration

client
dev tun
proto udp
remote your-vpn-server.com 1194
resolv-retry infinite
nobind
persist-key
persist-tun
ca ca.crt
cert client.crt
key client.key
verb 3

# Note: This is a template. Replace with actual certificates and server details.
";

        await File.WriteAllTextAsync(configPath, sampleConfig);
        _logger.LogInformation("Created sample VPN config at {ConfigPath}", configPath);
    }

    private async Task<bool> IsVpnConnectedAsync()
    {
        try
        {
            // Check for VPN network interfaces
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var ni in networkInterfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    (ni.Description.Contains("TAP") || ni.Description.Contains("TUN") ||
                     ni.Name.Contains("VPN") || ni.Description.Contains("OpenVPN")))
                {
                    return true;
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> IsConnectedAsync()
    {
        if (_isConnected && _vpnProcess != null)
        {
            return !_vpnProcess.HasExited && await IsVpnConnectedAsync();
        }

        return _isConnected; // For simulated connection
    }

    public async Task<string?> GetLocalIpAsync()
    {
        try
        {
            if (!_isConnected) return null;

            // For real VPN, check VPN adapter IP
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var ni in networkInterfaces)
            {
                if (ni.OperationalStatus == OperationalStatus.Up &&
                    (ni.Description.Contains("TAP") || ni.Description.Contains("OpenVPN")))
                {
                    foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            return addr.Address.ToString();
                        }
                    }
                }
            }

            // Fallback for simulation
            return "10.26.241.3";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting local IP");
            return null;
        }
    }

    public async Task<int> GetPingLatencyAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 3000);

            if (reply.Status == IPStatus.Success)
            {
                return (int)reply.RoundtripTime;
            }

            return -1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error measuring ping latency");
            return -1;
        }
    }

    private void StartStatusMonitoring()
    {
        var interval = _configuration.GetValue<int>("VpnSettings:HeartbeatInterval", 5000);
        _statusTimer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
    }

    private async void CheckConnectionStatus(object? state)
    {
        try
        {
            var wasConnected = _isConnected;
            _isConnected = await IsConnectedAsync();

            if (wasConnected != _isConnected)
            {
                StatusChanged?.Invoke(this, new VpnStatusEventArgs
                {
                    IsConnected = _isConnected,
                    LocalIp = _isConnected ? await GetLocalIpAsync() : null,
                    Latency = _isConnected ? await GetPingLatencyAsync() : 0
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking VPN status");
        }
    }

    public async Task DisconnectAsync()
    {
        _statusTimer.Change(Timeout.Infinite, Timeout.Infinite);

        if (_vpnProcess != null && !_vpnProcess.HasExited)
        {
            try
            {
                _vpnProcess.Kill();
                _vpnProcess.Dispose();
                _vpnProcess = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error terminating VPN process");
            }
        }

        _isConnected = false;
        StatusChanged?.Invoke(this, new VpnStatusEventArgs { IsConnected = false });
    }

    public void Dispose()
    {
        _statusTimer?.Dispose();
        _vpnProcess?.Dispose();
    }
}