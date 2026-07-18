using System.Text;
using System.Text.Json;
using Ino.Core;
using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.FlightSearch.Rfw;

/// <summary>
/// Builds an <see cref="RfwPayload"/> describing a column of flight cards.
/// Each card binds <c>onSelect</c> to <c>event 'flight.selected' { flightId: ... }</c>
/// — the canonical RFW event syntax (<c>event &quot;name&quot; { args }</c>) verified
/// against <c>package:rfw</c> 1.1.x source (<c>lib/src/dart/text.dart</c>) and
/// documented in <c>docs/rfw-research-notes.md</c> R3.
///
/// Field names + library imports match the existing Flutter <c>FlightCard</c>
/// widget at <c>clients/ino.flutter/lib/ui/components/flight_card.dart</c>
/// (airline / from / to / price[int] / date / duration), with two new fields
/// (<c>flightId</c> + <c>onSelect</c>) consumed by the Slice 4 Select button.
/// </summary>
internal static class FlightCardListBuilder
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static RfwPayload Build(IReadOnlyList<FlightOption> flights)
    {
        ArgumentNullException.ThrowIfNull(flights);

        var dsl = new StringBuilder();
        dsl.AppendLine("import ino.flights;");
        dsl.AppendLine("import core.widgets;");
        dsl.AppendLine("widget root = Column(children: [");
        for (var i = 0; i < flights.Count; i++)
        {
            dsl.AppendLine("  FlightCard(");
            dsl.AppendLine($"    airline: data.items.{i}.airline,");
            dsl.AppendLine($"    from: data.items.{i}.from,");
            dsl.AppendLine($"    to: data.items.{i}.to,");
            dsl.AppendLine($"    price: data.items.{i}.price,");
            dsl.AppendLine($"    date: data.items.{i}.date,");
            dsl.AppendLine($"    duration: data.items.{i}.duration,");
            dsl.AppendLine($"    flightId: data.items.{i}.flightId,");
            dsl.AppendLine($"    onSelect: event 'flight.selected' {{ flightId: data.items.{i}.flightId }},");
            dsl.Append("  )");
            if (i < flights.Count - 1) dsl.Append(',');
            dsl.AppendLine();
        }
        dsl.AppendLine("]);");

        var items = flights.Select(f => new Dictionary<string, object?>
        {
            ["airline"] = f.Airline,
            ["from"] = f.Origin,
            ["to"] = f.Destination,
            ["price"] = (int)f.Price,
            ["date"] = "Next month",
            ["duration"] = FormatDuration(f.DurationMin),
            ["flightId"] = f.Id,
        }).ToList();

        var data = JsonSerializer.SerializeToUtf8Bytes(new { items }, JsonOptions);

        return new RfwPayload(
            LibraryName: "ino.travel.flights",
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
