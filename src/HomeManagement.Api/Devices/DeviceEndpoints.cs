using HomeManagement.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HomeManagement.Api.Devices;

public record DeviceDto(
    int Id,
    string Name,
    string Address,
    bool IsOnline,
    DateTimeOffset? LastSeenUtc,
    DateTimeOffset? LastCheckedUtc,
    IReadOnlyDictionary<string, string> Metrics,
    IReadOnlyList<DeviceActionDto> Actions)
{
    public static DeviceDto From(Device d) => new(
        d.Id,
        d.DisplayName,
        d.Address,
        d.IsOnline,
        d.LastSeenUtc,
        d.LastCheckedUtc,
        d.Metrics,
        d.Actions.Select(a => new DeviceActionDto(a.Action, a.CommandType.ToString().ToUpperInvariant())).ToList());
}

public record DeviceActionDto(string Name, string Method);

public record RegisterDeviceRequest(string? Address, string? Name);

public record ActionResultDto(string Device, string Action, string Output);

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var devices = app.MapGroup("/api/devices").WithTags("Devices");

        // List all devices.
        devices.MapGet("/", async (HomeDbContext db, CancellationToken ct) =>
        {
            var all = await db.Devices.AsNoTracking().ToListAsync(ct);
            return all.OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase).Select(DeviceDto.From);
        });

        // One device.
        devices.MapGet("/{id:int}", async (int id, HomeDbContext db, CancellationToken ct) =>
            await db.Devices.FindAsync([id], ct) is { } device
                ? Results.Ok(DeviceDto.From(device))
                : Results.NotFound());

        // Register a new device by its address. It is saved even if it is offline right now.
        devices.MapPost("/", async (RegisterDeviceRequest request, HomeDbContext db, DeviceStatusService status,
            TimeProvider time, CancellationToken ct) =>
        {
            if (!TryNormalizeAddress(request.Address, out var address))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["address"] = ["Use a full http or https address, for example https://localhost:1234."]
                });
            }

            if (await db.Devices.AnyAsync(d => d.Address == address, ct))
            {
                return Results.Problem($"A device with the address {address} is already registered.",
                    statusCode: StatusCodes.Status409Conflict);
            }

            var device = new Device
            {
                Address = address,
                Name = request.Name?.Trim() ?? string.Empty,
                CreatedUtc = time.GetUtcNow()
            };

            await status.RefreshAsync(device, ct);
            db.Devices.Add(device);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/devices/{device.Id}", DeviceDto.From(device));
        });

        // Remove a device.
        devices.MapDelete("/{id:int}", async (int id, HomeDbContext db, CancellationToken ct) =>
        {
            var deleted = await db.Devices.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
            return deleted == 0 ? Results.NotFound() : Results.NoContent();
        });

        // Check the device right now instead of waiting for the background monitor.
        devices.MapPost("/{id:int}/refresh", async (int id, HomeDbContext db, DeviceStatusService status,
            CancellationToken ct) =>
        {
            if (await db.Devices.FindAsync([id], ct) is not { } device)
            {
                return Results.NotFound();
            }

            await status.RefreshAsync(device, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(DeviceDto.From(device));
        });

        // Live information straight from the device (not from the database).
        devices.MapGet("/{id:int}/info", async (int id, HomeDbContext db, DeviceClient client,
            CancellationToken ct) =>
        {
            if (await db.Devices.FindAsync([id], ct) is not { } device)
            {
                return Results.NotFound();
            }

            try
            {
                return Results.Ok(await client.GetInfoAsync(device.Address, ct));
            }
            catch (Exception ex) when (DeviceStatusService.IsUnreachable(ex) && !ct.IsCancellationRequested)
            {
                return DeviceUnavailable(device, ex);
            }
        });

        // Send a command to the device, e.g. POST /api/devices/1/actions/Start%20Notepad
        devices.MapPost("/{id:int}/actions/{action}", async (int id, string action, HomeDbContext db,
            DeviceClient client, DeviceStatusService status, CancellationToken ct) =>
        {
            if (await db.Devices.FindAsync([id], ct) is not { } device)
            {
                return Results.NotFound();
            }

            try
            {
                var deviceAction = FindAction(device, action);
                if (deviceAction is null)
                {
                    // Maybe the device learned new actions: ask it again once.
                    await status.LoadConfigAsync(device, ct);
                    await db.SaveChangesAsync(ct);
                    deviceAction = FindAction(device, action);
                }

                if (deviceAction is null)
                {
                    return Results.Problem($"The device {device.DisplayName} has no action called '{action}'.",
                        statusCode: StatusCodes.Status404NotFound);
                }

                // Only the device's own actions can be run: the command comes from its /config, never from the caller.
                var output = await client.ExecuteAsync(device.Address, deviceAction, ct);
                return Results.Ok(new ActionResultDto(device.DisplayName, deviceAction.Action, output));
            }
            catch (Exception ex) when (DeviceStatusService.IsUnreachable(ex) && !ct.IsCancellationRequested)
            {
                return DeviceUnavailable(device, ex);
            }
        });

        return app;
    }

    private static DeviceAction? FindAction(Device device, string action) =>
        device.Actions.FirstOrDefault(a => string.Equals(a.Action, action.Trim(), StringComparison.OrdinalIgnoreCase));

    private static IResult DeviceUnavailable(Device device, Exception ex) =>
        Results.Problem($"Could not complete the request on {device.DisplayName}: {ex.Message}",
            statusCode: StatusCodes.Status502BadGateway);

    internal static bool TryNormalizeAddress(string? input, out string address)
    {
        address = string.Empty;
        if (string.IsNullOrWhiteSpace(input)
            || !Uri.TryCreate(input.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        // Keep only scheme://host:port so the same device can't be registered twice with different spellings.
        address = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
        return true;
    }
}
