// src/AutoConnect.Client/Services/ObdVehicleService.cs
using System.IO.Ports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using AutoConnect.Shared.DTOs;
using AutoConnect.Core.Enums;
using System.Text;
using System.Text.RegularExpressions;

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
    private int _simulatedRpm = 0;
    private bool _simulatedIgnition = true;

    // OBD Communication tracking
    private int _communicationErrors = 0;
    private const int MaxCommunicationErrors = 5;
    private string _detectedProtocol = "Unknown";

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

                _logger.LogInformation("Vehicle service connected successfully (Mode: {Mode}, Protocol: {Protocol})",
                    _simulationMode ? "Simulation" : "Hardware", _detectedProtocol);

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

            // Try common OBD baud rates
            var baudRates = new[] { 38400, 9600, 115200, 57600 };

            foreach (var portName in availablePorts)
            {
                foreach (var baudRate in baudRates)
                {
                    if (await TryConnectToPortAsync(portName, baudRate))
                    {
                        return true;
                    }
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

    private async Task<bool> TryConnectToPortAsync(string portName, int baudRate)
    {
        try
        {
            _logger.LogDebug("Trying to connect to port {Port} at {BaudRate} baud", portName, baudRate);

            _serialPort = new SerialPort(portName, baudRate, Parity.None, 8, StopBits.One)
            {
                ReadTimeout = 3000,
                WriteTimeout = 3000,
                DtrEnable = true,
                RtsEnable = true,
                NewLine = "\r"
            };

            _serialPort.Open();
            await Task.Delay(500); // Allow port to stabilize

            // Test connection with ATZ (reset) command
            var response = await SendObdCommandAsync("ATZ");

            if (!string.IsNullOrEmpty(response))
            {
                _logger.LogDebug("Port {Port} ATZ response: {Response}", portName, response.Trim());

                // Check if response indicates an ELM327 or compatible adapter
                if (IsValidObdResponse(response))
                {
                    _logger.LogInformation("Found compatible OBD adapter on {Port} at {BaudRate} baud", portName, baudRate);
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
            _logger.LogDebug("Port {Port} at {BaudRate} baud failed: {Error}", portName, baudRate, ex.Message);

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

    private bool IsValidObdResponse(string response)
    {
        var cleanResponse = response.ToUpper().Replace("\r", "").Replace("\n", "").Replace(" ", "");

        return cleanResponse.Contains("ELM327") ||
               cleanResponse.Contains("ELM320") ||
               cleanResponse.Contains("OBD") ||
               cleanResponse.Contains("OK") ||
               cleanResponse.EndsWith(">") ||
               cleanResponse.Contains("ATZ");
    }

    private async Task InitializeObdConnectionAsync()
    {
        if (_serialPort == null || !_serialPort.IsOpen)
            return;

        try
        {
            _logger.LogInformation("Initializing OBD connection...");

            // Reset the adapter
            await SendObdCommandAsync("ATZ");
            await Task.Delay(1000);

            // Initialize ELM327 adapter
            await SendObdCommandAsync("ATE0");  // Echo off
            await Task.Delay(100);
            await SendObdCommandAsync("ATL0");  // Linefeeds off
            await Task.Delay(100);
            await SendObdCommandAsync("ATS0");  // Spaces off
            await Task.Delay(100);
            await SendObdCommandAsync("ATH1");  // Headers on
            await Task.Delay(100);

            // Try to detect protocol
            var protocolResponse = await SendObdCommandAsync("ATDP");
            if (!string.IsNullOrEmpty(protocolResponse))
            {
                _detectedProtocol = ParseProtocolFromResponse(protocolResponse);
            }

            // Test supported PIDs
            var pidsResponse = await SendObdCommandAsync("0100");
            if (!string.IsNullOrEmpty(pidsResponse))
            {
                _logger.LogDebug("Supported PIDs response: {Response}", pidsResponse);

                if (!pidsResponse.Contains("NO DATA") && !pidsResponse.Contains("?"))
                {
                    _communicationErrors = 0; // Reset error counter on successful communication
                }
            }

            _logger.LogInformation("OBD connection initialized successfully with protocol: {Protocol}", _detectedProtocol);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing OBD connection");
            _communicationErrors++;
        }
    }

    private string ParseProtocolFromResponse(string response)
    {
        var cleanResponse = response.ToUpper().Replace("\r", "").Replace("\n", "");

        if (cleanResponse.Contains("CAN")) return "CAN";
        if (cleanResponse.Contains("ISO 15765-4")) return "ISO 15765-4 (CAN)";
        if (cleanResponse.Contains("ISO 14230-4")) return "ISO 14230-4 (KWP2000)";
        if (cleanResponse.Contains("ISO 9141-2")) return "ISO 9141-2";
        if (cleanResponse.Contains("J1850")) return "J1850";

        return cleanResponse.Length > 0 ? cleanResponse : "Unknown";
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
            _serialPort.WriteLine(command);

            // Wait for response with timeout
            var response = await ReadObdResponseAsync();

            if (!string.IsNullOrEmpty(response))
            {
                _communicationErrors = 0; // Reset error counter on successful communication
            }
            else
            {
                _communicationErrors++;
                _logger.LogWarning("No response to OBD command: {Command} (Errors: {Count})",
                    command, _communicationErrors);
            }

            return response;
        }
        catch (Exception ex)
        {
            _communicationErrors++;
            _logger.LogError(ex, "Error sending OBD command: {Command} (Errors: {Count})",
                command, _communicationErrors);
            return null;
        }
    }

    private async Task<string?> ReadObdResponseAsync()
    {
        var response = new StringBuilder();
        var attempts = 0;
        var maxAttempts = 30; // 3 seconds max wait

        while (attempts < maxAttempts)
        {
            await Task.Delay(100);

            while (_serialPort != null && _serialPort.BytesToRead > 0)
            {
                try
                {
                    var data = _serialPort.ReadExisting();
                    response.Append(data);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading from serial port");
                    return null;
                }
            }

            var currentResponse = response.ToString();

            // Check if we have a complete response
            if (currentResponse.Contains(">") ||
                currentResponse.Contains("OK") ||
                currentResponse.Contains("NO DATA") ||
                currentResponse.Contains("UNABLE TO CONNECT") ||
                currentResponse.Contains("?"))
            {
                break;
            }

            attempts++;
        }

        return response.ToString().Trim();
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
                ObdProtocol = _simulationMode ? "Simulated Protocol" : _detectedProtocol
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

        // Try to detect adapter type based on initialization responses
        // This is simplified - real detection would be more sophisticated
        return "ELM327"; // Default assumption
    }

    public async Task<string?> ReadVinAsync()
    {
        if (_simulationMode)
        {
            // Simulate different VINs based on time for demo variety
            var vinSamples = new[]
            {
                "WBAPH7G56DNB12345", // BMW
                "1HGBH41JXMN109186", // Honda
                "JM1BL1H65A1234567"  // Mazda
            };
            return vinSamples[_random.Next(vinSamples.Length)];
        }

        try
        {
            // Mode 09, PID 02: Vehicle Identification Number
            var response = await SendObdCommandAsync("0902");

            if (!string.IsNullOrEmpty(response) && !response.Contains("NO DATA"))
            {
                var vin = ParseVinFromResponse(response);
                if (!string.IsNullOrEmpty(vin))
                {
                    _logger.LogInformation("Successfully read VIN from vehicle: {VIN}", vin);
                    return vin;
                }
            }

            // Fallback: try alternative VIN request formats
            var alternativeCommands = new[] { "09 02", "0902", "AT RV" };

            foreach (var cmd in alternativeCommands)
            {
                response = await SendObdCommandAsync(cmd);
                if (!string.IsNullOrEmpty(response) && !response.Contains("NO DATA"))
                {
                    var vin = ParseVinFromResponse(response);
                    if (!string.IsNullOrEmpty(vin))
                    {
                        _logger.LogInformation("VIN read using alternative command {Command}: {VIN}", cmd, vin);
                        return vin;
                    }
                }
            }

            _logger.LogWarning("Could not read VIN from vehicle");
            return "VIN_READ_FAILED";
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
            // Clean the response
            var cleanResponse = response.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace(">", "").ToUpper();

            // Method 1: Look for Mode 09 PID 02 response pattern
            // Response format: 49 02 XX [VIN data in hex]
            var match = Regex.Match(cleanResponse, @"4902\d{2}([A-F0-9]{34,})");
            if (match.Success)
            {
                var vinHex = match.Groups[1].Value;
                if (vinHex.Length >= 34) // 17 characters * 2 hex digits
                {
                    try
                    {
                        var vinBytes = Convert.FromHexString(vinHex.Substring(0, 34));
                        var vin = Encoding.ASCII.GetString(vinBytes);

                        // Validate VIN format (17 alphanumeric characters, no I, O, Q)
                        if (IsValidVin(vin))
                        {
                            return vin;
                        }
                    }
                    catch
                    {
                        // Continue to next method
                    }
                }
            }

            // Method 2: Look for direct ASCII VIN in response
            var asciiMatch = Regex.Match(response, @"[A-HJ-NPR-Z0-9]{17}");
            if (asciiMatch.Success && IsValidVin(asciiMatch.Value))
            {
                return asciiMatch.Value;
            }

            // Method 3: Multi-line VIN response parsing
            if (response.Contains("49 02"))
            {
                var hexData = Regex.Replace(cleanResponse, @"4902\d{2}", "");
                if (hexData.Length >= 34)
                {
                    try
                    {
                        var vinBytes = Convert.FromHexString(hexData.Substring(0, 34));
                        var vin = Encoding.ASCII.GetString(vinBytes);
                        if (IsValidVin(vin))
                        {
                            return vin;
                        }
                    }
                    catch
                    {
                        // Parsing failed
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing VIN from response: {Response}", response);
            return null;
        }
    }

    private bool IsValidVin(string vin)
    {
        if (string.IsNullOrEmpty(vin) || vin.Length != 17)
            return false;

        // VIN cannot contain I, O, or Q
        if (vin.Contains('I') || vin.Contains('O') || vin.Contains('Q'))
            return false;

        // Should be alphanumeric
        return vin.All(c => char.IsLetterOrDigit(c));
    }

    public async Task<decimal?> ReadBatteryVoltageAsync()
    {
        if (_simulationMode)
        {
            // Simulate realistic voltage fluctuation
            _baseVoltage += (decimal)(_random.NextDouble() - 0.5) * 0.2m;
            _baseVoltage = Math.Max(11.5m, Math.Min(14.5m, _baseVoltage));
            return Math.Round(_baseVoltage, 1);
        }

        try
        {
            // Method 1: ATRV command to read voltage directly from ELM327
            var response = await SendObdCommandAsync("ATRV");

            if (!string.IsNullOrEmpty(response))
            {
                // Parse voltage from response like "12.6V" or "12.6"
                var voltageMatch = Regex.Match(response, @"(\d+\.?\d*)V?");
                if (voltageMatch.Success && decimal.TryParse(voltageMatch.Groups[1].Value, out var voltage))
                {
                    // Sanity check - typical car battery voltage range
                    if (voltage >= 8.0m && voltage <= 16.0m)
                    {
                        return Math.Round(voltage, 1);
                    }
                }
            }

            // Method 2: Try to read from PID 0142 (Control module voltage)
            response = await SendObdCommandAsync("0142");
            if (!string.IsNullOrEmpty(response) && !response.Contains("NO DATA"))
            {
                // Parse hex response to voltage
                var voltage = ParseVoltageFromPidResponse(response);
                if (voltage.HasValue)
                {
                    return voltage;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading battery voltage");
            return null;
        }
    }

    private decimal? ParseVoltageFromPidResponse(string response)
    {
        try
        {
            // Clean response and look for PID 42 response pattern
            var cleanResponse = response.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace(">", "");

            // Response format: 41 42 XX XX (XX XX are voltage bytes)
            var match = Regex.Match(cleanResponse, @"4142([A-F0-9]{4})");
            if (match.Success)
            {
                var voltageHex = match.Groups[1].Value;
                if (int.TryParse(voltageHex, System.Globalization.NumberStyles.HexNumber, null, out var voltageRaw))
                {
                    // Convert raw value to voltage (formula may vary by vehicle)
                    var voltage = voltageRaw / 1000.0m; // Common conversion

                    if (voltage >= 8.0m && voltage <= 16.0m)
                    {
                        return Math.Round(voltage, 1);
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<bool> IsIgnitionOnAsync()
    {
        if (_simulationMode)
        {
            // Simulate ignition status with some variability
            _simulatedIgnition = _random.NextDouble() > 0.1; // 90% chance ignition is on during testing
            return _simulatedIgnition;
        }

        try
        {
            // Method 1: Check engine RPM - if we can read it and it's > 0, ignition is on
            var rpmResponse = await SendObdCommandAsync("010C");

            if (!string.IsNullOrEmpty(rpmResponse) && !rpmResponse.Contains("NO DATA"))
            {
                var rpm = ParseRpmFromResponse(rpmResponse);
                if (rpm >= 0) // If we can read RPM, ignition is on (even at 0 RPM)
                {
                    return true;
                }
            }

            // Method 2: Check if we can communicate with ECU at all
            var pingResponse = await SendObdCommandAsync("0100");
            if (!string.IsNullOrEmpty(pingResponse) &&
                !pingResponse.Contains("NO DATA") &&
                !pingResponse.Contains("UNABLE TO CONNECT"))
            {
                return true; // If ECU responds, ignition is likely on
            }

            // Method 3: Try reading vehicle speed - if available, ignition is on
            var speedResponse = await SendObdCommandAsync("010D");
            if (!string.IsNullOrEmpty(speedResponse) && !speedResponse.Contains("NO DATA"))
            {
                return true;
            }

            return false;
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
            // Clean response and look for PID 0C response pattern
            var cleanResponse = response.Replace(" ", "").Replace("\r", "").Replace("\n", "").Replace(">", "");

            // Response format: 41 0C XX XX (XX XX are RPM bytes)
            var match = Regex.Match(cleanResponse, @"410C([A-F0-9]{4})");
            if (match.Success)
            {
                var rpmHex = match.Groups[1].Value;
                if (int.TryParse(rpmHex, System.Globalization.NumberStyles.HexNumber, null, out var rpmRaw))
                {
                    // RPM = ((A*256)+B)/4
                    var rpm = rpmRaw / 4;
                    return Math.Max(0, Math.Min(8000, rpm)); // Sanity check
                }
            }

            return -1; // Couldn't parse
        }
        catch
        {
            return -1;
        }
    }

    private async void ReadVehicleDataPeriodic(object? state)
    {
        if (!_isConnected)
            return;

        try
        {
            // Skip reading if we have too many communication errors
            if (_communicationErrors >= MaxCommunicationErrors)
            {
                _logger.LogWarning("Too many communication errors ({Count}), skipping data read", _communicationErrors);
                return;
            }

            var vin = _cachedVin ?? await ReadVinAsync();
            var voltage = await ReadBatteryVoltageAsync();
            var ignitionOn = await IsIgnitionOnAsync();

            // Update cached VIN if we got a new one
            if (!string.IsNullOrEmpty(vin) && vin != "VIN_READ_FAILED")
            {
                _cachedVin = vin;
            }

            // Simulate some additional data for demo
            if (_simulationMode)
            {
                _simulatedRpm = ignitionOn ? _random.Next(700, 2500) : 0;
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
            if (_currentSessionId.HasValue && (DateTime.Now - _lastDataSent).TotalSeconds >= 3)
            {
                await SendDataToApiAsync(vin, voltage, ignitionOn);
                _lastDataSent = DateTime.Now;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during periodic data reading");
            _communicationErrors++;
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
                EngineRPM = _simulationMode ? _simulatedRpm : null,
                RawObdData = $"Mode: {(_simulationMode ? "Simulation" : "Hardware")}, Protocol: {_detectedProtocol}, VIN: {vin}, Errors: {_communicationErrors}, Time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
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
                        // Send a final command to clean up
                        _serialPort.WriteLine("ATZ");
                        await Task.Delay(500);
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