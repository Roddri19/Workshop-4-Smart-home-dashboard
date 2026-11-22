using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Api.Data;

public class DeviceSeedOptions
{
    /// <summary>Device addresses registered automatically when the database is empty.</summary>
    public List<string> Seed { get; set; } = [];
}

public static class DatabaseSetup
{
    /// <summary>
    /// Creates the SQLite database file on first run and registers the demo devices,
    /// so the project works right after cloning, with no extra steps.
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomeDbContext>();
        var time = scope.ServiceProvider.GetRequiredService<TimeProvider>();
        var seed = app.Configuration.GetSection("Devices").Get<DeviceSeedOptions>() ?? new DeviceSeedOptions();

        await db.Database.EnsureCreatedAsync();

        if (await db.Devices.AnyAsync())
        {
            return;
        }

        foreach (var input in seed.Seed.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Devices.DeviceEndpoints.TryNormalizeAddress(input, out var address))
            {
                db.Devices.Add(new Device { Address = address, CreatedUtc = time.GetUtcNow() });
            }
        }

        await db.SaveChangesAsync();
    }
}
