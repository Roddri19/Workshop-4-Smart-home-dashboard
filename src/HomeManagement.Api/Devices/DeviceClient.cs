using System.Net.Http.Json;
using System.Text;
using HomeManagement.Api.Data;

namespace HomeManagement.Api.Devices;

/// <summary>What a device returns from GET /info.</summary>
public record DeviceInfoResponse(string? Name, string? Address, Dictionary<string, string>? Metrics);

/// <summary>What a device returns from GET /config.</summary>
public record DeviceConfigResponse(string? Name, string? Address, List<DeviceAction>? Actions);

/// <summary>
/// Talks to a single smart device over HTTP. Registered as a typed HttpClient,
/// so the HttpClient (timeouts, certificates) is configured once in Program.cs.
/// </summary>
public sealed class DeviceClient(HttpClient http)
{
    public async Task<DeviceInfoResponse> GetInfoAsync(string address, CancellationToken ct) =>
        await http.GetFromJsonAsync<DeviceInfoResponse>(Url(address, "info"), ct)
        ?? throw new HttpRequestException("The device returned an empty /info response.");

    public async Task<DeviceConfigResponse> GetConfigAsync(string address, CancellationToken ct) =>
        await http.GetFromJsonAsync<DeviceConfigResponse>(Url(address, "config"), ct)
        ?? throw new HttpRequestException("The device returned an empty /config response.");

    /// <summary>Runs one of the device's own actions and returns its text output.</summary>
    public async Task<string> ExecuteAsync(string address, DeviceAction action, CancellationToken ct)
    {
        var url = Url(address, action.Command);

        using var response = action.CommandType == DeviceCommandType.Get
            ? await http.GetAsync(url, ct)
            : await http.PostAsync(url, Body(action.CommandArgs), ct);

        var output = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"The device answered {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
        }

        return output;
    }

    private static StringContent? Body(string? json) =>
        json is null ? null : new StringContent(json, Encoding.UTF8, "application/json");

    private static Uri Url(string address, string path) => new($"{address}/{path.TrimStart('/')}");
}
