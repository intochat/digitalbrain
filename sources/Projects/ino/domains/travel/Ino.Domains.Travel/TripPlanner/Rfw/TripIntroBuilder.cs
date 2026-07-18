using System.Text;
using System.Text.Json;
using Ino.Core;
using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.TripPlanner.Rfw;

/// <summary>
/// Initial-state RFW for the rich Plan Trip flow. Combines a weather
/// summary banner (climatology context) with the flight cards below in a
/// single Column. Imports both <c>ino.weather</c> and <c>ino.flights</c>
/// libraries so the Flutter runtime can render both widget families from
/// one DSL.
///
/// LibraryName is set to <c>ino.travel.intro</c> for the gateway content
/// type — distinguishes this composite hop from a plain flights-only
/// response so observability/telemetry can group it correctly.
/// </summary>
internal static class TripIntroBuilder
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static RfwPayload Build(WeatherClimatology weather, IReadOnlyList<FlightOption> flights)
    {
        ArgumentNullException.ThrowIfNull(weather);
        ArgumentNullException.ThrowIfNull(flights);

        var dsl = new StringBuilder();
        dsl.AppendLine("import ino.weather;");
        dsl.AppendLine("import ino.flights;");
        dsl.AppendLine("import core.widgets;");
        dsl.AppendLine("widget root = Column(children: [");
        dsl.AppendLine("  WeatherSummaryCard(");
        dsl.AppendLine("    destination: data.weather.destination,");
        dsl.AppendLine("    month: data.weather.month,");
        dsl.AppendLine("    season: data.weather.season,");
        dsl.AppendLine("    avgTempC: data.weather.avgTempC,");
        dsl.AppendLine("    rainProbability: data.weather.rainProbability,");
        dsl.AppendLine("  ),");
        for (var i = 0; i < flights.Count; i++)
        {
            dsl.AppendLine("  FlightCard(");
            dsl.AppendLine($"    airline: data.flights.{i}.airline,");
            dsl.AppendLine($"    from: data.flights.{i}.from,");
            dsl.AppendLine($"    to: data.flights.{i}.to,");
            dsl.AppendLine($"    price: data.flights.{i}.price,");
            dsl.AppendLine($"    date: data.flights.{i}.date,");
            dsl.AppendLine($"    duration: data.flights.{i}.duration,");
            dsl.AppendLine($"    flightId: data.flights.{i}.flightId,");
            dsl.AppendLine($"    onSelect: event 'flight.selected' {{ flightId: data.flights.{i}.flightId }},");
            dsl.Append("  )");
            if (i < flights.Count - 1) dsl.Append(',');
            dsl.AppendLine();
        }
        dsl.AppendLine("]);");

        var weatherJson = new
        {
            destination = weather.Destination,
            month = weather.Month,
            season = weather.Season,
            avgTempC = weather.AvgTempC,
            rainProbability = weather.RainProbability,
        };
        var flightsJson = flights.Select(f => new Dictionary<string, object?>
        {
            ["airline"] = f.Airline,
            ["from"] = f.Origin,
            ["to"] = f.Destination,
            ["price"] = (int)f.Price,
            ["date"] = "Trip dates",
            ["duration"] = FormatDuration(f.DurationMin),
            ["flightId"] = f.Id,
        }).ToList();

        var data = JsonSerializer.SerializeToUtf8Bytes(
            new { weather = weatherJson, flights = flightsJson },
            JsonOptions);

        return new RfwPayload(
            LibraryName: "ino.travel.intro",
            DescriptionDsl: Encoding.UTF8.GetBytes(dsl.ToString()),
            DataPayload: data);
    }

    static string FormatDuration(int minutes)
    {
        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? $"{h}h" : $"{h}h {m}m";
    }
}
