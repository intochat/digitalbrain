using System.Collections.Immutable;

namespace DigitalBrain.UI;

[GenerateSerializer, Alias("db.ui.activity-state")]
internal sealed record ActivityState
{
    [Id(0)] public ActivityExecutionChanged? Root { get; init; }
    [Id(1)] public ImmutableDictionary<string, ActivityExecutionChanged> Operations { get; init; } = [];
    [Id(2)] public ImmutableList<ActivityExecutionChanged> Events { get; init; } = [];
    [Id(3)] public bool RootSettled { get; init; }
    [Id(4)] public long Version { get; init; }

    public ActivityState? Apply(ActivityExecutionChanged fact)
    {
        if (Operations.TryGetValue(fact.OperationId, out var previous)
            && (previous.Timestamp > fact.Timestamp
                || previous == fact
                || (previous.Timestamp == fact.Timestamp && IsTerminal(previous.Phase) && !IsTerminal(fact.Phase))))
        {
            return null;
        }

        var root = Root ?? fact;
        if (fact.CausationId is null && root.CausationId is not null)
        {
            root = fact;
        }
        var rootSettled = RootSettled;
        if (fact.CausationId is null && IsTerminal(fact.Phase))
        {
            rootSettled = true;
        }
        var operations = Operations.SetItem(fact.OperationId, fact);
        var version = Version + 1;
        var events = Events.Add(fact);
        // The current operation ledger remains intact when the display trace rolls over.
        if (events.Count > 512)
        {
            events = events.RemoveRange(0, events.Count - 512);
        }
        return this with
        {
            Root = root,
            RootSettled = rootSettled,
            Operations = operations,
            Version = version,
            Events = events,
        };
    }

    public ActivityView View()
    {
        var root = Root ?? throw new InvalidOperationException("An activity requires an execution fact.");
        var values = Operations.Values;
        var status = values.Any(f => f.Phase == "running") ? "running"
            : values.Any(f => f.Phase == "waiting") ? "waiting"
            : values.Any(f => f.Phase == "failed") ? "failed"
            : values.Any(f => f.Phase == "cancelled") ? "cancelled"
            : RootSettled ? "completed" : "observed";
        var latest = values.MaxBy(f => f.Timestamp)!;
        var id = root.CorrelationId.ToString();
        var participants = values.SelectMany<ActivityExecutionChanged, string>(f => f.Target is { } target
                ? [Instance(f.Source), Instance(target)] : [Instance(f.Source)])
            .Distinct(StringComparer.Ordinal).ToArray();
        return new(id, id, root.SignalId.ToString(), root.SignalType, root.Title ?? root.SignalType,
            status, root.Timestamp, latest.Timestamp, participants,
            Events.Select(f => new ActivityEventView(f.OperationId, f.SignalId.ToString(), f.CausationId?.ToString(),
                Instance(f.Source), f.Target is { } target ? Instance(target) : null, f.SignalType, f.Phase,
                f.Timestamp, f.Detail)).ToArray(), root.CommandId,
            latest.Detail ?? (status == "observed" ? "Observed signal; no tracked execution has settled it." : null), Version);
    }

    private static bool IsTerminal(string phase) => phase is "completed" or "failed" or "cancelled";
    private static string Instance(Abstractions.Identity.NeuronId id) => $"{id.Type}:{id.Name}";
}
