using System.Collections.Immutable;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Core.Endpoints;
using Brain.Core.Outbox;

namespace Brain.Core.Neurons;

internal sealed class GraphRoute(
    EndpointAddress target,
    SynapseKey synapse,
    long revision,
    ContractId inputContract,
    ContractId outputContract,
    ReshapeId? reshape)
{
    public EndpointAddress Target { get; set; } = target;

    public SynapseKey Synapse { get; set; } = synapse;

    public long Revision { get; set; } = revision;

    public ContractId InputContract { get; set; } = inputContract;

    public ContractId OutputContract { get; set; } = outputContract;

    public ReshapeId? Reshape { get; set; } = reshape;
}

internal interface IGraphRouteResolver
{
    Task<IReadOnlyList<GraphRoute>> ResolveAsync(
        EndpointAddress source,
        ContractId eventContract,
        ActivityContext activity,
        CancellationToken cancellationToken);
}

internal readonly record struct NeuronStateSnapshot<TState>(long Version, TState State);

internal sealed class NeuronTurnCommit<TState>(
    TState state,
    ImmutableArray<JournalEntry> journal,
    ImmutableArray<OutboxEntry> emissions,
    ImmutableArray<DirectedMessage> directedMessages)
{
    public TState State { get; } = state;

    public ImmutableArray<JournalEntry> Journal { get; } = journal;

    public ImmutableArray<OutboxEntry> Emissions { get; } = emissions;

    public ImmutableArray<DirectedMessage> DirectedMessages { get; } = directedMessages;
}

internal interface INeuronTurnStore<TState> : IOutboxStore
{
    TState State { get; }

    IReadOnlyList<JournalEntry> Journal { get; }

    NeuronStateSnapshot<TState> Read();

    void Commit(NeuronStateSnapshot<TState> expected, NeuronTurnCommit<TState> commit);
}

internal sealed class NeuronTurn<TState>
{
    private readonly NeuronStateSnapshot<TState> _before;
    private readonly List<JournalEntry> _journal = [];
    private readonly List<OutboxEntry> _emissions = [];
    private readonly List<DirectedMessage> _directedMessages = [];

    internal NeuronTurn(
        NeuronStateSnapshot<TState> before,
        EndpointAddress source,
        ActivityContext activity,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(clock);
        _before = before;
        Source = source;
        Activity = activity;
        Clock = clock;
        State = before.State;
    }

    internal EndpointAddress Source { get; }

    internal ActivityContext Activity { get; }

    internal TimeProvider Clock { get; }

    internal TState State { get; private set; }

    internal void SetState(TState state) => State = state;

    internal EmissionOutcome StageEmission(
        ContractId eventContract,
        ImmutableArray<DeliverySnapshot> deliveries,
        FiringId? causeFiring = null)
    {
        var firing = FiringId.New();
        var eventId = EventId.New();
        var timestamp = Clock.GetUtcNow();
        _journal.Add(new JournalEntry(
            firing,
            eventId,
            eventContract,
            Activity,
            causeFiring,
            Source,
            timestamp));
        _emissions.Add(new OutboxEntry(
            firing,
            eventId,
            eventContract,
            Activity,
            causeFiring,
            Source,
            timestamp,
            deliveries));
        return new EmissionOutcome(deliveries.Length);
    }

    internal void StageDirectedMessage(EndpointAddress target, ContractId contract)
    {
        ArgumentNullException.ThrowIfNull(target);
        _directedMessages.Add(new DirectedMessage(
            DirectedMessageId.New(),
            FiringId.New(),
            Activity,
            Source,
            target,
            contract,
            Clock.GetUtcNow()));
    }

    internal NeuronTurnCommit<TState> Commit()
        => new(
            State,
            [.. _journal],
            [.. _emissions],
            [.. _directedMessages]);

    internal NeuronStateSnapshot<TState> Before => _before;
}
