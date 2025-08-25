// src/AutoConnect.Core/Interfaces/IVehicleSessionRepository.cs
using AutoConnect.Core.Entities;
using AutoConnect.Core.Enums;

namespace AutoConnect.Core.Interfaces;

public interface IVehicleSessionRepository : IRepository<VehicleSession>
{
    Task<IEnumerable<VehicleSession>> GetByClientIdAsync(Guid clientId);
    Task<VehicleSession?> GetActiveSessionByClientIdAsync(Guid clientId);
    Task<IEnumerable<VehicleSession>> GetByVINAsync(string vin);
    Task<VehicleSession?> GetWithDataAsync(Guid id);
    Task<IEnumerable<VehicleSession>> GetByStatusAsync(VehicleConnectionStatus status);
}