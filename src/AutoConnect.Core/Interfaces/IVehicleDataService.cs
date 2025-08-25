// src/AutoConnect.Core/Interfaces/IVehicleDataService.cs
using AutoConnect.Core.Entities;
using AutoConnect.Core.Enums;

namespace AutoConnect.Core.Interfaces;

public interface IVehicleDataService
{
    Task<VehicleData?> GetVehicleDataByIdAsync(Guid id);
    Task<IEnumerable<VehicleData>> GetDataBySessionIdAsync(Guid sessionId);
    Task<VehicleData?> GetLatestDataBySessionIdAsync(Guid sessionId);
    Task<IEnumerable<VehicleData>> GetRecentDataBySessionIdAsync(Guid sessionId, int count = 50);
    Task<VehicleData> CreateVehicleDataAsync(Guid sessionId, decimal? batteryVoltage = null, decimal? kl15Voltage = null, decimal? kl30Voltage = null, IgnitionStatus ignitionStatus = IgnitionStatus.Off, int? engineRpm = null, decimal? vehicleSpeed = null, decimal? coolantTemperature = null, decimal? fuelLevel = null, string? diagnosticTroubleCodes = null, string? rawObdData = null);
    Task DeleteVehicleDataAsync(Guid id);
}