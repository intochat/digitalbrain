namespace DigitalBrain.Kernel;

internal readonly record struct AliasMark(NeuronId Publisher, string Alias, Position Last);
internal readonly record struct PositionedSignal(Position Position, string Alias, JournaledSignal Journaled);
internal sealed record JournalPage(Position Floor, Position Last, IReadOnlyList<PositionedSignal> Signals);

internal interface IJournalView
{
    Position Last { get; }
    SubscriptionIndex Subscriptions { get; }
    Position FloorOf(NeuronId? publisher);
    Position LastOf(NeuronId publisher);
    IEnumerable<AliasMark> Marks(NeuronId? publisher);
    long SequenceAt(NeuronId publisher, Position position);
    Task<JournalPage> ReadAfter(Position after, NeuronId? publisher, int limit, CancellationToken ct);
}

internal interface INudge
{
    void Nudge(NeuronId subscriber, Position upTo);
}

internal sealed class Fanout(
    IJournalView journal,
    INudge nudge,
    Func<string, Type?> typeOf,
    Action<NeuronId, IReadOnlyList<Signal>> listeners) : ISignalNudge
{
    public Task Appended(NeuronId publisher, IReadOnlyList<JournaledSignal> appended, Position last, CancellationToken ct)
    {
        listeners(publisher, [.. appended.Select(item => item.Signal)]);
        Appended(publisher, [.. appended.Select(item => SignalAliases.Of(item.Signal))], last);
        return Task.CompletedTask;
    }

    public void Appended(NeuronId publisher, IReadOnlyCollection<string> aliases, Position last)
    {
        foreach (var subscriber in journal.Subscriptions.Hearing(publisher, aliases, typeOf))
        {
            nudge.Nudge(subscriber, last);
        }
    }

    public Task Offered(NeuronId publisher, Signal signal, CancellationToken ct)
    {
        listeners(publisher, [signal]);
        return Task.CompletedTask;
    }

    public int Reconcile()
    {
        var due = journal.Subscriptions.Due(journal, typeOf, Delivery.SkipFlush);
        foreach (var subscriber in due)
        {
            nudge.Nudge(subscriber, journal.Last);
        }
        return due.Count;
    }
}
