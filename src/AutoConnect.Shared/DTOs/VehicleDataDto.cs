// src/AutoConnect.Shared/DTOs/VehicleDataDto.cs

using AutoConnect.Core.Enums;

namespace AutoConnect.Shared.DTOs;

public class VehicleDataDto
{
    public Guid Id { get; set; }
    public Guid VehicleSessionId { get; set; }
    public DateTime Timestamp { get; set; }
    public decimal? BatteryVoltage { get; set; }
    public decimal? KL15Voltage { get; set; }
    public decimal? KL30Voltage { get; set; }
    public IgnitionStatus IgnitionStatus { get; set; }
    public int? EngineRPM { get; set; }
    public decimal? VehicleSpeed { get; set; }
    public decimal? CoolantTemperature { get; set; }
    public decimal? FuelLevel { get; set; }
    public string? DiagnosticTroubleCodes { get; set; }
}

public class CreateVehicleDataRequest
{
    public Guid VehicleSessionId { get; set; }
    public decimal? BatteryVoltage { get; set; }
    public decimal? KL15Voltage { get; set; }
    public decimal? KL30Voltage { get; set; }
    public IgnitionStatus IgnitionStatus { get; set; }
    public int? EngineRPM { get; set; }
    public decimal? VehicleSpeed { get; set; }
    public decimal? CoolantTemperature { get; set; }
    public decimal? FuelLevel { get; set; }
    public string? DiagnosticTroubleCodes { get; set; }
    public string? RawObdData { get; set; }
}