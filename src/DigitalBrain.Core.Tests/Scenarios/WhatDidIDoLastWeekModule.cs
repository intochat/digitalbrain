using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests.Scenarios;

// Domain facts the owner "did" — journaled before recall; WeekSummary items derive only from these.
public sealed record WeekEmailLogged(string Subject, DateTimeOffset At) : Synapse;

public sealed record WeekMeetingLogged(string Title, DateTimeOffset At) : Synapse;

public sealed record WeekTaskLogged(string Title, DateTimeOffset At) : Synapse;

public sealed record WeekSummaryAsked(DateTimeOffset RangeStart, DateTimeOffset RangeEnd) : Synapse;

public sealed record WeekItem(string Kind, string Title, DateTimeOffset At);

public sealed record WeekSummary(ImmutableArray<WeekItem> Items) : Synapse;

public sealed class WeekRecallState
{
#pragma warning disable CA1002, CA2227, CA1819
    public List<WeekItem> Items { get; set; } = [];
#pragma warning restore CA1002, CA2227, CA1819
}

// Session/introspection stand-in: durable State is filled only by handlers of journaled domain facts.
// WeekSummary is built from that structure — never from free-form invented memory.
public sealed class WeekRecall : Neuron<WeekRecallState>,
    INeuron<WeekEmailLogged>,
    INeuron<WeekMeetingLogged>,
    INeuron<WeekTaskLogged>,
    IAnswers<WeekSummaryAsked, WeekSummary>
{
    public Task HandleAsync(WeekEmailLogged fact, CancellationToken cancellationToken)
    {
        State.Items.Add(new WeekItem("email", fact.Subject, fact.At));
        return Task.CompletedTask;
    }

    public Task HandleAsync(WeekMeetingLogged fact, CancellationToken cancellationToken)
    {
        State.Items.Add(new WeekItem("meeting", fact.Title, fact.At));
        return Task.CompletedTask;
    }

    public Task HandleAsync(WeekTaskLogged fact, CancellationToken cancellationToken)
    {
        State.Items.Add(new WeekItem("task", fact.Title, fact.At));
        return Task.CompletedTask;
    }

    public Task<WeekSummary?> HandleAsync(WeekSummaryAsked question, CancellationToken cancellationToken)
    {
        var inRange = State.Items
            .Where(item => item.At >= question.RangeStart && item.At < question.RangeEnd)
            .OrderBy(item => item.At)
            .ToImmutableArray();
        return Task.FromResult<WeekSummary?>(new WeekSummary(inRange));
    }
}
