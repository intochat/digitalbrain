using System.Text;
using System.Text.Json;
using Ino.Core;
using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.HotelSearch.Rfw;

internal static class HotelCardListBuilder
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static RfwPayload Build(IReadOnlyList<HotelOption> hotels)
    {
        ArgumentNullException.ThrowIfNull(hotels);

        var dsl = new StringBuilder();
        dsl.AppendLine("import ino.hotels;");
        dsl.AppendLine("import core.widgets;");
        dsl.AppendLine("widget root = Column(children: [");
        for (var i = 0; i < hotels.Count; i++)
        {
            dsl.AppendLine("  HotelCard(");
            dsl.AppendLine($"    name: data.items.{i}.name,");
            dsl.AppendLine($"    location: data.items.{i}.location,");
            dsl.AppendLine($"    price: data.items.{i}.price,");
            dsl.AppendLine($"    rating: data.items.{i}.rating,");
            dsl.AppendLine($"    stars: data.items.{i}.stars,");
            dsl.AppendLine($"    hotelId: data.items.{i}.hotelId,");
            dsl.AppendLine($"    onSelect: event 'hotel.selected' {{ hotelId: data.items.{i}.hotelId }},");
            dsl.Append("  )");
            if (i < hotels.Count - 1) dsl.Append(',');
            dsl.AppendLine();
        }
        dsl.AppendLine("]);");

        var items = hotels.Select(h => new Dictionary<string, object?>
        {
            ["name"] = h.Name,
            ["location"] = h.City,
            ["price"] = (int)h.PricePerNight,
            ["rating"] = h.Rating,
            ["stars"] = (int)Math.Round(h.Rating),
            ["hotelId"] = h.Id,
        }).ToList();

        var data = JsonSerializer.SerializeToUtf8Bytes(new { items }, JsonOptions);

        return new RfwPayload(
            LibraryName: "ino.travel.hotels",
            DescriptionDsl: Encoding.UTF8.GetBytes(dsl.ToString()),
            DataPayload: data);
    }
}
