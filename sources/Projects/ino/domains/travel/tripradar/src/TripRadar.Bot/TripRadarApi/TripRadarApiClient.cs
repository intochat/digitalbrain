using System.Globalization;
using System.Text.Json;

namespace TripRadar.Bot.TripRadarApi;

internal sealed class TripRadarApiClient(
    HttpClient httpClient,
    ILogger<TripRadarApiClient> logger) : ITripRadarApiClient
{
    public async Task<IReadOnlyList<ActiveTracking>> LoadActiveTrackingsAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await httpClient.GetAsync("/api/v1/scheduled-executions", ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to load active trackings: {StatusCode} {Body}", (int)response.StatusCode, body);
                return [];
            }

            return ParseTrackings(body);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error loading active trackings from TripRadar API");
            return [];
        }
    }

    private static IReadOnlyList<ActiveTracking> ParseTrackings(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<ActiveTracking>();

        if (!doc.RootElement.TryGetProperty("scheduledExecutions", out var executions)
            || executions.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in executions.EnumerateArray())
        {
            if (!IsScheduledFlight(item))
                continue;

            var tracking = TryParseTracking(item);
            if (tracking is not null)
                list.Add(tracking);
        }

        return list;
    }

    private static bool IsScheduledFlight(JsonElement item)
    {
        if (!item.TryGetProperty("serviceType", out var serviceType))
            return false;

        return string.Equals(serviceType.GetString(), "ScheduledFlight", StringComparison.OrdinalIgnoreCase);
    }

    private static ActiveTracking? TryParseTracking(JsonElement item)
    {
        var id = TryParseGuid(item, "scheduledExecutionUniqueId");
        var username = TryGetString(item, "username") ?? string.Empty;
        var dep = TryGetString(item, "departureAirportCode") ?? string.Empty;
        var dest = TryGetString(item, "destinationAirportCode") ?? string.Empty;
        var depDate = TryParseDate(item, "departureDate");

        return new ActiveTracking(id, username, dep, dest, depDate);
    }

    private static string? TryGetString(JsonElement element, string property)
        => element.TryGetProperty(property, out var prop) ? prop.GetString() : null;

    private static Guid TryParseGuid(JsonElement element, string property)
    {
        if (element.TryGetProperty(property, out var prop)
            && prop.ValueKind == JsonValueKind.String
            && Guid.TryParse(prop.GetString(), out var guid))
            return guid;
        return Guid.NewGuid();
    }

    private static DateOnly TryParseDate(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var prop))
            return DateOnly.FromDateTime(DateTime.UtcNow);

        if (prop.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(prop.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
            return DateOnly.FromDateTime(dto.UtcDateTime);

        return DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
