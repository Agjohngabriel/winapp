// src/AutoConnect.Client/Services/ObdVehicleService.cs
using System.IO.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using AutoConnect.Shared.DTOs;
using AutoConnect.Core.Enums;

namespace AutoConnect.Client.Services;

public class ObdVehicleService : IVehicleService, IDisposable
{
    private readonly ILogger<ObdVehicleService> _logger;
    private readonly IConfiguration _configuration;
    private readonly IApiService _apiService;
    private SerialPort? _serialPort;
    private bool _isConnected;
    private readonly Timer _dataTimer;
    private Guid? _currentSessionId;

    // Simulated values for when no hardware is connected
    private readonly Random _random = new();
    private decimal _baseVoltage = 12.4m;
    private bool _simulationMode = false;
    private string? _cachedVin;
    private DateTime _lastDataSent = DateTime.MinValue;

    public event EventHandler<VehicleDataEventArgs>? DataReceived;

    public ObdVehicleService(
        ILogger<ObdVehicleService> logger,
        IConfiguration configuration,
        IApiService apiService)
    {
        _logger = logger;
        _configuration = configuration;
        _apiService = apiService;
        _dataTimer = new Timer(ReadVehicleDataPeriodic, null, Timeout.Infinite, Timeout.Infinite);
    }

    public async Task<bool> ConnectAsync()
    {
        try
        {
            _logger.LogInformation("Attempting to connect to OBD adapter...");

            // First, try to connect to real hardware
            if (await TryConnectToRealHardwareAsync())
            {
                _logger.LogInformation("Connected to real OBD adapter on port {Port}", _serialPort?.PortName);
                _simulationMode = false;
                _isConnected = true;
            }
            else
            {
                _logger.LogWarning("No OBD adapter found, using simulation mode for MVP demo");
                _simulationMode = true;
                _isConnected = true; // Enable simulation mode
            }

            if (_isConnected)
            {
                // Create API session
                await CreateVehicleSessionAsync();

                // Initialize OBD connection if real hardware
                if (!_simulationMode && _serialPort != null)
                {
                    await InitializeObdConnectionAsync();
                }

                // Cache VIN for session
                _cachedVin = await ReadVinAsync();

                // Start periodic data reading
                var interval = _configuration.GetValue<int>("VehicleSettings:DataRefreshInterval", 2000);
                _dataTimer.Change(TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(interval));

                _logger.LogInformation("Vehicle service connected successfully (Mode: {Mode})",
                    _simulationMode ? "Simulation" : "Hardware");

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to vehicle");
            return false;
        }
    }

    private async Task<bool> TryConnectToRealHardwareAsync()
    {
        try
        {
            var availablePorts = SerialPort.GetPortNames();
            _logger.LogInformation("Available COM ports: {Ports}", string.Join(", ", availablePorts));

            if (!availablePorts.Any())
            {
                _logger.LogInformation("No COM ports available");
                return false;
            }

            foreach (var portName in availablePorts)
            {
                if (await TryConnectToPortAsync(portName))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning for OBD hardware");
            return false;
        }
    }

    private async Task<bool> TryConnectToPortAsync(string portName)
    {
        try
        {
            _logger.LogDebug("Trying to connect to port {Port}", portName);

            _serialPort = new SerialPort(portName, 38400, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 2000,
                WriteTimeout = 2000,
                DtrEnable = true,
                RtsEnable = true
            };

            _serialPort.Open();
            await Task.Delay(100); // Allow port to stabilize

            // Test connection with ATZ (reset) command
            var response = await SendObdCommandAsync("ATZ");

            if (!string.IsNullOrEmpty(response))
            {
                _logger.LogDebug("Port {Port} ATZ response: {Response}", portName, response.Trim());

                // Check if response indicates an ELM327 or compatible adapter
                if (response.Contains("ELM") || response.Contains("OK") || response.Contains(">"))
                {
                    _logger.LogInformation("Found compatible OBD adapter on {Port}", portName);
                    return true;
                }
            }

            // Clean up failed connection
            _serialPort.Close();
            _serialPort.Dispose();
            _serialPort = null;
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Port {Port} connection failed: {Error}", portName, ex.Message);

            // Clean up on exception
            try
            {
                _serialPort?.Close();
                _serialPort?.Dispose();
                _serialPort = null;
            }
            catch { /* Ignore cleanup errors */ }

            return false;
        }
    }

    private async Task InitializeObdConnectionAsync()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return;

        try
        {
            _logger.LogInformation("Initializing OBD connection...");

            // Initialize ELM327 adapter
            await SendObdCommandAsync("ATE0");  // Echo off
            await Task.Delay(50);
            await SendObdCommandAsync("ATL0");  // Linefeeds off
            await Task.Delay(50);
            await SendObdCommandAsync("ATS0");  // Spaces off
            await Task.Delay(50);
            await SendObdCommandAsync("ATH1");  // Headers on
            await Task.Delay(50);

            // Test supported PIDs
            var pidsResponse = await SendObdCommandAsync("0100");
            _logger.LogDebug("Supported PIDs response: {Response}", pidsResponse);

            _logger.LogInformation("OBD connection initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing OBD connection");
        }
    }

    private async Task<string?> SendObdCommandAsync(string command)
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return null;

        try
        {
            // Clear any existing data
            _serialPort.DiscardInBuffer();

            // Send command
            _serialPort.WriteLine(command + "\r");

            // Wait for response
            var response = string.Empty;
            var attempts = 0;
            var maxAttempts = 20; // 2 seconds max wait

            while (attempts < maxAttempts)
            {
                await Task.Delay(100);

                while (_serialPort.BytesToRead > 0)
                {
                    response += _serialPort.ReadExisting();
                }

                // Check if we have a complete response (ends with > or contains response)
                if (response.Contains(">") || response.Contains("OK") || response.Contains("NO DATA"))
                {
                    break;
                }

                attempts++;
            }

            return response.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending OBD command: {Command}", command);
            return null;
        }
    }

    private async Task CreateVehicleSessionAsync()
    {
        try
        {
            // Get client ID from configuration or create a default one
            var clientIdString = _configuration["VehicleSettings:ClientId"];
            Guid clientId;

            if (string.IsNullOrEmpty(clientIdString) || !Guid.TryParse(clientIdString, out clientId))
            {
                clientId = Guid.NewGuid();
                _logger.LogWarning("No valid ClientId in configuration, generated new one: {ClientId}", clientId);
            }

            var createSessionRequest = new CreateVehicleSessionRequest
            {
                ClientId = clientId,
                ObdAdapterType = _simulationMode ? "Simulated" : DetectAdapterType(),
                ObdProtocol = "ISO 15031-5 (CAN)"
            };

            var sessionResponse = await _apiService.PostAsync<VehicleSessionDto>(
                "api/vehiclesessions",
                createSessionRequest);

            if (sessionResponse.Success && sessionResponse.Data != null)
            {
                _currentSessionId = sessionResponse.Data.Id;
                _logger.LogInformation("Created vehicle session: {SessionId} for client {ClientId}",
                    _currentSessionId, clientId);
            }
            else
            {
                _logger.LogWarning("Failed to create vehicle session via API: {Error}", sessionResponse.Error);
                // Continue without API session for local testing
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating vehicle session");
            // Continue without API session for local testing
        }
    }

    private string DetectAdapterType()
    {
        if (_serialPort == null) return "Unknown";

        // Try to detect adapter type based on responses
        // This is simplified - real detection would be more sophisticated
        return "ELM327"; // Default assumption
    }

    public async Task<string?> ReadVinAsync()
    {
        if (_simulationMode)
        {
            return "WBAPH7G56DNB12345"; // Simulated BMW VIN
        }

        try
        {
            // Mode 09, PID 02: Vehicle Identification Number
            var response = await SendObdCommandAsync("0902");

            if (!string.IsNullOrEmpty(response) && !response.Contains("NO DATA"))
            {
                // Parse VIN from hex response (simplified)
                var vin = ParseVinFromResponse(response);
                if (!string.IsNullOrEmpty(vin))
                {
                    _logger.LogDebug("Read VIN from vehicle: {VIN}", vin);
                    return vin;
                }
            }

            // Fallback: try alternative VIN request
            response = await SendObdCommandAsync("09 02");
            if (!string.IsNullOrEmpty(response) && !response.Contains("NO DATA"))
            {
                var vin = ParseVinFromResponse(response);
                if (!string.IsNullOrEmpty(vin))
                {
                    return vin;
                }
            }

            _logger.LogWarning("Could not read VIN from vehicle, using placeholder");
            return "HARDWARE_VIN_N/A";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading VIN");
            return null;
        }
    }

    private string? ParseVinFromResponse(string response)
    {
        try
        {
            // This is a simplified VIN parser
            // Real implementation would need to handle multi-line responses and proper hex decoding
            var cleanResponse = response.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace(">", "");

            // Look for VIN pattern in hex (simplified)
            if (cleanResponse.Length > 20)
            {
                // TODO: Implement proper hex-to-ASCII conversion for VIN
                // For now, return a placeholder indicating we got some response
                return "OBD_DETECTED_VIN";
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<decimal?> ReadBatteryVoltageAsync()
    {
        if (_simulationMode)
        {
            // Simulate realistic voltage fluctuation
            _baseVoltage += (decimal)(_random.NextDouble() - 0.5) * 0.1m;
            _baseVoltage = Math.Max(11.8m, Math.Min(14.2m, _baseVoltage));
            return Math.Round(_baseVoltage, 1);
        }

        try
        {
            // ATRV command to read voltage
            var response = await SendObdCommandAsync("ATRV");

            if (!string.IsNullOrEmpty(response))
            {
                // Parse voltage from response like "12.6V"
                var match = System.Text.RegularExpressions.Regex.Match(response, @"(\d+\.\d+)V");
                if (match.Success && decimal.TryParse(match.Groups[1].Value, out var voltage))
                {
                    return voltage;
                }
            }

            // Fallback: try to read from PID 0142 (Control module voltage)
            response = await SendObdCommandAsync("0142");
            if (!string.IsNullOrEmpty(response) && !response.Contains("NO DATA"))
            {
                // Parse hex response to voltage (simplified)
                return 12.6m; // Placeholder for complex parsing
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading battery voltage");
            return null;
        }
    }

    public async Task<bool> IsIgnitionOnAsync()
    {
        if (_simulationMode)
        {
            // Simulate ignition status with some variability
            return _random.NextDouble() > 0.2; // 80% chance ignition is on
        }

        try
        {
            // Check engine RPM - if we can read it, ignition is likely on
            var response = await SendObdCommandAsync("010C");

            if (!string.IsNullOrEmpty(response) && !response.Contains("NO DATA"))
            {
                // Parse RPM to determine if engine is running
                var rpm = ParseRpmFromResponse(response);
                return rpm > 0; // If RPM > 0, ignition is definitely on
            }

            // Alternative: check if we can communicate with ECU at all
            response = await SendObdCommandAsync("0100");
            return !string.IsNullOrEmpty(response) && !response.Contains("NO DATA");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking ignition status");
            return false;
        }
    }

    private int ParseRpmFromResponse(string response)
    {
        try
        {
            // Simplified RPM parsing from hex response
            // Real implementation would convert hex bytes to RPM value
            var cleanResponse = response.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace(">", "");

            if (cleanResponse.Length >= 8 && cleanResponse.StartsWith("410C"))
            {
                // Extract RPM bytes and convert (simplified)
                return _random.Next(700, 2000); // Placeholder for proper parsing
            }

            return 0;
        }
        catch
        {
            return 0;
        }
    }

    private async void ReadVehicleDataPeriodic(object? state)
    {
        if (!_isConnected)
            return;

        try
        {
            var vin = _cachedVin ?? await ReadVinAsync();
            var voltage = await ReadBatteryVoltageAsync();
            var ignitionOn = await IsIgnitionOnAsync();

            // Update cached VIN if we got a new one
            if (!string.IsNullOrEmpty(vin))
            {
                _cachedVin = vin;
            }

            // Notify UI immediately
            DataReceived?.Invoke(this, new VehicleDataEventArgs
            {
                Vin = vin,
                BatteryVoltage = voltage,
                IgnitionOn = ignitionOn,
                Timestamp = DateTime.Now
            });

            // Send to API (throttled to avoid spam)
            if (_currentSessionId.HasValue && (DateTime.Now - _lastDataSent).TotalSeconds >= 2)
            {
                await SendDataToApiAsync(vin, voltage, ignitionOn);
                _lastDataSent = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during periodic data reading");
        }
    }

    private async Task SendDataToApiAsync(string? vin, decimal? voltage, bool ignitionOn)
    {
        try
        {
            var vehicleDataRequest = new CreateVehicleDataRequest
            {
                VehicleSessionId = _currentSessionId!.Value,
                BatteryVoltage = voltage,
                KL15Voltage = ignitionOn ? voltage : null,
                KL30Voltage = voltage, // KL30 is always battery voltage
                IgnitionStatus = ignitionOn ? IgnitionStatus.KL15_On : IgnitionStatus.Off,
                RawObdData = $"Mode: {(_simulationMode ? "Simulation" : "Hardware")}, VIN: {vin}, Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
            };

            var apiResponse = await _apiService.PostAsync<VehicleDataDto>(
                "api/vehicledata",
                vehicleDataRequest);

            if (apiResponse.Success)
            {
                _logger.LogDebug("Successfully sent vehicle data to API");
            }
            else
            {
                _logger.LogDebug("Failed to send vehicle data to API: {Error}", apiResponse.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending data to API");
        }
    }

    public async Task DisconnectAsync()
    {
        try
        {
            _isConnected = false;

            // Stop periodic data reading
            _dataTimer.Change(Timeout.Infinite, Timeout.Infinite);

            // Close serial connection
            if (_serialPort != null)
            {
                try
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }
                    _serialPort.Dispose();
                    _serialPort = null;
                    _logger.LogInformation("Disconnected from OBD adapter");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error closing serial port");
                }
            }

            // End API session
            if (_currentSessionId.HasValue)
            {
                try
                {
                    var endResponse = await _apiService.PostAsync<object>(
                        $"api/vehiclesessions/{_currentSessionId}/end",
                        new { });

                    if (endResponse.Success)
                    {
                        _logger.LogInformation("Successfully ended vehicle session {SessionId}", _currentSessionId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error ending vehicle session");
                }
                finally
                {
                    _currentSessionId = null;
                }
            }

            _logger.LogInformation("Vehicle service disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during vehicle service disconnect");
        }
    }

    public void Dispose()
    {
        try
        {
            // Ensure we're disconnected
            if (_isConnected)
            {
                // Don't await in Dispose - just do cleanup
                _isConnected = false;
                _dataTimer.Change(Timeout.Infinite, Timeout.Infinite);
            }

            _dataTimer?.Dispose();
            _serialPort?.Dispose();

            GC.SuppressFinalize(this);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during dispose");
        }
    }
}