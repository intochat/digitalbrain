using System.Text;
using System.Text.Json;
using Ino.Core;
using Ino.Domains.Travel.Rfw;

namespace Ino.Domains.Travel.TripPlanner.Rfw;

/// <summary>
/// Renders a column of event cards plus a "Skip events" affordance. Each
/// card binds <c>onSelect</c> to <c>event 'event.selected' { eventId: ... }</c>;
/// the skip button binds to <c>event 'events.skipped' { }</c>. The plan
/// grain dispatches both via <c>HandleRfwEventAsync</c>.
/// </summary>
internal static class EventCardListBuilder
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static RfwPayload Build(IReadOnlyList<EventOption> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var dsl = new StringBuilder();
        dsl.AppendLine("import ino.events;");
        dsl.AppendLine("import core.widgets;");
        dsl.AppendLine("widget root = Column(children: [");
        for (var i = 0; i < events.Count; i++)
        {
            dsl.AppendLine("  EventCard(");
            dsl.AppendLine($"    title: data.items.{i}.title,");
            dsl.AppendLine($"    dateLabel: data.items.{i}.dateLabel,");
            dsl.AppendLine($"    venueName: data.items.{i}.venueName,");
            dsl.AppendLine($"    category: data.items.{i}.category,");
            dsl.AppendLine($"    ticketSummary: data.items.{i}.ticketSummary,");
            dsl.AppendLine($"    description: data.items.{i}.description,");
            dsl.AppendLine($"    eventId: data.items.{i}.eventId,");
            dsl.AppendLine($"    onSelect: event 'event.selected' {{ eventId: data.items.{i}.eventId }},");
            dsl.AppendLine("  ),");
        }
        // Skip button — RFW emits an empty-args event the plan handles as
        // "user opted out of events; advance to activities".
        dsl.AppendLine("  EventSkipButton(");
        dsl.AppendLine("    onSkip: event 'events.skipped' {},");
        dsl.AppendLine("  ),");
        dsl.AppendLine("]);");

        var items = events.Select(e => new Dictionary<string, object?>
        {
            ["title"] = e.Title,
            ["dateLabel"] = e.DateLabel,
            ["venueName"] = e.VenueName,
            ["category"] = e.Category,
            ["ticketSummary"] = e.TicketSummary,
            ["description"] = e.Description,
            ["eventId"] = e.Id,
        }).ToList();

        var data = JsonSerializer.SerializeToUtf8Bytes(new { items }, JsonOptions);

        return new RfwPayload(
            LibraryName: "ino.travel.events",
            DescriptionDsl: Encoding.UTF8.GetBytes(dsl.ToString()),
            DataPayload: data);
    }
}
