using System.Text;
using System.Text.Json;
using Ino.Domains.Travel.Contracts;

namespace Ino.Domains.Travel.UI;

/// <summary>
/// Builds the Remote Flutter Widget payload for a flight-results card.
/// FlightSearch produces <see cref="FlightSummary"/> records directly — no
/// <c>JsonElement</c> shim. The Dart RFW parser rejects Windows CRLF, so
/// description bytes are always written as LF-only UTF-8.
/// </summary>
public static class FlightCardTemplate
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Render a skeleton list with <paramref name="count"/> placeholder cards.
    /// Same RFW description as <see cref="BuildList"/>, but every field is
    /// zero/empty so the Flutter <c>FlightCard</c> widget can detect the
    /// placeholder state and render shimmer bars. The gateway emits this as
    /// the first frame of the streaming <c>Chat</c> response
    /// (<c>is_skeleton=true</c>) while the real neuron handler runs.
    /// </summary>
    public static (byte[] Description, byte[] Data) BuildSkeleton(int count)
    {
        var description = BuildListDescription(count);

        var items = Enumerable.Range(0, count).Select(_ => new Dictionary<string, object?>
        {
            ["airline"] = "",
            ["from"] = "",
            ["to"] = "",
            ["price"] = 0,
            ["date"] = "",
            ["duration"] = "",
        }).ToList();

        var data = JsonSerializer.SerializeToUtf8Bytes(new { items }, JsonOptions);
        return (Encoding.UTF8.GetBytes(description), data);
    }

    /// <summary>
    /// Render a list of flights as an RFW description + data pair. The Dart
    /// RFW parser rejects Windows CRLF, so description bytes are written as
    /// LF-only regardless of the author platform.
    /// </summary>
    public static (byte[] Description, byte[] Data) BuildList(IReadOnlyList<FlightSummary> flights)
    {
        var description = BuildListDescription(flights.Count);

        // Shape the data wrapper to match the template's `data.items.N.airline`
        // path bindings. Field names stay camelCase to match the Flutter widget
        // consumer in clients/ino.flutter/lib/ui/components/flight_card.dart.
        var items = flights.Select(f => new Dictionary<string, object?>
        {
            ["airline"] = f.Airline,
            ["from"] = f.FromCode,
            ["to"] = f.ToCode,
            ["price"] = f.PriceUsd,
            ["date"] = f.DepartTime,
            ["duration"] = f.Duration,
        }).ToList();

        var data = JsonSerializer.SerializeToUtf8Bytes(new { items }, JsonOptions);
        return (Encoding.UTF8.GetBytes(description), data);
    }

    static string BuildListDescription(int count)
    {
        // Build with explicit \n terminators — Encoding.UTF8.GetBytes on a
        // CRLF-containing string would yield bytes the Dart RFW parser rejects.
        var sb = new StringBuilder();
        sb.Append("import ino.flights;\n");
        sb.Append("import core.widgets;\n");
        sb.Append("widget root = Column(children: [\n");
        for (var i = 0; i < count; i++)
        {
            sb.Append("  FlightCard(\n");
            sb.Append($"    airline: data.items.{i}.airline,\n");
            sb.Append($"    from: data.items.{i}.from,\n");
            sb.Append($"    to: data.items.{i}.to,\n");
            sb.Append($"    price: data.items.{i}.price,\n");
            sb.Append($"    date: data.items.{i}.date,\n");
            sb.Append($"    duration: data.items.{i}.duration\n");
            sb.Append("  )");
            if (i < count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append("]);\n");
        return sb.ToString();
    }
}
