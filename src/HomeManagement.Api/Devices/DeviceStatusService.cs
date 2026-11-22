using System.Text.Json;
using HomeManagement.Api.Data;

namespace HomeManagement.Api.Devices;

/// <summary>
/// Asks a device how it is doing and stores the answer on the Device entity.
/// Used by the background monitor and by the "refresh now" endpoint.
/// The caller is responsible for calling SaveChanges.
/// </summary>
public sealed class DeviceStatusService(DeviceClient client, TimeProvider time, ILogger<DeviceStatusService> logger)
{
    public async Task RefreshAsync(Device device, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        device.LastCheckedUtc = now;

        try
        {
            var info = await client.GetInfoAsync(device.Address, ct);

            device.IsOnline = true;
            device.LastSeenUtc = now;
            device.Metrics = info.Metrics ?? [];

            // The name and the list of actions rarely change: ask /config only when we don't know them yet.
            if (string.IsNullOrWhiteSpace(device.Name) || device.Actions.Count == 0)
            {
                await LoadConfigAsync(device, ct);
            }
        }
        catch (Exception ex) when (IsUnreachable(ex) && !ct.IsCancellationRequested)
        {
            if (device.IsOnline)
            {
                logger.LogWarning("Device {Device} went offline: {Reason}", device.DisplayName, ex.Message);
            }

            device.IsOnline = false;
        }
    }

    public async Task LoadConfigAsync(Device device, CancellationToken ct)
    {
        var config = await client.GetConfigAsync(device.Address, ct);

        if (string.IsNullOrWhiteSpace(device.Name) && !string.IsNullOrWhiteSpace(config.Name))
        {
            device.Name = config.Name.Trim();
        }

        device.Actions = config.Actions ?? [];
    }

    /// <summary>Errors that just mean "the device did not answer properly" (as opposed to bugs).</summary>
    public static bool IsUnreachable(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException;
}
