// src/AutoConnect.Infrastructure/Repositories/VehicleSessionRepository.cs
using AutoConnect.Core.Entities;
using AutoConnect.Core.Enums;
using AutoConnect.Core.Interfaces;
using AutoConnect.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AutoConnect.Infrastructure.Repositories;

public class VehicleSessionRepository : Repository<VehicleSession>, IVehicleSessionRepository
{
    public VehicleSessionRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<VehicleSession>> GetByClientIdAsync(Guid clientId)
    {
        return await _dbSet.Where(vs => vs.ClientId == clientId).ToListAsync();
    }

    public async Task<VehicleSession?> GetActiveSessionByClientIdAsync(Guid clientId)
    {
        return await _dbSet
            .Where(vs => vs.ClientId == clientId && vs.SessionEndedAt == null)
            .OrderByDescending(vs => vs.SessionStartedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<VehicleSession>> GetByVINAsync(string vin)
    {
        return await _dbSet.Where(vs => vs.VIN == vin).ToListAsync();
    }

    public async Task<VehicleSession?> GetWithDataAsync(Guid id)
    {
        return await _dbSet
            .Include(vs => vs.VehicleDataPoints)
            .FirstOrDefaultAsync(vs => vs.Id == id);
    }

    public async Task<IEnumerable<VehicleSession>> GetByStatusAsync(VehicleConnectionStatus status)
    {
        return await _dbSet.Where(vs => vs.ConnectionStatus == status).ToListAsync();
    }
}
