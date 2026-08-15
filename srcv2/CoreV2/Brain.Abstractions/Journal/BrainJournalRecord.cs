using Orleans.Concurrency;

namespace Brain.Abstractions.Journal;

[GenerateSerializer, Immutable]
public sealed record BrainJournalRecord
{
    public BrainJournalRecord(
        long sequence,
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
        if (sequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sequence));
        }
        RequireGuid(recordId, nameof(recordId));
        Require(workspaceId, nameof(workspaceId));
        RequireGuid(activityId, nameof(activityId));
        Require(principalId, nameof(principalId));
        Require(neuronId, nameof(neuronId));
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }
        Require(contractId, nameof(contractId));
        RequireGuid(firingId, nameof(firingId));
        RequireOptionalGuid(causeFiringId, nameof(causeFiringId));
        RequireOptionalGuid(synapseId, nameof(synapseId));
        if (synapseId.HasValue != synapseRevision.HasValue)
        {
            throw new ArgumentException("Synapse identity and revision must be supplied together.", nameof(synapseRevision));
        }
        if (synapseRevision <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(synapseRevision));
        }
        if (routeCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(routeCount));
        }
        Require(outcome, nameof(outcome));
        Require(summary, nameof(summary));

        Sequence = sequence;
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

    [Id(0)] public long Sequence { get; }
    [Id(1)] public Guid RecordId { get; }
    [Id(2)] public string WorkspaceId { get; }
    [Id(3)] public Guid ActivityId { get; }
    [Id(4)] public string PrincipalId { get; }
    [Id(5)] public string NeuronId { get; }
    [Id(6)] public BrainJournalDirection Direction { get; }
    [Id(7)] public string ContractId { get; }
    [Id(8)] public Guid FiringId { get; }
    [Id(9)] public Guid? CauseFiringId { get; }
    [Id(10)] public Guid? SynapseId { get; }
    [Id(11)] public long? SynapseRevision { get; }
    [Id(12)] public DateTimeOffset OccurredAt { get; }
    [Id(13)] public int RouteCount { get; }
    [Id(14)] public string Outcome { get; }
    [Id(15)] public string Summary { get; }

    private static void Require(string value, string parameterName)
        => ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

    private static void RequireGuid(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("A non-empty identity is required.", parameterName);
        }
    }

    private static void RequireOptionalGuid(Guid? value, string parameterName)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("An optional identity cannot be empty.", parameterName);
        }
    }
}
