using System.Text;
using System.Text.Json;
using Ino.Domains.Travel.Contracts;

namespace Ino.Domains.Travel.UI;

/// <summary>
/// Builds the RFW payload for a places-results card. LF-only description bytes,
/// camelCase data keys matching <c>ui/components/place_card.dart</c>.
/// </summary>
public static class PlaceCardTemplate
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static (byte[] Description, byte[] Data) BuildList(IReadOnlyList<PlaceSummary> places)
    {
        var description = BuildListDescription(places.Count);

        var items = places.Select(p => new Dictionary<string, object?>
        {
            ["name"] = p.Name,
            ["type"] = p.Type,
            ["rating"] = p.Rating,
            ["reviewCount"] = p.ReviewCount,
            ["description"] = p.Description,
        }).ToList();

        var data = JsonSerializer.SerializeToUtf8Bytes(new { items }, JsonOptions);
        return (Encoding.UTF8.GetBytes(description), data);
    }

    static string BuildListDescription(int count)
    {
        var sb = new StringBuilder();
        sb.Append("import ino.places;\n");
        sb.Append("import core.widgets;\n");
        sb.Append("widget root = Column(children: [\n");
        for (var i = 0; i < count; i++)
        {
            sb.Append("  PlaceCard(\n");
            sb.Append($"    name: data.items.{i}.name,\n");
            sb.Append($"    type: data.items.{i}.type,\n");
            sb.Append($"    rating: data.items.{i}.rating,\n");
            sb.Append($"    reviewCount: data.items.{i}.reviewCount,\n");
            sb.Append($"    description: data.items.{i}.description\n");
            sb.Append("  )");
            if (i < count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append("]);\n");
        return sb.ToString();
    }
}
