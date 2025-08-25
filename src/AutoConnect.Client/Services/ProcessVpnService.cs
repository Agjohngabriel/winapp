// src/AutoConnect.Client/Services/ProcessVpnService.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace AutoConnect.Client.Services;

public class ProcessVpnService : IVpnService, IDisposable
{
    private readonly ILogger<ProcessVpnService> _logger;
    private readonly IConfiguration _configuration;
    private Process? _vpnProcess;
    private readonly Timer _statusTimer;
    private bool _isConnected;
    private string? _currentLocalIp;
    private string _vpnInterfaceName = "";
    private DateTime _connectionStartTime;
    private readonly Random _random = new();

    // Connection statistics
    private int _reconnectAttempts = 0;
    private const int MaxReconnectAttempts = 3;
    private TimeSpan _reconnectDelay = TimeSpan.FromSeconds(5);

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
            _logger.LogInformation("Initiating VPN connection...");
            _connectionStartTime = DateTime.Now;

            // Check if already connected
            if (await IsConnectedAsync())
            {
                _logger.LogInformation("VPN already connected");
                _isConnected = true;
                StartStatusMonitoring();
                return true;
            }

            var configPath = _configuration["VpnSettings:ConfigPath"] ?? "vpn-configs";
            var configFile = Path.Combine(configPath, "client.ovpn");

            if (!File.Exists(configFile))
            {
                _logger.LogWarning("VPN config file not found: {ConfigFile}", configFile);

                // Create a sample config file for development
                await CreateSampleConfigAsync(configFile);

                // For MVP, simulate connection
                return await SimulateVpnConnectionAsync();
            }

            // Try different VPN clients in order of preference
            var vpnClients = new[]
            {
                await TryWireGuardAsync(),
                await TryOpenVpnAsync(configFile),
                await TryWindowsBuiltInVpnAsync()
            };

            foreach (var result in vpnClients)
            {
                if (result)
                {
                    _isConnected = true;
                    StartStatusMonitoring();
                    await NotifyConnectionStatusAsync(true);
                    return true;
                }
            }

            // Fallback to simulation for MVP
            _logger.LogWarning("No VPN client available, falling back to simulation");
            return await SimulateVpnConnectionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to VPN");
            return false;
        }
    }

    private async Task<bool> TryWireGuardAsync()
    {
        try
        {
            var wgPath = FindWireGuardExecutable();
            if (string.IsNullOrEmpty(wgPath))
            {
                _logger.LogDebug("WireGuard not found on system");
                return false;
            }

            _logger.LogInformation("Attempting WireGuard connection...");

            // Check for WireGuard config
            var wgConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "WireGuard", "autoconnect.conf");

            if (!File.Exists(wgConfigPath))
            {
                await CreateSampleWireGuardConfigAsync(wgConfigPath);
                _logger.LogWarning("Created sample WireGuard config at {Path}. Please configure with real server details.", wgConfigPath);
                return false;
            }

            // Try to start WireGuard tunnel
            var startInfo = new ProcessStartInfo
            {
                FileName = wgPath,
                Arguments = "/installtunnelservice \"" + wgConfigPath + "\"",
                UseShellExecute = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    // Give WireGuard time to establish connection
                    await Task.Delay(3000);

                    var vpnInterface = await DetectVpnInterfaceAsync();
                    if (!string.IsNullOrEmpty(vpnInterface))
                    {
                        _vpnInterfaceName = vpnInterface;
                        _logger.LogInformation("WireGuard connected successfully via interface: {Interface}", vpnInterface);
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error attempting WireGuard connection");
            return false;
        }
    }

    private string? FindWireGuardExecutable()
    {
        var possiblePaths = new[]
        {
            @"C:\Program Files\WireGuard\wireguard.exe",
            @"C:\Program Files (x86)\WireGuard\wireguard.exe"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private async Task CreateSampleWireGuardConfigAsync(string configPath)
    {
        var configDir = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(configDir))
        {
            Directory.CreateDirectory(configDir);
        }

        var sampleConfig = @"# AutoConnect WireGuard Configuration
# Replace with your actual WireGuard server configuration

[Interface]
PrivateKey = YOUR_PRIVATE_KEY_HERE
Address = 10.26.241.3/24
DNS = 8.8.8.8

[Peer]
PublicKey = YOUR_SERVER_PUBLIC_KEY_HERE
Endpoint = your-vpn-server.com:51820
AllowedIPs = 0.0.0.0/0
PersistentKeepalive = 25

# Generate keys with: wg genkey | tee privatekey | wg pubkey > publickey
";

        await File.WriteAllTextAsync(configPath, sampleConfig);
    }

    private async Task<bool> TryOpenVpnAsync(string configFile)
    {
        var openVpnPath = FindOpenVpnExecutable();
        if (string.IsNullOrEmpty(openVpnPath))
        {
            _logger.LogDebug("OpenVPN not found on system");
            return false;
        }

        try
        {
            _logger.LogInformation("Attempting OpenVPN connection with config: {ConfigFile}", configFile);

            _vpnProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = openVpnPath,
                    Arguments = $"--config \"{configFile}\" --log-append vpn.log",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(configFile)
                }
            };

            _vpnProcess.Start();

            // Monitor process output for connection status
            var connectionEstablished = false;
            var timeout = TimeSpan.FromSeconds(15);
            var startTime = DateTime.Now;

            while (DateTime.Now - startTime < timeout && !_vpnProcess.HasExited)
            {
                await Task.Delay(500);

                // Check if VPN interface is up
                var vpnInterface = await DetectVpnInterfaceAsync();
                if (!string.IsNullOrEmpty(vpnInterface))
                {
                    _vpnInterfaceName = vpnInterface;
                    connectionEstablished = true;
                    break;
                }
            }

            if (connectionEstablished && !_vpnProcess.HasExited)
            {
                _logger.LogInformation("OpenVPN connected successfully via interface: {Interface}", _vpnInterfaceName);
                return true;
            }
            else
            {
                _logger.LogWarning("OpenVPN connection failed or timed out");
                _vpnProcess?.Kill();
                _vpnProcess?.Dispose();
                _vpnProcess = null;
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting OpenVPN process");
            _vpnProcess?.Dispose();
            _vpnProcess = null;
            return false;
        }
    }

    private string? FindOpenVpnExecutable()
    {
        var possiblePaths = new[]
        {
            @"C:\Program Files\OpenVPN\bin\openvpn.exe",
            @"C:\Program Files (x86)\OpenVPN\bin\openvpn.exe",
            @"C:\OpenVPN\bin\openvpn.exe"
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
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
                    continue;
                }
            }
        }

        return null;
    }

    private async Task<bool> TryWindowsBuiltInVpnAsync()
    {
        try
        {
            _logger.LogDebug("Checking for existing Windows VPN connections...");

            // Use PowerShell to check VPN connections
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-Command \"Get-VpnConnection | Where-Object {$_.ConnectionStatus -eq 'Connected'} | Select-Object -First 1\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrWhiteSpace(output) && output.Contains("Connected"))
                {
                    _logger.LogInformation("Found existing Windows VPN connection");
                    var vpnInterface = await DetectVpnInterfaceAsync();
                    if (!string.IsNullOrEmpty(vpnInterface))
                    {
                        _vpnInterfaceName = vpnInterface;
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking Windows built-in VPN");
            return false;
        }
    }

    private async Task<string?> DetectVpnInterfaceAsync()
    {
        try
        {
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var ni in networkInterfaces)
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                var desc = ni.Description.ToLower();
                var name = ni.Name.ToLower();

                // Check for common VPN interface patterns
                if (IsVpnInterface(desc, name))
                {
                    // Get the first IPv4 address
                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(addr.Address) &&
                            IsVpnIpAddress(addr.Address))
                        {
                            _currentLocalIp = addr.Address.ToString();
                            _logger.LogDebug("Detected VPN interface: {Name} ({Description}) with IP: {IP}",
                                ni.Name, ni.Description, _currentLocalIp);
                            return ni.Name;
                        }
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detecting VPN interface");
            return null;
        }
    }

    private bool IsVpnInterface(string description, string name)
    {
        var vpnIndicators = new[]
        {
            "tap", "tun", "wireguard", "openvpn", "vpn", "virtual", "wan miniport",
            "ppp adapter", "sstp", "ikev2", "l2tp", "pptp"
        };

        var vpnNamePrefixes = new[] { "wg", "tun", "tap", "vpn", "ppp" };

        // Check description for VPN indicators
        foreach (var indicator in vpnIndicators)
        {
            if (description.Contains(indicator))
                return true;
        }

        // Check name for VPN prefixes
        foreach (var prefix in vpnNamePrefixes)
        {
            if (name.StartsWith(prefix))
                return true;
        }

        return false;
    }

    private bool IsVpnIpAddress(IPAddress address)
    {
        // Common VPN IP ranges
        var vpnRanges = new[]
        {
            new { Network = IPAddress.Parse("10.0.0.0"), Mask = IPAddress.Parse("255.0.0.0") },      // 10.0.0.0/8
            new { Network = IPAddress.Parse("172.16.0.0"), Mask = IPAddress.Parse("255.240.0.0") },  // 172.16.0.0/12
            new { Network = IPAddress.Parse("192.168.0.0"), Mask = IPAddress.Parse("255.255.0.0") }, // 192.168.0.0/16
        };

        foreach (var range in vpnRanges)
        {
            if (IsInSubnet(address, range.Network, range.Mask))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsInSubnet(IPAddress address, IPAddress network, IPAddress mask)
    {
        var addressBytes = address.GetAddressBytes();
        var networkBytes = network.GetAddressBytes();
        var maskBytes = mask.GetAddressBytes();

        for (int i = 0; i < addressBytes.Length; i++)
        {
            if ((addressBytes[i] & maskBytes[i]) != (networkBytes[i] & maskBytes[i]))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<bool> SimulateVpnConnectionAsync()
    {
        _logger.LogWarning("Simulating VPN connection for MVP demo");

        await Task.Delay(2000 + _random.Next(1000)); // Simulate connection time with variation

        _isConnected = true;
        _currentLocalIp = "10.26.241." + _random.Next(2, 254); // Random IP in VPN range
        _vpnInterfaceName = "Simulated VPN Interface";

        StartStatusMonitoring();

        await NotifyConnectionStatusAsync(true);

        return true;
    }

    private async Task NotifyConnectionStatusAsync(bool connected)
    {
        var latency = connected ? await GetPingLatencyAsync() : 0;

        StatusChanged?.Invoke(this, new VpnStatusEventArgs
        {
            IsConnected = connected,
            LocalIp = connected ? _currentLocalIp : null,
            Latency = latency,
            ErrorMessage = connected ? null : "VPN connection lost"
        });
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
auth-nocache

# Uncomment if server certificate is not trusted
# verify-x509-name server_hostname name

# Note: This is a template. Replace with actual certificates and server details.
# To use this config, you need:
# 1. ca.crt - Certificate Authority certificate
# 2. client.crt - Client certificate  
# 3. client.key - Client private key
";

        await File.WriteAllTextAsync(configPath, sampleConfig);
        _logger.LogInformation("Created sample VPN config at {ConfigPath}", configPath);
    }

    public async Task<bool> IsConnectedAsync()
    {
        try
        {
            // If we have a process, check if it's still running
            if (_vpnProcess != null && !_vpnProcess.HasExited)
            {
                // Also verify network interface is still up
                var vpnInterface = await DetectVpnInterfaceAsync();
                return !string.IsNullOrEmpty(vpnInterface);
            }

            // For simulation mode or built-in VPN, just check interface
            if (_isConnected)
            {
                var vpnInterface = await DetectVpnInterfaceAsync();
                return !string.IsNullOrEmpty(vpnInterface);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking connection status");
            return false;
        }
    }

    public async Task<string?> GetLocalIpAsync()
    {
        try
        {
            if (!_isConnected) return null;

            // Try to get fresh IP from interface
            var vpnInterface = await DetectVpnInterfaceAsync();
            if (!string.IsNullOrEmpty(vpnInterface))
            {
                return _currentLocalIp;
            }

            return null;
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
            // Test multiple servers and return average
            var testServers = new[] { "8.8.8.8", "1.1.1.1", "208.67.222.222" };
            var latencies = new List<long>();

            using var ping = new Ping();

            foreach (var server in testServers)
            {
                try
                {
                    var reply = await ping.SendPingAsync(server, 3000);
                    if (reply.Status == IPStatus.Success)
                    {
                        latencies.Add(reply.RoundtripTime);
                    }
                }
                catch
                {
                    // Skip failed pings
                }
            }

            if (latencies.Any())
            {
                return (int)latencies.Average();
            }

            // Fallback for simulation mode
            if (_isConnected && _vpnInterfaceName == "Simulated VPN Interface")
            {
                return 15 + _random.Next(-5, 10); // Simulate 10-25ms latency
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
        _statusTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(interval));
    }

    private async void CheckConnectionStatus(object? state)
    {
        try
        {
            var wasConnected = _isConnected;
            var currentlyConnected = await IsConnectedAsync();

            if (wasConnected != currentlyConnected)
            {
                _isConnected = currentlyConnected;

                if (!currentlyConnected)
                {
                    _logger.LogWarning("VPN connection lost, attempting reconnection...");

                    // Attempt reconnection
                    if (_reconnectAttempts < MaxReconnectAttempts)
                    {
                        _reconnectAttempts++;
                        await Task.Delay(_reconnectDelay);

                        if (await ConnectAsync())
                        {
                            _logger.LogInformation("VPN reconnection successful");
                            _reconnectAttempts = 0;
                        }
                    }
                    else
                    {
                        _logger.LogError("Max reconnection attempts reached");
                    }
                }
                else
                {
                    _reconnectAttempts = 0; // Reset on successful connection
                }

                await NotifyConnectionStatusAsync(currentlyConnected);
            }
            else if (currentlyConnected)
            {
                // Refresh IP address periodically
                await DetectVpnInterfaceAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking VPN status");
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _statusTimer.Change(Timeout.Infinite, Timeout.Infinite);

            if (_vpnProcess != null && !_vpnProcess.HasExited)
            {
                try
                {
                    // Try graceful shutdown first
                    _vpnProcess.CloseMainWindow();

                    if (!_vpnProcess.WaitForExit(5000))
                    {
                        // Force kill if graceful shutdown fails
                        _vpnProcess.Kill();
                    }

                    _vpnProcess.Dispose();
                    _vpnProcess = null;
                    _logger.LogInformation("VPN process terminated");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error terminating VPN process");
                }
            }

            _isConnected = false;
            _currentLocalIp = null;
            _vpnInterfaceName = "";
            _reconnectAttempts = 0;

            await NotifyConnectionStatusAsync(false);
            _logger.LogInformation("VPN service disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during VPN disconnect");
        }
    }

    public void Dispose()
    {
        try
        {
            _statusTimer?.Dispose();
            _vpnProcess?.Dispose();
            GC.SuppressFinalize(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during VPN service disposal");
        }
    }
}