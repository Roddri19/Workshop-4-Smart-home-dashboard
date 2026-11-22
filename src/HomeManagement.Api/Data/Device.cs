namespace HomeManagement.Api.Data;

/// <summary>
/// A smart device registered in the home. Stored in the database.
/// </summary>
public class Device
{
    public int Id { get; set; }

    /// <summary>Name reported by the device (or given by the user). Empty until known.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Base URL of the device API, e.g. https://localhost:1234 (no trailing slash).</summary>
    public required string Address { get; set; }

    /// <summary>True when the last health check got an answer from the device.</summary>
    public bool IsOnline { get; set; }

    /// <summary>Last time the device answered.</summary>
    public DateTimeOffset? LastSeenUtc { get; set; }

    /// <summary>Last time we tried to reach the device (answered or not).</summary>
    public DateTimeOffset? LastCheckedUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>Latest metrics reported by the device (uptime, temperature...).</summary>
    public Dictionary<string, string> Metrics { get; set; } = [];

    /// <summary>Actions the device supports, as reported by its /config endpoint.</summary>
    public List<DeviceAction> Actions { get; set; } = [];

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Address : Name;
}

public enum DeviceCommandType
{
    Get = 0,
    Post = 1
}

/// <summary>
/// One action a device can perform. Mirrors the emulator's DeviceAction record.
/// </summary>
public record DeviceAction(string Action, DeviceCommandType CommandType, string Command, string? CommandArgs = null);
