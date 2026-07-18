using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ino.Domains.Travel.Tests.Storyboard;

public sealed class StoryboardRecorder
{
    private readonly List<StoryboardEvent> events = new();

    public StoryboardRecorder(string id, string label)
    {
        Id = id;
        Label = label;
    }

    public string Id { get; }
    public string Label { get; }
    public IReadOnlyList<StoryboardEvent> Events => events;
    public double DurationSeconds => events.Count == 0 ? 0 : events[^1].T + 0.4;

    public void AppendOrb(double t, string state)
        => events.Add(new OrbEvent(t, state));

    public void AppendUtter(double t, string text)
        => events.Add(new UtterEvent(t, text));

    public void AppendSynapse(
        double t, string from, string to, JsonElement payload, bool gold)
        => events.Add(new SynapseEvent(t, from, to, payload, gold));

    public void AppendCard(double t, string id, string stage, string? fromCluster)
        => events.Add(new CardEvent(t, id, stage, fromCluster));

    public string ToJson()
    {
        var root = new JsonObject
        {
            ["id"] = Id,
            ["label"] = Label,
            ["duration_s"] = DurationSeconds,
            ["events"] = new JsonArray(events.Select(SerializeEvent).ToArray()),
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonNode SerializeEvent(StoryboardEvent ev) => ev switch
    {
        OrbEvent o => new JsonObject
        {
            ["t"] = o.T,
            ["kind"] = "orb",
            ["state"] = o.State,
        },
        UtterEvent u => new JsonObject
        {
            ["t"] = u.T,
            ["kind"] = "utter",
            ["text"] = u.Text,
        },
        SynapseEvent s => new JsonObject
        {
            ["t"] = s.T,
            ["kind"] = "syn",
            ["from"] = s.From,
            ["to"] = s.To,
            ["payload"] = JsonNode.Parse(s.Payload.GetRawText()),
            ["gold"] = s.Gold,
        },
        CardEvent c => new JsonObject
        {
            ["t"] = c.T,
            ["kind"] = "card",
            ["id"] = c.Id,
            ["stage"] = c.Stage,
            ["from"] = c.FromCluster,
        },
        _ => throw new InvalidOperationException($"Unknown event {ev.GetType()}"),
    };
}
