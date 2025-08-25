// src/AutoConnect.Core/Interfaces/IClientService.cs
using AutoConnect.Core.Entities;
using AutoConnect.Core.Enums;

namespace AutoConnect.Core.Interfaces;

public interface IClientService
{
    Task<Client?> GetClientByIdAsync(Guid id);
    Task<Client?> GetClientByEmailAsync(string email);
    Task<IEnumerable<Client>> GetAllClientsAsync();
    Task<IEnumerable<Client>> GetClientsByStatusAsync(ClientStatus status);
    Task<Client> CreateClientAsync(string name, string email, string? notes = null);
    Task<Client> UpdateClientAsync(Guid id, string? name = null, string? email = null, ClientStatus? status = null, string? notes = null);
    Task DeleteClientAsync(Guid id);
    Task<bool> ClientExistsAsync(Guid id);
    Task<bool> EmailExistsAsync(string email);
}