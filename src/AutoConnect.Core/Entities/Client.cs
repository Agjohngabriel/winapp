using AutoConnect.Core.Enums;
using System.ComponentModel.DataAnnotations;

namespace AutoConnect.Core.Entities;

public class Client : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string VpnCertificate { get; set; } = string.Empty;

    [MaxLength(45)] // IPv6 max length
    public string? LastKnownIpAddress { get; set; }

    public DateTime? LastConnectedAt { get; set; }

    public ClientStatus Status { get; set; } = ClientStatus.Inactive;

    [MaxLength(1000)]
    public string? Notes { get; set; }

    // Navigation properties
    public virtual ICollection<VehicleSession> VehicleSessions { get; set; } = new List<VehicleSession>();
}