using System.Text;
using System.Text.Json;
using Ino.Core;
using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.TripPlanner.Rfw;

/// <summary>
/// Renders a column of activity cards. Each card carries an indoor/outdoor
/// hint plus a weather badge derived from the trip's climatology by
/// <see cref="MockActivityCorpus.For"/>. The Flutter <c>ActivityCard</c>
/// widget surfaces the badge (e.g., "Rainy day pick", "Sunny day pick")
/// next to the rating so the user can pick weather-appropriately.
/// </summary>
internal static class ActivityCardListBuilder
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static RfwPayload Build(IReadOnlyList<ActivityOption> activities)
    {
        ArgumentNullException.ThrowIfNull(activities);

        var dsl = new StringBuilder();
        dsl.AppendLine("import ino.activities;");
        dsl.AppendLine("import core.widgets;");
        dsl.AppendLine("widget root = Column(children: [");
        for (var i = 0; i < activities.Count; i++)
        {
            dsl.AppendLine("  ActivityCard(");
            dsl.AppendLine($"    name: data.items.{i}.name,");
            dsl.AppendLine($"    category: data.items.{i}.category,");
            dsl.AppendLine($"    rating: data.items.{i}.rating,");
            dsl.AppendLine($"    isIndoor: data.items.{i}.isIndoor,");
            dsl.AppendLine($"    weatherBadge: data.items.{i}.weatherBadge,");
            dsl.AppendLine($"    activityId: data.items.{i}.activityId,");
            dsl.AppendLine($"    onSelect: event 'activity.selected' {{ activityId: data.items.{i}.activityId }},");
            dsl.Append("  )");
            if (i < activities.Count - 1) dsl.Append(',');
            dsl.AppendLine();
        }
        dsl.AppendLine("]);");

        var items = activities.Select(a => new Dictionary<string, object?>
        {
            ["name"] = a.Name,
            ["category"] = a.Category,
            ["rating"] = a.Rating,
            ["isIndoor"] = a.IsIndoor,
            ["weatherBadge"] = a.WeatherBadge,
            ["activityId"] = a.Id,
        }).ToList();

        var data = JsonSerializer.SerializeToUtf8Bytes(new { items }, JsonOptions);

        return new RfwPayload(
            LibraryName: "ino.travel.activities",
            DescriptionDsl: Encoding.UTF8.GetBytes(dsl.ToString()),
            DataPayload: data);
    }
}
