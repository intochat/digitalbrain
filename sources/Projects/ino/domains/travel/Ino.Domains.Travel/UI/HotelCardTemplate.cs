using System.Text;
using System.Text.Json;
using Ino.Domains.Travel.Contracts;

namespace Ino.Domains.Travel.UI;

/// <summary>
/// Builds the RFW payload for a hotel-results card. Follows the same conventions
/// as <see cref="FlightCardTemplate"/>: LF-only description bytes (Dart RFW
/// parser rejects CRLF), camelCase data keys to match the Flutter widget.
/// </summary>
public static class HotelCardTemplate
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static (byte[] Description, byte[] Data) BuildList(IReadOnlyList<HotelSummary> hotels)
    {
        var description = BuildListDescription(hotels.Count);

        var items = hotels.Select(h => new Dictionary<string, object?>
        {
            ["name"] = h.Name,
            ["location"] = h.Location,
            ["price"] = h.PricePerNightUsd,
            ["rating"] = h.Rating,
            ["stars"] = h.Stars,
        }).ToList();

        var data = JsonSerializer.SerializeToUtf8Bytes(new { items }, JsonOptions);
        return (Encoding.UTF8.GetBytes(description), data);
    }

    static string BuildListDescription(int count)
    {
        var sb = new StringBuilder();
        sb.Append("import ino.hotels;\n");
        sb.Append("import core.widgets;\n");
        sb.Append("widget root = Column(children: [\n");
        for (var i = 0; i < count; i++)
        {
            sb.Append("  HotelCard(\n");
            sb.Append($"    name: data.items.{i}.name,\n");
            sb.Append($"    location: data.items.{i}.location,\n");
            sb.Append($"    price: data.items.{i}.price,\n");
            sb.Append($"    rating: data.items.{i}.rating,\n");
            sb.Append($"    stars: data.items.{i}.stars\n");
            sb.Append("  )");
            if (i < count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append("]);\n");
        return sb.ToString();
    }
}
