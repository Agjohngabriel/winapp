// src/AutoConnect.Core/Interfaces/IClientRepository.cs
using AutoConnect.Core.Entities;
using AutoConnect.Core.Enums;

namespace AutoConnect.Core.Interfaces;

public interface IClientRepository : IRepository<Client>
{
    Task<Client?> GetByEmailAsync(string email);
    Task<IEnumerable<Client>> GetByStatusAsync(ClientStatus status);
    Task<Client?> GetWithSessionsAsync(Guid id);
}