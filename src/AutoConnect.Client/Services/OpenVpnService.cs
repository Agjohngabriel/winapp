// src/AutoConnect.Client/Services/OpenVpnService.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace AutoConnect.Client.Services;

public class OpenVpnService : IVpnService, IDisposable
{
    private readonly ILogger<OpenVpnService> _logger;
    private readonly IConfiguration _configuration;
    private Process? _openVpnProcess;
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

    // OpenVPN specific settings
    private string _openVpnPath = "";
    private string _configPath = "";
    private string _logFilePath = "";
    private readonly List<string> _connectionLog = new();

    public event EventHandler<VpnStatusEventArgs>? StatusChanged;

    public OpenVpnService(ILogger<OpenVpnService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
        _statusTimer = new Timer(CheckConnectionStatus, null, Timeout.Infinite, Timeout.Infinite);

        InitializePaths();
    }

    private void InitializePaths()
    {
        // Get paths from configuration
        var configDir = _configuration["VpnSettings:ConfigPath"] ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vpn-configs");
        _configPath = Path.Combine(configDir, "client.ovpn");

        // Setup log file path
        var logsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDir);
        _logFilePath = Path.Combine(logsDir, $"openvpn-{DateTime.Now:yyyy-MM-dd}.log");

        _logger.LogInformation("OpenVPN config path: {ConfigPath}", _configPath);
        _logger.LogInformation("OpenVPN log path: {LogPath}", _logFilePath);
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            _logger.LogInformation("Initiating OpenVPN connection...");
            _connectionStartTime = DateTime.Now;

            // Check if already connected
            if (await IsConnectedAsync())
            {
                _logger.LogInformation("OpenVPN already connected");
                _isConnected = true;
                StartStatusMonitoring();
                return true;
            }

            // Find OpenVPN executable
            _openVpnPath = FindOpenVpnExecutable();
            if (string.IsNullOrEmpty(_openVpnPath))
            {
                _logger.LogError("OpenVPN executable not found. Please install OpenVPN.");
                return false;
            }

            // Ensure TAP adapter is installed
            if (!await EnsureTapAdapterAsync())
            {
                _logger.LogError("Failed to install or find TAP adapter.");
                return false;
            }

            // Verify config file exists
            if (!File.Exists(_configPath))
            {
                _logger.LogError("OpenVPN config file not found: {ConfigPath}", _configPath);
                return false;
            }

            _logger.LogInformation("Using OpenVPN: {OpenVpnPath}", _openVpnPath);
            _logger.LogInformation("Using config: {ConfigPath}", _configPath);

            // Validate config file
            if (!await ValidateConfigFileAsync())
            {
                _logger.LogError("OpenVPN config file validation failed");
                return false;
            }

            // Start OpenVPN process
            if (await StartOpenVpnProcessAsync())
            {
                _isConnected = true;
                StartStatusMonitoring();
                await NotifyConnectionStatusAsync(true);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to OpenVPN");
            return false;
        }
    }

    private string FindOpenVpnExecutable()
    {
        var possiblePaths = new[]
        {
            // Bundled OpenVPN (first priority - in application directory)
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "openvpn", "openvpn.exe"),
            
            // System OpenVPN installations
            @"C:\Program Files\OpenVPN\bin\openvpn.exe",
            @"C:\Program Files (x86)\OpenVPN\bin\openvpn.exe",
            @"C:\OpenVPN\bin\openvpn.exe",
            @"openvpn.exe" // Try system PATH
        };

        foreach (var path in possiblePaths)
        {
            try
            {
                if (path == "openvpn.exe")
                {
                    // Test if openvpn is in system PATH
                    var testProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "openvpn",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });

                    if (testProcess != null)
                    {
                        testProcess.WaitForExit(3000);
                        if (testProcess.ExitCode == 0 || testProcess.ExitCode == 1) // OpenVPN --version exits with code 1
                        {
                            return "openvpn";
                        }
                    }
                }
                else if (File.Exists(path))
                {
                    // Test the executable
                    var testProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = path,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    });

                    if (testProcess != null)
                    {
                        testProcess.WaitForExit(3000);
                        if (testProcess.ExitCode == 0 || testProcess.ExitCode == 1)
                        {
                            _logger.LogInformation("Found OpenVPN at: {Path}", path);
                            return path;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Failed to test OpenVPN path {Path}: {Error}", path, ex.Message);
            }
        }

        return string.Empty;
    }

    private async Task<bool> ValidateConfigFileAsync()
    {
        try
        {
            var configContent = await File.ReadAllTextAsync(_configPath);

            // Basic validation - check for required directives
            var requiredDirectives = new[] { "remote", "ca", "cert", "key" };
            var hasInlineCredentials = configContent.Contains("<ca>") && configContent.Contains("<cert>") && configContent.Contains("<key>");

            if (!hasInlineCredentials)
            {
                foreach (var directive in requiredDirectives)
                {
                    if (!configContent.Contains(directive))
                    {
                        _logger.LogError("Config file missing required directive: {Directive}", directive);
                        return false;
                    }
                }
            }

            // Check for client directive
            if (!configContent.Contains("client"))
            {
                _logger.LogWarning("Config file should contain 'client' directive");
            }

            // Check for protocol and port
            if (!configContent.Contains("proto") || !configContent.Contains("port"))
            {
                _logger.LogWarning("Config file missing protocol or port specification");
            }

            _logger.LogInformation("OpenVPN config file validation passed");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating config file");
            return false;
        }
    }

    private async Task<bool> StartOpenVpnProcessAsync()
    {
        try
        {
            _logger.LogInformation("Starting OpenVPN process...");

            // Prepare command arguments
            var arguments = BuildOpenVpnArguments();

            _openVpnProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _openVpnPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                }
            };

            // Set up event handlers for process output
            _openVpnProcess.OutputDataReceived += OnOpenVpnOutputReceived;
            _openVpnProcess.ErrorDataReceived += OnOpenVpnErrorReceived;
            _openVpnProcess.Exited += OnOpenVpnProcessExited;
            _openVpnProcess.EnableRaisingEvents = true;

            // Start the process
            _openVpnProcess.Start();
            _openVpnProcess.BeginOutputReadLine();
            _openVpnProcess.BeginErrorReadLine();

            _logger.LogInformation("OpenVPN process started with PID: {ProcessId}", _openVpnProcess.Id);

            // Wait for connection to establish
            var connectionEstablished = await WaitForConnectionAsync();

            if (connectionEstablished)
            {
                _logger.LogInformation("OpenVPN connection established successfully");
                return true;
            }
            else
            {
                _logger.LogError("OpenVPN connection failed to establish");
                await StopOpenVpnProcessAsync();
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting OpenVPN process");
            await StopOpenVpnProcessAsync();
            return false;
        }
    }

    private string BuildOpenVpnArguments()
    {
        var args = new List<string>
        {
            $"--config \"{_configPath}\"",
            $"--log-append \"{_logFilePath}\"",
            "--verb 4", // More verbose logging
            "--status-version 3",
            "--suppress-timestamps", // Cleaner log parsing
            "--management 127.0.0.1 7505", // Management interface for monitoring
            "--data-ciphers AES-256-GCM:AES-128-GCM:AES-128-CBC:CHACHA20-POLY1305", // Modern cipher support
            "--allow-compression no" // Disable compression warnings
        };

        // Add platform-specific arguments for Windows
        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
        {
            args.Add("--route-method exe");
            args.Add("--redirect-gateway def1");
        }

        var argumentString = string.Join(" ", args);
        _logger.LogDebug("OpenVPN arguments: {Arguments}", argumentString);

        return argumentString;
    }

    private async Task<bool> WaitForConnectionAsync()
    {
        var timeout = TimeSpan.FromSeconds(30); // 30 second timeout
        var startTime = DateTime.Now;
        var checkInterval = TimeSpan.FromMilliseconds(500);

        while (DateTime.Now - startTime < timeout)
        {
            // Check if process is still running
            if (_openVpnProcess == null || _openVpnProcess.HasExited)
            {
                _logger.LogError("OpenVPN process exited during connection attempt");
                return false;
            }

            // Check connection log for success indicators
            if (await CheckConnectionLogForSuccessAsync())
            {
                // Double-check by detecting VPN interface
                var vpnInterface = await DetectVpnInterfaceAsync();
                if (!string.IsNullOrEmpty(vpnInterface))
                {
                    _vpnInterfaceName = vpnInterface;
                    return true;
                }
            }

            // Check for connection errors in log
            if (await CheckConnectionLogForErrorsAsync())
            {
                _logger.LogError("OpenVPN connection failed - check logs for details");
                return false;
            }

            await Task.Delay(checkInterval);
        }

        _logger.LogError("OpenVPN connection timeout after {Timeout} seconds", timeout.TotalSeconds);
        return false;
    }

    private async Task<bool> CheckConnectionLogForSuccessAsync()
    {
        try
        {
            var successIndicators = new[]
            {
                "Initialization Sequence Completed",
                "CONNECTED,SUCCESS",
                "Connection established successfully"
            };

            lock (_connectionLog)
            {
                var recentLogs = _connectionLog.TakeLast(10).ToList();
                foreach (var log in recentLogs)
                {
                    foreach (var indicator in successIndicators)
                    {
                        if (log.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogDebug("Found connection success indicator: {Indicator}", indicator);
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking connection log for success");
            return false;
        }
    }

    private async Task<bool> CheckConnectionLogForErrorsAsync()
    {
        try
        {
            var errorIndicators = new[]
            {
                "AUTH_FAILED",
                "TLS Error",
                "Connection refused",
                "RESOLVE: Cannot resolve host",
                "TCP/UDP: Incoming packet rejected",
                "SIGTERM received"
            };

            lock (_connectionLog)
            {
                var recentLogs = _connectionLog.TakeLast(20).ToList();
                foreach (var log in recentLogs)
                {
                    foreach (var indicator in errorIndicators)
                    {
                        if (log.Contains(indicator, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogError("Found connection error indicator: {Indicator} in log: {Log}", indicator, log);
                            return true;
                        }
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking connection log for errors");
            return false;
        }
    }

    private void OnOpenVpnOutputReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            lock (_connectionLog)
            {
                _connectionLog.Add(e.Data);
                // Keep only last 100 log entries
                if (_connectionLog.Count > 100)
                {
                    _connectionLog.RemoveAt(0);
                }
            }

            _logger.LogDebug("OpenVPN: {Output}", e.Data);
        }
    }

    private void OnOpenVpnErrorReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            lock (_connectionLog)
            {
                _connectionLog.Add($"ERROR: {e.Data}");
                if (_connectionLog.Count > 100)
                {
                    _connectionLog.RemoveAt(0);
                }
            }

            _logger.LogWarning("OpenVPN Error: {Error}", e.Data);
        }
    }

    private void OnOpenVpnProcessExited(object? sender, EventArgs e)
    {
        if (_openVpnProcess != null)
        {
            _logger.LogWarning("OpenVPN process exited with code: {ExitCode}", _openVpnProcess.ExitCode);

            if (_isConnected)
            {
                _isConnected = false;
                _ = NotifyConnectionStatusAsync(false);
            }
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

                // Check for OpenVPN TAP/TUN interface patterns
                if (IsOpenVpnInterface(desc, name))
                {
                    // Get the first IPv4 address
                    var ipProps = ni.GetIPProperties();
                    foreach (var addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == AddressFamily.InterNetwork &&
                            !IPAddress.IsLoopback(addr.Address))
                        {
                            _currentLocalIp = addr.Address.ToString();
                            _logger.LogDebug("Detected OpenVPN interface: {Name} ({Description}) with IP: {IP}",
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

    private bool IsOpenVpnInterface(string description, string name)
    {
        var vpnIndicators = new[]
        {
            "tap", "tun", "openvpn", "vpn adapter", "tap-windows", "tap adapter",
            "local area connection", "ethernet adapter"
        };

        // Check description for VPN indicators
        foreach (var indicator in vpnIndicators)
        {
            if (description.Contains(indicator))
            {
                // Additional check for TAP-Windows adapter specifically
                if (description.Contains("tap") || description.Contains("openvpn") || name.Contains("tap"))
                {
                    return true;
                }
            }
        }

        // Check if interface name suggests it's a VPN interface
        if (name.StartsWith("tap") || name.StartsWith("tun") || name.Contains("vpn"))
        {
            return true;
        }

        return false;
    }

    public async Task<bool> IsConnectedAsync()
    {
        try
        {
            // Check if process is running
            if (_openVpnProcess != null && !_openVpnProcess.HasExited)
            {
                // Verify network interface is still up
                var vpnInterface = await DetectVpnInterfaceAsync();
                if (!string.IsNullOrEmpty(vpnInterface))
                {
                    return true;
                }
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

            return -1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error measuring ping latency");
            return -1;
        }
    }

    private async Task NotifyConnectionStatusAsync(bool connected)
    {
        var latency = connected ? await GetPingLatencyAsync() : 0;

        StatusChanged?.Invoke(this, new VpnStatusEventArgs
        {
            IsConnected = connected,
            LocalIp = connected ? _currentLocalIp : null,
            Latency = latency,
            ErrorMessage = connected ? null : "OpenVPN connection lost"
        });
    }

    private void StartStatusMonitoring()
    {
        var interval = _configuration.GetValue<int>("VpnSettings:HeartbeatInterval", 5000);
        _statusTimer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(interval));
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
                    _logger.LogWarning("OpenVPN connection lost, attempting reconnection...");

                    if (_reconnectAttempts < MaxReconnectAttempts)
                    {
                        _reconnectAttempts++;
                        await Task.Delay(_reconnectDelay);

                        if (await ConnectAsync())
                        {
                            _logger.LogInformation("OpenVPN reconnection successful");
                            _reconnectAttempts = 0;
                        }
                    }
                    else
                    {
                        _logger.LogError("Max OpenVPN reconnection attempts reached");
                    }
                }
                else
                {
                    _reconnectAttempts = 0;
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
            _logger.LogError(ex, "Error checking OpenVPN status");
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _statusTimer.Change(Timeout.Infinite, Timeout.Infinite);
            await StopOpenVpnProcessAsync();

            _isConnected = false;
            _currentLocalIp = null;
            _vpnInterfaceName = "";
            _reconnectAttempts = 0;

            await NotifyConnectionStatusAsync(false);
            _logger.LogInformation("OpenVPN service disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OpenVPN disconnect");
        }
    }

    private async Task StopOpenVpnProcessAsync()
    {
        if (_openVpnProcess != null && !_openVpnProcess.HasExited)
        {
            try
            {
                _logger.LogInformation("Stopping OpenVPN process...");

                // Try graceful shutdown first
                _openVpnProcess.CloseMainWindow();

                if (!_openVpnProcess.WaitForExit(5000))
                {
                    // Force kill if graceful shutdown fails
                    _openVpnProcess.Kill();
                    _logger.LogWarning("OpenVPN process was forcefully terminated");
                }
                else
                {
                    _logger.LogInformation("OpenVPN process terminated gracefully");
                }

                _openVpnProcess.Dispose();
                _openVpnProcess = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping OpenVPN process");
            }
        }
    }

    private async Task<bool> EnsureTapAdapterAsync()
    {
        try
        {
            _logger.LogInformation("Checking for TAP adapter...");
            var tapCtlPath = Path.Combine(Path.GetDirectoryName(_openVpnPath)!, "tapctl.exe");
            
            _logger.LogDebug("Looking for tapctl.exe at: {TapCtlPath}", tapCtlPath);
            if (!File.Exists(tapCtlPath))
            {
                _logger.LogWarning("tapctl.exe not found at: {TapCtlPath}", tapCtlPath);
                return true; // Continue without installing, maybe system has TAP adapter
            }

            // Check if TAP adapter already exists
            _logger.LogDebug("Checking existing TAP adapters...");
            var listProcess = Process.Start(new ProcessStartInfo
            {
                FileName = tapCtlPath,
                Arguments = "list",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });

            if (listProcess != null)
            {
                listProcess.WaitForExit(10000);
                var output = listProcess.StandardOutput.ReadToEnd();
                var error = listProcess.StandardError.ReadToEnd();
                
                _logger.LogDebug("tapctl list output: {Output}", output);
                if (!string.IsNullOrEmpty(error))
                {
                    _logger.LogDebug("tapctl list error: {Error}", error);
                }
                
                if (output.Contains("TAP-Windows") || output.Contains("Wintun") || output.Contains("ovpn-dco"))
                {
                    _logger.LogInformation("TAP adapter already exists");
                    return true;
                }
            }

            // Try to install TAP adapter (requires admin privileges)
            _logger.LogInformation("No TAP adapter found. Attempting to create one...");
            _logger.LogWarning("TAP adapter creation requires administrator privileges. This may fail if not running as administrator.");
            
            var installProcess = Process.Start(new ProcessStartInfo
            {
                FileName = tapCtlPath,
                Arguments = "create --name \"OpenVPN TAP\"",
                UseShellExecute = true, // This allows UAC elevation prompt
                RedirectStandardOutput = false, // Can't redirect when using UseShellExecute
                RedirectStandardError = false,
                CreateNoWindow = false,
                Verb = "runas" // Request elevation
            });

            if (installProcess != null)
            {
                installProcess.WaitForExit(60000); // Give more time for UAC
                var exitCode = installProcess.ExitCode;

                if (exitCode == 0)
                {
                    _logger.LogInformation("TAP adapter installation completed successfully");
                    return true;
                }
                else
                {
                    _logger.LogWarning("TAP adapter installation failed with exit code: {ExitCode}. You may need to run as administrator or install OpenVPN system-wide.", exitCode);
                }
            }

            _logger.LogInformation("Continuing without TAP adapter installation. OpenVPN will attempt to proceed.");
            return true; // Continue even if TAP installation failed
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring TAP adapter. You may need to install OpenVPN system-wide or run as administrator.");
            return true; // Continue even if failed
        }
    }

    public void Dispose()
    {
        try
        {
            _statusTimer?.Dispose();
            _ = StopOpenVpnProcessAsync(); // Fire and forget
            GC.SuppressFinalize(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OpenVPN service disposal");
        }
    }
}