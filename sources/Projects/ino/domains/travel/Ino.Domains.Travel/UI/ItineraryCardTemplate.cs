using System.Text;
using System.Text.Json;
using Ino.Domains.Travel.Contracts;

namespace Ino.Domains.Travel.UI;

/// <summary>
/// Builds the RFW payload for a composed itinerary card. Uses only
/// <c>core.widgets</c> + <c>material.widgets</c> primitives (no custom Flutter
/// <c>ItineraryCard</c> widget yet) so this card renders on today's Flutter
/// client without a client-side change. A later slice can add a polished
/// widget and swap the template.
/// </summary>
public static class ItineraryCardTemplate
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static (byte[] Description, byte[] Data) Build(
        string destination,
        IReadOnlyList<ItineraryDay> days)
    {
        var description = BuildDescription(days);

        var dayItems = days.Select(d => new Dictionary<string, object?>
        {
            ["header"] = $"Day {d.DayNumber}: {d.Title}",
            ["lines"] = d.Items.Select(i => new Dictionary<string, object?>
            {
                ["text"] = i,
            }).ToList(),
        }).ToList();

        var data = JsonSerializer.SerializeToUtf8Bytes(new
        {
            destination,
            days = dayItems,
        }, JsonOptions);

        return (Encoding.UTF8.GetBytes(description), data);
    }

    static string BuildDescription(IReadOnlyList<ItineraryDay> days)
    {
        var sb = new StringBuilder();
        sb.Append("import core.widgets;\n");
        sb.Append("import material.widgets;\n");
        sb.Append("widget root = Column(children: [\n");
        sb.Append("  Text(text: ['Itinerary: ', data.destination]),\n");
        for (var i = 0; i < days.Count; i++)
        {
            sb.Append("  Column(children: [\n");
            sb.Append($"    Text(text: data.days.{i}.header),\n");
            for (var j = 0; j < days[i].Items.Length; j++)
            {
                sb.Append($"    Text(text: data.days.{i}.lines.{j}.text)");
                if (j < days[i].Items.Length - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append("  ])");
            if (i < days.Count - 1) sb.Append(',');
            sb.Append('\n');
        }
        sb.Append("]);\n");
        return sb.ToString();
    }
}
