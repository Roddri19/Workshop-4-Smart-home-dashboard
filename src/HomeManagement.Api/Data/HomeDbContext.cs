using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HomeManagement.Api.Data;

public class HomeDbContext(DbContextOptions<HomeDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public DbSet<Device> Devices => Set<Device>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var device = modelBuilder.Entity<Device>();

        device.HasIndex(d => d.Address).IsUnique();
        device.Property(d => d.Address).HasMaxLength(500);
        device.Property(d => d.Name).HasMaxLength(200);
        device.Ignore(d => d.DisplayName);

        // Metrics and actions are small and always read together with the device,
        // so they are stored as JSON text columns instead of separate tables.
        device.Property(d => d.Metrics)
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<Dictionary<string, string>>(v, Json) ?? new Dictionary<string, string>(),
                new ValueComparer<Dictionary<string, string>>(
                    (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                    v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                    v => new Dictionary<string, string>(v)));

        device.Property(d => d.Actions)
            .HasConversion(
                v => JsonSerializer.Serialize(v, Json),
                v => JsonSerializer.Deserialize<List<DeviceAction>>(v, Json) ?? new List<DeviceAction>(),
                new ValueComparer<List<DeviceAction>>(
                    (a, b) => JsonSerializer.Serialize(a, Json) == JsonSerializer.Serialize(b, Json),
                    v => JsonSerializer.Serialize(v, Json).GetHashCode(),
                    v => v.ToList()));
    }
}
