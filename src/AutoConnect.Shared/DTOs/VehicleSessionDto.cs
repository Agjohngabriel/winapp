// src/AutoConnect.Shared/DTOs/VehicleSessionDto.cs
using AutoConnect.Core.Enums;

namespace AutoConnect.Shared.DTOs;

public class VehicleSessionDto
{
    public Guid Id { get; set; }
    public Guid ClientId { get; set; }
    public string? VIN { get; set; }
    public DateTime SessionStartedAt { get; set; }
    public DateTime? SessionEndedAt { get; set; }
    public VehicleConnectionStatus ConnectionStatus { get; set; }
    public string? ObdAdapterType { get; set; }
    public string? ObdProtocol { get; set; }
    public int? PingLatencyMs { get; set; }
    public decimal? DataUsageMB { get; set; }
    public string? LastErrorMessage { get; set; }
    public DateTime? LastDataReceivedAt { get; set; }
}

public class CreateVehicleSessionRequest
{
    public Guid ClientId { get; set; }
    public string? VIN { get; set; }
    public string? ObdAdapterType { get; set; }
    public string? ObdProtocol { get; set; }
}

public class UpdateVehicleSessionRequest
{
    public string? VIN { get; set; }
    public VehicleConnectionStatus? ConnectionStatus { get; set; }
    public string? ObdAdapterType { get; set; }
    public string? ObdProtocol { get; set; }
    public int? PingLatencyMs { get; set; }
    public decimal? DataUsageMB { get; set; }
    public string? LastErrorMessage { get; set; }
}