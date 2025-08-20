using AutoConnect.Core.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AutoConnect.Core.Entities;

public class VehicleSession : BaseEntity
{
    [Required]
    public Guid ClientId { get; set; }

    [MaxLength(17)] // VIN is exactly 17 characters
    public string? VIN { get; set; }

    public DateTime SessionStartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SessionEndedAt { get; set; }

    public VehicleConnectionStatus ConnectionStatus { get; set; } = VehicleConnectionStatus.Disconnected;

    [MaxLength(100)]
    public string? ObdAdapterType { get; set; }

    [MaxLength(50)]
    public string? ObdProtocol { get; set; }

    public int? PingLatencyMs { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? DataUsageMB { get; set; }

    [MaxLength(500)]
    public string? LastErrorMessage { get; set; }

    public DateTime? LastDataReceivedAt { get; set; }

    // Navigation properties
    [ForeignKey("ClientId")]
    public virtual Client Client { get; set; } = null!;

    public virtual ICollection<VehicleData> VehicleDataPoints { get; set; } = new List<VehicleData>();
}