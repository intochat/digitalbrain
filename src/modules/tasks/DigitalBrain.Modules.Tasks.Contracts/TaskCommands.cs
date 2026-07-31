using System.ComponentModel;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("tasks.policy")]
public sealed record TaskPolicy(
    [property: Id(0)] int MaximumAttempts,
    [property: Id(1)] TimeSpan RetryDelay,
    [property: Id(2)] DateTimeOffset? Deadline);

[GenerateSerializer]
[Alias("tasks.behavior-activation")]
public sealed record BehaviorTaskActivation
{
    public BehaviorTaskActivation(
        BehaviorId behaviorId,
        BehaviorRevisionId revision,
        string contractVersion,
        string caseId,
        ProtectedPayloadReference protectedPayload,
        string triggerTypeName,
        IReadOnlyList<TaskOperationEdge> capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(triggerTypeName);
        ArgumentNullException.ThrowIfNull(capabilities);

        BehaviorId = behaviorId;
        Revision = revision;
        ContractVersion = contractVersion;
        CaseId = caseId;
        ProtectedPayload = protectedPayload;
        TriggerTypeName = triggerTypeName;
        Capabilities = [.. capabilities];
    }

    [Id(0)]
    public BehaviorId BehaviorId { get; init; }

    [Id(1)]
    public BehaviorRevisionId Revision { get; init; }

    [Id(2)]
    public string ContractVersion { get; init; }

    [Id(3)]
    public string CaseId { get; init; }

    [Id(4)]
    public ProtectedPayloadReference ProtectedPayload { get; init; }

    [Id(5)]
    public string TriggerTypeName { get; init; }

    [Id(6)]
    public IReadOnlyList<TaskOperationEdge> Capabilities { get; init; }

    public bool Equals(BehaviorTaskActivation? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return BehaviorId == other.BehaviorId
            && Revision == other.Revision
            && string.Equals(ContractVersion, other.ContractVersion, StringComparison.Ordinal)
            && string.Equals(CaseId, other.CaseId, StringComparison.Ordinal)
            && ProtectedPayload == other.ProtectedPayload
            && string.Equals(TriggerTypeName, other.TriggerTypeName, StringComparison.Ordinal)
            && CapabilitiesEqual(Capabilities, other.Capabilities);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BehaviorId);
        hash.Add(Revision);
        hash.Add(ContractVersion, StringComparer.Ordinal);
        hash.Add(CaseId, StringComparer.Ordinal);
        hash.Add(ProtectedPayload);
        hash.Add(TriggerTypeName, StringComparer.Ordinal);
        foreach (var edge in Capabilities)
        {
            hash.Add(edge);
        }

        return hash.ToHashCode();
    }

    private static bool CapabilitiesEqual(
        IReadOnlyList<TaskOperationEdge> left,
        IReadOnlyList<TaskOperationEdge> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }
}

[GenerateSerializer]
[Alias("tasks.start")]
[Description("Start a durable owner-scoped task")]
public sealed record StartTask(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] Goal Goal,
    [property: Id(2)] NeuronId Worker,
    [property: Id(3)] TaskPolicy Policy,
    [property: Id(4)] NeuronId? RetryOf = null,
    [property: Id(5)] BehaviorTaskActivation? Activation = null) : RequestSynapse<TaskSnapshot>;

[GenerateSerializer]
[Alias("tasks.cancel")]
public sealed record CancelTask(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long ExpectedRevision);
