using System.Text;
using System.Text.Json;
using Ino.Core;
using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.TripPlanner.Rfw;

/// <summary>
/// Final confirmation card. Consolidates every selection (weather context,
/// flight, hotel, event, activity) into one TripSummaryCard the user can
/// glance at before booking. No interactive affordance — closes the Plan
/// Trip neuron.
/// </summary>
internal static class TripSummaryBuilder
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static RfwPayload Build(
        string destination,
        WeatherClimatology weather,
        string? flightAirline,
        string? hotelName,
        string? eventTitle,
        string? activityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentNullException.ThrowIfNull(weather);

        var dsl = new StringBuilder();
        dsl.AppendLine("import ino.summary;");
        dsl.AppendLine("import core.widgets;");
        dsl.AppendLine("widget root = TripSummaryCard(");
        dsl.AppendLine("  destination: data.destination,");
        dsl.AppendLine("  weatherSummary: data.weatherSummary,");
        dsl.AppendLine("  flight: data.flight,");
        dsl.AppendLine("  hotel: data.hotel,");
        dsl.AppendLine("  event: data.event,");
        dsl.AppendLine("  activity: data.activity,");
        dsl.AppendLine(");");

        var weatherSummary =
            $"{weather.Month} {weather.Destination} — {weather.Season} season, " +
            $"{weather.AvgTempC}°C avg, {(int)(weather.RainProbability * 100)}% rain.";

        var data = JsonSerializer.SerializeToUtf8Bytes(new
        {
            destination,
            weatherSummary,
            flight = flightAirline ?? "(none selected)",
            hotel = hotelName ?? "(none selected)",
            evt = eventTitle ?? "(skipped)",     // serialised below as "event"
            activity = activityName ?? "(none selected)",
        }, JsonOptions);

        // The anonymous-property name `evt` would serialise as "evt"; emit
        // the canonical "event" key explicitly so the DSL field reference
        // lines up.
        var dataString = Encoding.UTF8.GetString(data).Replace("\"evt\":", "\"event\":");

        return new RfwPayload(
            LibraryName: "ino.travel.summary",
            DescriptionDsl: Encoding.UTF8.GetBytes(dsl.ToString()),
            DataPayload: Encoding.UTF8.GetBytes(dataString));
    }
}
