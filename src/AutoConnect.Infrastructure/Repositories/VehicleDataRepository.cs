// src/AutoConnect.Infrastructure/Repositories/VehicleDataRepository.cs
using AutoConnect.Core.Entities;
using AutoConnect.Core.Interfaces;
using AutoConnect.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AutoConnect.Infrastructure.Repositories;

public class VehicleDataRepository : Repository<VehicleData>, IVehicleDataRepository
{
    public VehicleDataRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<VehicleData>> GetBySessionIdAsync(Guid sessionId)
    {
        return await _dbSet
            .Where(vd => vd.VehicleSessionId == sessionId)
            .OrderBy(vd => vd.Timestamp)
            .ToListAsync();
    }

    public async Task<IEnumerable<VehicleData>> GetBySessionIdAndDateRangeAsync(Guid sessionId, DateTime startDate, DateTime endDate)
    {
        return await _dbSet
            .Where(vd => vd.VehicleSessionId == sessionId &&
                        vd.Timestamp >= startDate &&
                        vd.Timestamp <= endDate)
            .OrderBy(vd => vd.Timestamp)
            .ToListAsync();
    }

    public async Task<VehicleData?> GetLatestBySessionIdAsync(Guid sessionId)
    {
        return await _dbSet
            .Where(vd => vd.VehicleSessionId == sessionId)
            .OrderByDescending(vd => vd.Timestamp)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<VehicleData>> GetRecentBySessionIdAsync(Guid sessionId, int count = 50)
    {
        return await _dbSet
            .Where(vd => vd.VehicleSessionId == sessionId)
            .OrderByDescending(vd => vd.Timestamp)
            .Take(count)
            .ToListAsync();
    }
}