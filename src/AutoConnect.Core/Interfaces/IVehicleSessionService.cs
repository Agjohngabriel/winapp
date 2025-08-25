// src/AutoConnect.Core/Interfaces/IVehicleSessionService.cs
using AutoConnect.Core.Entities;
using AutoConnect.Core.Enums;

namespace AutoConnect.Core.Interfaces;

public interface IVehicleSessionService
{
    Task<VehicleSession?> GetSessionByIdAsync(Guid id);
    Task<IEnumerable<VehicleSession>> GetSessionsByClientIdAsync(Guid clientId);
    Task<VehicleSession?> GetActiveSessionByClientIdAsync(Guid clientId);
    Task<VehicleSession> CreateSessionAsync(Guid clientId, string? vin = null, string? obdAdapterType = null, string? obdProtocol = null);
    Task<VehicleSession> UpdateSessionAsync(Guid id, string? vin = null, VehicleConnectionStatus? status = null, string? obdAdapterType = null, string? obdProtocol = null, int? pingLatency = null, decimal? dataUsage = null, string? errorMessage = null);
    Task EndSessionAsync(Guid id);
    Task DeleteSessionAsync(Guid id);
    Task<bool> SessionExistsAsync(Guid id);
}