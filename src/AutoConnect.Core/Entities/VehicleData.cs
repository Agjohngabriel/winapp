using AutoConnect.Core.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoConnect.Core.Entities;

public class VehicleData : BaseEntity
{
    [Required]
    public Guid VehicleSessionId { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "decimal(5,2)")]
    public decimal? BatteryVoltage { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal? KL15Voltage { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal? KL30Voltage { get; set; }

    public IgnitionStatus IgnitionStatus { get; set; } = IgnitionStatus.Off;

    public int? EngineRPM { get; set; }

    [Column(TypeName = "decimal(5,1)")]
    public decimal? VehicleSpeed { get; set; }

    [Column(TypeName = "decimal(5,1)")]
    public decimal? CoolantTemperature { get; set; }

    [Column(TypeName = "decimal(5,1)")]
    public decimal? FuelLevel { get; set; }

    [MaxLength(2000)]
    public string? DiagnosticTroubleCodes { get; set; } // JSON array of DTCs

    [MaxLength(4000)]
    public string? RawObdData { get; set; } // Raw OBD response for debugging

    // Navigation property
    [ForeignKey("VehicleSessionId")]
    public virtual VehicleSession VehicleSession { get; set; } = null!;
}