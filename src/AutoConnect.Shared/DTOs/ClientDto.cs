// src/AutoConnect.Shared/DTOs/ClientDto.cs
using AutoConnect.Core.Enums;

namespace AutoConnect.Shared.DTOs;

public class ClientDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? LastKnownIpAddress { get; set; }
    public DateTime? LastConnectedAt { get; set; }
    public ClientStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateClientRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class UpdateClientRequest
{
    public string? Name { get; set; }
    public string? Email { get; set; }
    public ClientStatus? Status { get; set; }
    public string? Notes { get; set; }
}