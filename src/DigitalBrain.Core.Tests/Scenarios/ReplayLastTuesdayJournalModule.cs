using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests.Scenarios;

// Domain facts seeded "last Tuesday morning" — replay cites only these, never free invention.

public sealed record ReplayEmailLogged(string Subject, DateTimeOffset At) : Synapse;

public sealed record ReplayTaskLogged(string Title, DateTimeOffset At) : Synapse;

public sealed record ReplayChatLogged(string Text, DateTimeOffset At) : Synapse;

public sealed record JournalRangeQuery(
    DateTimeOffset RangeStart,
    DateTimeOffset RangeEnd) : Synapse;

public sealed record JournalSliceItem(
    string Kind,
    string Title,
    DateTimeOffset At);

public sealed record JournalSlice(ImmutableArray<JournalSliceItem> Items) : Synapse;

public sealed record ReplayTimelineSurfaced(
    string RangeLabel,
    ImmutableArray<string> CitedTitles) : Synapse;

public sealed class JournalReplayState
{
#pragma warning disable CA1002, CA2227
    public List<JournalSliceItem> Items { get; set; } = [];
#pragma warning restore CA1002, CA2227
}

// Introspection: slice is built only from journaled domain facts in range.
public sealed class JournalReplayIndex : Neuron<JournalReplayState>,
    INeuron<ReplayEmailLogged>,
    INeuron<ReplayTaskLogged>,
    INeuron<ReplayChatLogged>,
    IAnswers<JournalRangeQuery, JournalSlice>
{
    public Task HandleAsync(ReplayEmailLogged fact, CancellationToken cancellationToken)
    {
        State.Items.Add(new JournalSliceItem("email", fact.Subject, fact.At));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ReplayTaskLogged fact, CancellationToken cancellationToken)
    {
        State.Items.Add(new JournalSliceItem("task", fact.Title, fact.At));
        return Task.CompletedTask;
    }

    public Task HandleAsync(ReplayChatLogged fact, CancellationToken cancellationToken)
    {
        State.Items.Add(new JournalSliceItem("chat", fact.Text, fact.At));
        return Task.CompletedTask;
    }

    public Task<JournalSlice?> HandleAsync(JournalRangeQuery question, CancellationToken cancellationToken)
    {
        var inRange = State.Items
            .Where(item => item.At >= question.RangeStart && item.At < question.RangeEnd)
            .OrderBy(item => item.At)
            .ToImmutableArray();
        return Task.FromResult<JournalSlice?>(new JournalSlice(inRange));
    }
}

// Shell surface after slice answer — cites titles from the slice body only.
public sealed class ReplayTimelineProjector : Neuron, INeuron<JournalSlice>
{
    public Task HandleAsync(JournalSlice fact, CancellationToken cancellationToken)
    {
        // Session ask answers do not ambient-fan JournalSlice to INeuron — projector is for
        // directed/session proofs via separate Emit path if needed. Keep as catalog sink.
        return Task.CompletedTask;
    }
}

// Chat stand-in: on ReplayAsked, Ask range query then surface citations from the answer.
public sealed record ReplayAsked(DateTimeOffset RangeStart, DateTimeOffset RangeEnd, string Label) : Synapse;

public sealed class ReplayChat : Neuron, INeuron<ReplayAsked>, INeuron<JournalSlice>
{
    public Task HandleAsync(ReplayAsked fact, CancellationToken cancellationToken)
    {
        Ask<JournalSlice>(new JournalRangeQuery(fact.RangeStart, fact.RangeEnd));
        return Task.CompletedTask;
    }

    public Task HandleAsync(JournalSlice fact, CancellationToken cancellationToken)
    {
        var titles = fact.Items.Select(item => item.Title).ToImmutableArray();
        Emit(new ReplayTimelineSurfaced(
            RangeLabel: $"{fact.Items.Length} events",
            CitedTitles: titles));
        return Task.CompletedTask;
    }
}

public sealed class ReplaySurfaceLedger : Neuron, INeuron<ReplayTimelineSurfaced>
{
    public Task HandleAsync(ReplayTimelineSurfaced fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
