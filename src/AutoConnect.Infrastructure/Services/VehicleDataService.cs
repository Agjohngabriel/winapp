// src/AutoConnect.Infrastructure/Services/VehicleDataService.cs
using AutoConnect.Core.Entities;
using AutoConnect.Core.Enums;
using AutoConnect.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AutoConnect.Infrastructure.Services;

public class VehicleDataService : IVehicleDataService
{
    private readonly IVehicleDataRepository _dataRepository;
    private readonly IVehicleSessionRepository _sessionRepository;
    private readonly ILogger<VehicleDataService> _logger;

    public VehicleDataService(
        IVehicleDataRepository dataRepository,
        IVehicleSessionRepository sessionRepository,
        ILogger<VehicleDataService> logger)
    {
        _dataRepository = dataRepository;
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task<VehicleData?> GetVehicleDataByIdAsync(Guid id)
    {
        return await _dataRepository.GetByIdAsync(id);
    }

    public async Task<IEnumerable<VehicleData>> GetDataBySessionIdAsync(Guid sessionId)
    {
        return await _dataRepository.GetBySessionIdAsync(sessionId);
    }

    public async Task<VehicleData?> GetLatestDataBySessionIdAsync(Guid sessionId)
    {
        return await _dataRepository.GetLatestBySessionIdAsync(sessionId);
    }

    public async Task<IEnumerable<VehicleData>> GetRecentDataBySessionIdAsync(Guid sessionId, int count = 50)
    {
        return await _dataRepository.GetRecentBySessionIdAsync(sessionId, count);
    }

    public async Task<VehicleData> CreateVehicleDataAsync(Guid sessionId, decimal? batteryVoltage = null, decimal? kl15Voltage = null, decimal? kl30Voltage = null, IgnitionStatus ignitionStatus = IgnitionStatus.Off, int? engineRpm = null, decimal? vehicleSpeed = null, decimal? coolantTemperature = null, decimal? fuelLevel = null, string? diagnosticTroubleCodes = null, string? rawObdData = null)
    {
        // Verify session exists
        if (!await _sessionRepository.ExistsAsync(sessionId))
        {
            throw new InvalidOperationException($"Vehicle session with ID '{sessionId}' not found.");
        }

        var vehicleData = new VehicleData
        {
            VehicleSessionId = sessionId,
            Timestamp = DateTime.UtcNow,
            BatteryVoltage = batteryVoltage,
            KL15Voltage = kl15Voltage,
            KL30Voltage = kl30Voltage,
            IgnitionStatus = ignitionStatus,
            EngineRPM = engineRpm,
            VehicleSpeed = vehicleSpeed,
            CoolantTemperature = coolantTemperature,
            FuelLevel = fuelLevel,
            DiagnosticTroubleCodes = diagnosticTroubleCodes,
            RawObdData = rawObdData
        };

        var createdData = await _dataRepository.CreateAsync(vehicleData);
        _logger.LogDebug("Created vehicle data: {DataId} for session {SessionId}", createdData.Id, sessionId);

        return createdData;
    }

    public async Task DeleteVehicleDataAsync(Guid id)
    {
        var vehicleData = await _dataRepository.GetByIdAsync(id);
        if (vehicleData == null)
        {
            throw new InvalidOperationException($"Vehicle data with ID '{id}' not found.");
        }

        await _dataRepository.DeleteAsync(id);
        _logger.LogInformation("Deleted vehicle data: {DataId}", id);
    }
}