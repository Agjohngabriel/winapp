// src/AutoConnect.Infrastructure/Repositories/ClientRepository.cs
using AutoConnect.Core.Entities;
using AutoConnect.Core.Enums;
using AutoConnect.Core.Interfaces;
using AutoConnect.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AutoConnect.Infrastructure.Repositories;

public class ClientRepository : Repository<Client>, IClientRepository
{
    public ClientRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Client?> GetByEmailAsync(string email)
    {
        return await _dbSet.FirstOrDefaultAsync(c => c.Email == email);
    }

    public async Task<IEnumerable<Client>> GetByStatusAsync(ClientStatus status)
    {
        return await _dbSet.Where(c => c.Status == status).ToListAsync();
    }

    public async Task<Client?> GetWithSessionsAsync(Guid id)
    {
        return await _dbSet
            .Include(c => c.VehicleSessions)
            .FirstOrDefaultAsync(c => c.Id == id);
    }
}
