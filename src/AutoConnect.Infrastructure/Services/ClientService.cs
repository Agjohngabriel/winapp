// src/AutoConnect.Infrastructure/Services/ClientService.cs
using AutoConnect.Core.Entities;
using AutoConnect.Core.Enums;
using AutoConnect.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AutoConnect.Infrastructure.Services;

public class ClientService : IClientService
{
    private readonly IClientRepository _clientRepository;
    private readonly ILogger<ClientService> _logger;

    public ClientService(IClientRepository clientRepository, ILogger<ClientService> logger)
    {
        _clientRepository = clientRepository;
        _logger = logger;
    }

    public async Task<Client?> GetClientByIdAsync(Guid id)
    {
        return await _clientRepository.GetByIdAsync(id);
    }

    public async Task<Client?> GetClientByEmailAsync(string email)
    {
        return await _clientRepository.GetByEmailAsync(email);
    }

    public async Task<IEnumerable<Client>> GetAllClientsAsync()
    {
        return await _clientRepository.GetAllAsync();
    }

    public async Task<IEnumerable<Client>> GetClientsByStatusAsync(ClientStatus status)
    {
        return await _clientRepository.GetByStatusAsync(status);
    }

    public async Task<Client> CreateClientAsync(string name, string email, string? notes = null)
    {
        // Check if email already exists
        if (await EmailExistsAsync(email))
        {
            throw new InvalidOperationException($"Client with email '{email}' already exists.");
        }

        var client = new Client
        {
            Name = name,
            Email = email,
            Notes = notes,
            Status = ClientStatus.Inactive,
            VpnCertificate = GenerateTemporaryCertificate() // TODO: Replace with actual certificate generation
        };

        var createdClient = await _clientRepository.CreateAsync(client);
        _logger.LogInformation("Created new client: {ClientId} - {Email}", createdClient.Id, createdClient.Email);

        return createdClient;
    }

    public async Task<Client> UpdateClientAsync(Guid id, string? name = null, string? email = null, ClientStatus? status = null, string? notes = null)
    {
        var client = await _clientRepository.GetByIdAsync(id);
        if (client == null)
        {
            throw new InvalidOperationException($"Client with ID '{id}' not found.");
        }

        // Check if email is being updated and already exists
        if (email != null && email != client.Email && await EmailExistsAsync(email))
        {
            throw new InvalidOperationException($"Client with email '{email}' already exists.");
        }

        if (name != null) client.Name = name;
        if (email != null) client.Email = email;
        if (status.HasValue) client.Status = status.Value;
        if (notes != null) client.Notes = notes;

        var updatedClient = await _clientRepository.UpdateAsync(client);
        _logger.LogInformation("Updated client: {ClientId}", updatedClient.Id);

        return updatedClient;
    }

    public async Task DeleteClientAsync(Guid id)
    {
        if (!await ClientExistsAsync(id))
        {
            throw new InvalidOperationException($"Client with ID '{id}' not found.");
        }

        await _clientRepository.DeleteAsync(id);
        _logger.LogInformation("Deleted client: {ClientId}", id);
    }

    public async Task<bool> ClientExistsAsync(Guid id)
    {
        return await _clientRepository.ExistsAsync(id);
    }

    public async Task<bool> EmailExistsAsync(string email)
    {
        var client = await _clientRepository.GetByEmailAsync(email);
        return client != null;
    }

    private string GenerateTemporaryCertificate()
    {
        // TODO: Implement actual certificate generation logic
        return $"temp-cert-{Guid.NewGuid()}";
    }
}