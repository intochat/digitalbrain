using System.Collections.Immutable;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Core.Endpoints;
using Brain.Core.Outbox;

namespace Brain.Core.Neurons;

internal sealed class GraphRoute
{
    public GraphRoute(
        EndpointAddress target,
        SynapseKey synapse,
        long revision,
        ContractId inputContract,
        ContractId outputContract,
        ReshapeId? reshape)
    {
        RuntimeRecordValidation.Endpoint(target, nameof(target));
        RuntimeRecordValidation.Synapse(synapse, nameof(synapse));
        RuntimeRecordValidation.Revision(revision, nameof(revision));
        RuntimeRecordValidation.Contract(inputContract, nameof(inputContract));
        RuntimeRecordValidation.Contract(outputContract, nameof(outputContract));
        RuntimeRecordValidation.Reshape(reshape, nameof(reshape));
        Target = target;
        Synapse = synapse;
        Revision = revision;
        InputContract = inputContract;
        OutputContract = outputContract;
        Reshape = reshape;
    }

    public EndpointAddress Target { get; set; }

    public SynapseKey Synapse { get; set; }

    public long Revision { get; set; }

    public ContractId InputContract { get; set; }

    public ContractId OutputContract { get; set; }

    public ReshapeId? Reshape { get; set; }

    internal DeliverySnapshot ToDeliverySnapshot(FiringId firing)
    {
        var target = ValidTarget();
        var synapse = ValidSynapse();
        var revision = ValidRevision();
        return new DeliverySnapshot(
            DeliveryId.Derive(firing, synapse, revision),
            target,
            synapse,
            revision,
            ValidInputContract(),
            ValidOutputContract(),
            ValidReshape());
    }

    private EndpointAddress ValidTarget()
    {
        RuntimeRecordValidation.Endpoint(Target, nameof(Target));
        return Target;
    }

    private SynapseKey ValidSynapse()
    {
        RuntimeRecordValidation.Synapse(Synapse, nameof(Synapse));
        return Synapse;
    }

    private long ValidRevision()
    {
        RuntimeRecordValidation.Revision(Revision, nameof(Revision));
        return Revision;
    }

    private ContractId ValidInputContract()
    {
        RuntimeRecordValidation.Contract(InputContract, nameof(InputContract));
        return InputContract;
    }

    private ContractId ValidOutputContract()
    {
        RuntimeRecordValidation.Contract(OutputContract, nameof(OutputContract));
        return OutputContract;
    }

    private ReshapeId? ValidReshape()
    {
        RuntimeRecordValidation.Reshape(Reshape, nameof(Reshape));
        return Reshape;
    }
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
        IReadOnlyList<GraphRoute> routes,
        FiringId? causeFiring = null)
    {
        RuntimeRecordValidation.Contract(eventContract, nameof(eventContract));
        ArgumentNullException.ThrowIfNull(routes);
        var firing = FiringId.New();
        var deliveries = routes.Select(route =>
        {
            ArgumentNullException.ThrowIfNull(route);
            return route.ToDeliverySnapshot(firing);
        }).ToImmutableArray();
        foreach (var delivery in deliveries)
        {
            delivery.EnsureValid();
        }

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
        RuntimeRecordValidation.Endpoint(target, nameof(target));
        RuntimeRecordValidation.Contract(contract, nameof(contract));
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
