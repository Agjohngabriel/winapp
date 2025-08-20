using AutoConnect.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoConnect.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    // DbSets
    public DbSet<Client> Clients { get; set; }
    public DbSet<VehicleSession> VehicleSessions { get; set; }
    public DbSet<VehicleData> VehicleData { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Client entity
        modelBuilder.Entity<Client>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.HasIndex(c => c.Email).IsUnique();
            entity.Property(c => c.Email).IsRequired();
            entity.Property(c => c.Name).IsRequired();

            // Configure the relationship
            entity.HasMany(c => c.VehicleSessions)
                  .WithOne(vs => vs.Client)
                  .HasForeignKey(vs => vs.ClientId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure VehicleSession entity
        modelBuilder.Entity<VehicleSession>(entity =>
        {
            entity.HasKey(vs => vs.Id);
            entity.HasIndex(vs => vs.VIN);
            entity.HasIndex(vs => new { vs.ClientId, vs.SessionStartedAt });

            // Configure the relationship
            entity.HasMany(vs => vs.VehicleDataPoints)
                  .WithOne(vd => vd.VehicleSession)
                  .HasForeignKey(vd => vd.VehicleSessionId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        // Configure VehicleData entity
        modelBuilder.Entity<VehicleData>(entity =>
        {
            entity.HasKey(vd => vd.Id);
            entity.HasIndex(vd => new { vd.VehicleSessionId, vd.Timestamp });
            entity.HasIndex(vd => vd.Timestamp);
        });

        // Configure enum conversions
        modelBuilder.Entity<Client>()
            .Property(c => c.Status)
            .HasConversion<int>();

        modelBuilder.Entity<VehicleSession>()
            .Property(vs => vs.ConnectionStatus)
            .HasConversion<int>();

        modelBuilder.Entity<VehicleData>()
            .Property(vd => vd.IgnitionStatus)
            .HasConversion<int>();

        // Add soft delete filter
        modelBuilder.Entity<Client>().HasQueryFilter(c => !c.IsDeleted);
        modelBuilder.Entity<VehicleSession>().HasQueryFilter(vs => !vs.IsDeleted);
        modelBuilder.Entity<VehicleData>().HasQueryFilter(vd => !vd.IsDeleted);
    }

    public override int SaveChanges()
    {
        UpdateTimestamps();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        UpdateTimestamps();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void UpdateTimestamps()
    {
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is BaseEntity && (e.State == EntityState.Added || e.State == EntityState.Modified));

        foreach (var entry in entries)
        {
            var entity = (BaseEntity)entry.Entity;

            if (entry.State == EntityState.Added)
            {
                entity.CreatedAt = DateTime.UtcNow;
            }
            else if (entry.State == EntityState.Modified)
            {
                entity.UpdatedAt = DateTime.UtcNow;
                // Prevent overwriting CreatedAt
                entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
            }
        }
    }
}