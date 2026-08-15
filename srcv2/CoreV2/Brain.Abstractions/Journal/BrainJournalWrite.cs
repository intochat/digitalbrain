using Orleans.Concurrency;

namespace Brain.Abstractions.Journal;

[GenerateSerializer, Immutable]
public sealed record BrainJournalWrite
{
    public BrainJournalWrite(
        Guid recordId,
        string workspaceId,
        Guid activityId,
        string principalId,
        string neuronId,
        BrainJournalDirection direction,
        string contractId,
        Guid firingId,
        Guid? causeFiringId,
        Guid? synapseId,
        long? synapseRevision,
        DateTimeOffset occurredAt,
        int routeCount,
        string outcome,
        string summary)
    {
        _ = new BrainJournalRecord(
            1,
            recordId,
            workspaceId,
            activityId,
            principalId,
            neuronId,
            direction,
            contractId,
            firingId,
            causeFiringId,
            synapseId,
            synapseRevision,
            occurredAt,
            routeCount,
            outcome,
            summary);

        RecordId = recordId;
        WorkspaceId = workspaceId;
        ActivityId = activityId;
        PrincipalId = principalId;
        NeuronId = neuronId;
        Direction = direction;
        ContractId = contractId;
        FiringId = firingId;
        CauseFiringId = causeFiringId;
        SynapseId = synapseId;
        SynapseRevision = synapseRevision;
        OccurredAt = occurredAt;
        RouteCount = routeCount;
        Outcome = outcome;
        Summary = summary;
    }

    [Id(0)] public Guid RecordId { get; }
    [Id(1)] public string WorkspaceId { get; }
    [Id(2)] public Guid ActivityId { get; }
    [Id(3)] public string PrincipalId { get; }
    [Id(4)] public string NeuronId { get; }
    [Id(5)] public BrainJournalDirection Direction { get; }
    [Id(6)] public string ContractId { get; }
    [Id(7)] public Guid FiringId { get; }
    [Id(8)] public Guid? CauseFiringId { get; }
    [Id(9)] public Guid? SynapseId { get; }
    [Id(10)] public long? SynapseRevision { get; }
    [Id(11)] public DateTimeOffset OccurredAt { get; }
    [Id(12)] public int RouteCount { get; }
    [Id(13)] public string Outcome { get; }
    [Id(14)] public string Summary { get; }

    public BrainJournalRecord WithSequence(long sequence)
        => new(
            sequence,
            RecordId,
            WorkspaceId,
            ActivityId,
            PrincipalId,
            NeuronId,
            Direction,
            ContractId,
            FiringId,
            CauseFiringId,
            SynapseId,
            SynapseRevision,
            OccurredAt,
            RouteCount,
            Outcome,
            Summary);
}
