// src/AutoConnect.Core/Interfaces/IVehicleDataRepository.cs
using AutoConnect.Core.Entities;

namespace AutoConnect.Core.Interfaces;

public interface IVehicleDataRepository : IRepository<VehicleData>
{
    Task<IEnumerable<VehicleData>> GetBySessionIdAsync(Guid sessionId);
    Task<IEnumerable<VehicleData>> GetBySessionIdAndDateRangeAsync(Guid sessionId, DateTime startDate, DateTime endDate);
    Task<VehicleData?> GetLatestBySessionIdAsync(Guid sessionId);
    Task<IEnumerable<VehicleData>> GetRecentBySessionIdAsync(Guid sessionId, int count = 50);
}
