using HomeManagement.Api.Data;
using HomeManagement.Api.Devices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HomeManagement.Api.Monitoring;

public class MonitoringOptions
{
    /// <summary>How often every device is checked.</summary>
    public int IntervalSeconds { get; set; } = 10;
}

/// <summary>
/// Background job: every few seconds it checks every registered device
/// and saves whether it is online, plus its latest metrics.
/// </summary>
public sealed class DeviceMonitor(
    IServiceScopeFactory scopeFactory,
    IOptions<MonitoringOptions> options,
    ILogger<DeviceMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.IntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        try
        {
            do
            {
                try
                {
                    await CheckAllDevicesAsync(stoppingToken);
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    // Never let one bad cycle stop the monitor.
                    logger.LogError(ex, "Device check cycle failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The app is shutting down: this is the normal way out, not an error.
        }
    }

    private async Task CheckAllDevicesAsync(CancellationToken ct)
    {
        // The monitor lives for the whole app, but DbContext and DeviceClient are short-lived:
        // create a fresh scope for each cycle.
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HomeDbContext>();
        var status = scope.ServiceProvider.GetRequiredService<DeviceStatusService>();

        var devices = await db.Devices.ToListAsync(ct);

        // Check all devices at the same time, so one slow device doesn't delay the others.
        await Task.WhenAll(devices.Select(d => status.RefreshAsync(d, ct)));

        await db.SaveChangesAsync(ct);
    }
}
