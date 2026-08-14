using System.Collections.Immutable;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Identity;
using Brain.Core.Endpoints;

namespace Brain.Core.Outbox;

internal readonly record struct FiringId
{
    public FiringId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A firing id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static FiringId New() => new(Guid.NewGuid());
}

internal readonly record struct EventId
{
    public EventId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An event id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static EventId New() => new(Guid.NewGuid());
}

internal readonly record struct DeliveryId
{
    public DeliveryId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A delivery id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static DeliveryId New() => new(Guid.NewGuid());
}

internal readonly record struct DirectedMessageId
{
    public DirectedMessageId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A directed message id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }

    public static DirectedMessageId New() => new(Guid.NewGuid());
}

internal readonly record struct ReshapeId
{
    public ReshapeId(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A reshape id cannot be empty.", nameof(value));
        }

        Value = value;
    }

    public Guid Value { get; }
}

internal readonly record struct DeliverySnapshot
{
    public DeliverySnapshot(
        DeliveryId delivery,
        EndpointAddress target,
        SynapseKey synapse,
        long synapseRevision,
        ContractId inputContract,
        ContractId outputContract,
        ReshapeId? reshape)
    {
        RuntimeRecordValidation.Delivery(
            delivery,
            target,
            synapse,
            synapseRevision,
            inputContract,
            outputContract,
            reshape);
        Delivery = delivery;
        Target = target;
        Synapse = synapse;
        SynapseRevision = synapseRevision;
        InputContract = inputContract;
        OutputContract = outputContract;
        Reshape = reshape;
    }

    public DeliveryId Delivery { get; }

    public EndpointAddress Target { get; }

    public SynapseKey Synapse { get; }

    public long SynapseRevision { get; }

    public ContractId InputContract { get; }

    public ContractId OutputContract { get; }

    public ReshapeId? Reshape { get; }

    internal void EnsureValid()
        => RuntimeRecordValidation.Delivery(
            Delivery,
            Target,
            Synapse,
            SynapseRevision,
            InputContract,
            OutputContract,
            Reshape);
}

internal sealed record JournalEntry(
    FiringId Firing,
    EventId EventId,
    ContractId EventContract,
    ActivityContext Activity,
    FiringId? CauseFiring,
    EndpointAddress Source,
    DateTimeOffset OccurredAt);

internal sealed class OutboxEntry
{
    public OutboxEntry(
        FiringId firing,
        EventId eventId,
        ContractId eventContract,
        ActivityContext activity,
        FiringId? causeFiring,
        EndpointAddress source,
        DateTimeOffset stagedAt,
        ImmutableArray<DeliverySnapshot> deliveries)
    {
        ArgumentNullException.ThrowIfNull(activity);
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(eventContract.Value))
        {
            throw new ArgumentException("An outbox entry requires an event contract.", nameof(eventContract));
        }

        Firing = firing;
        EventId = eventId;
        EventContract = eventContract;
        Activity = activity;
        CauseFiring = causeFiring;
        Source = source;
        StagedAt = stagedAt;
        Deliveries = deliveries.IsDefault ? ImmutableArray<DeliverySnapshot>.Empty : deliveries;
    }

    public FiringId Firing { get; }

    public EventId EventId { get; }

    public ContractId EventContract { get; }

    public ActivityContext Activity { get; }

    public FiringId? CauseFiring { get; }

    public EndpointAddress Source { get; }

    public DateTimeOffset StagedAt { get; }

    public ImmutableArray<DeliverySnapshot> Deliveries { get; }
}

internal sealed record DirectedMessage(
    DirectedMessageId Id,
    FiringId Firing,
    ActivityContext Activity,
    EndpointAddress Source,
    EndpointAddress Target,
    ContractId Contract,
    DateTimeOffset StagedAt);

internal readonly record struct EmissionOutcome
{
    public EmissionOutcome(int deliveryCount)
    {
        if (deliveryCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deliveryCount));
        }

        DeliveryCount = deliveryCount;
    }

    public int DeliveryCount { get; }
}

internal static class RuntimeRecordValidation
{
    internal static void Endpoint(EndpointAddress endpoint, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(endpoint, parameterName);
        if (endpoint.Workspace.IsEmpty
            || string.IsNullOrWhiteSpace(endpoint.Module.Value)
            || string.IsNullOrWhiteSpace(endpoint.Role.Value)
            || string.IsNullOrWhiteSpace(endpoint.ScopeToken))
        {
            throw new ArgumentException("An endpoint must carry workspace, module, role, and scope metadata.", parameterName);
        }
    }

    internal static void Contract(ContractId contract, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(contract.Value))
        {
            throw new ArgumentException("A contract id is required.", parameterName);
        }
    }

    internal static void Synapse(SynapseKey synapse, string parameterName)
    {
        if (synapse.Value == Guid.Empty)
        {
            throw new ArgumentException("A synapse key is required.", parameterName);
        }
    }

    internal static void Revision(long revision, string parameterName)
    {
        if (revision <= 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "A synapse revision must be positive.");
        }
    }

    internal static void Reshape(ReshapeId? reshape, string parameterName)
    {
        if (reshape is { } supplied && supplied.Value == Guid.Empty)
        {
            throw new ArgumentException("A reshape id cannot be empty when supplied.", parameterName);
        }
    }

    internal static void Delivery(
        DeliveryId delivery,
        EndpointAddress target,
        SynapseKey synapse,
        long revision,
        ContractId inputContract,
        ContractId outputContract,
        ReshapeId? reshape)
    {
        if (delivery.Value == Guid.Empty)
        {
            throw new ArgumentException("A delivery id is required.", nameof(delivery));
        }

        Endpoint(target, nameof(target));
        Synapse(synapse, nameof(synapse));
        Revision(revision, nameof(revision));
        Contract(inputContract, nameof(inputContract));
        Contract(outputContract, nameof(outputContract));
        Reshape(reshape, nameof(reshape));
    }
}
