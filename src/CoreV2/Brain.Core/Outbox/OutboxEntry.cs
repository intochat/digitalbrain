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

internal readonly record struct DeliverySnapshot(
    DeliveryId Delivery,
    EndpointAddress Target,
    SynapseKey Synapse,
    long SynapseRevision,
    ContractId InputContract,
    ContractId OutputContract,
    ReshapeId? Reshape);

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
