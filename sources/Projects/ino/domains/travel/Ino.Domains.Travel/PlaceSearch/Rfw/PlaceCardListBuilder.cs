using System.Text;
using System.Text.Json;
using Ino.Core;
using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.PlaceSearch.Rfw;

internal static class PlaceCardListBuilder
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static RfwPayload Build(IReadOnlyList<PlaceOption> places)
    {
        ArgumentNullException.ThrowIfNull(places);

        var dsl = new StringBuilder();
        dsl.AppendLine("import ino.places;");
        dsl.AppendLine("import core.widgets;");
        dsl.AppendLine("widget root = Column(children: [");
        for (var i = 0; i < places.Count; i++)
        {
            dsl.AppendLine("  PlaceCard(");
            dsl.AppendLine($"    name: data.items.{i}.name,");
            dsl.AppendLine($"    type: data.items.{i}.type,");
            dsl.AppendLine($"    rating: data.items.{i}.rating,");
            dsl.AppendLine($"    reviewCount: data.items.{i}.reviewCount,");
            dsl.AppendLine($"    description: data.items.{i}.description,");
            dsl.AppendLine($"    placeId: data.items.{i}.placeId,");
            dsl.AppendLine($"    onSelect: event 'place.selected' {{ placeId: data.items.{i}.placeId }},");
            dsl.Append("  )");
            if (i < places.Count - 1) dsl.Append(',');
            dsl.AppendLine();
        }
        dsl.AppendLine("]);");

        var items = places.Select(p => new Dictionary<string, object?>
        {
            ["name"] = p.Name,
            ["type"] = p.Category,
            ["rating"] = p.Rating,
            ["reviewCount"] = 200,
            ["description"] = $"{p.Category} attraction in Bali — handpicked.",
            ["placeId"] = p.Id,
        }).ToList();

        var data = JsonSerializer.SerializeToUtf8Bytes(new { items }, JsonOptions);

        return new RfwPayload(
            LibraryName: "ino.travel.places",
            DescriptionDsl: Encoding.UTF8.GetBytes(dsl.ToString()),
            DataPayload: data);
    }
}
