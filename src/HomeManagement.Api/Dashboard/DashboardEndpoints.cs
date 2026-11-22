using HomeManagement.Api.Data;
using HomeManagement.Api.Devices;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Api.Dashboard;

public record DashboardDto(
    int Total,
    int Online,
    int Offline,
    DateTimeOffset GeneratedUtc,
    int CheckIntervalSeconds,
    IReadOnlyList<DeviceDto> Devices);

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        // Everything the dashboard page needs in a single request.
        app.MapGet("/api/dashboard", async (HomeDbContext db, TimeProvider time,
            Microsoft.Extensions.Options.IOptions<Monitoring.MonitoringOptions> monitoring, CancellationToken ct) =>
        {
            var devices = await db.Devices.AsNoTracking().ToListAsync(ct);

            // Online devices first, then alphabetically.
            var ordered = devices
                .OrderByDescending(d => d.IsOnline)
                .ThenBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(DeviceDto.From)
                .ToList();

            var online = devices.Count(d => d.IsOnline);
            return new DashboardDto(
                devices.Count,
                online,
                devices.Count - online,
                time.GetUtcNow(),
                monitoring.Value.IntervalSeconds,
                ordered);
        }).WithTags("Dashboard");

        return app;
    }
}
