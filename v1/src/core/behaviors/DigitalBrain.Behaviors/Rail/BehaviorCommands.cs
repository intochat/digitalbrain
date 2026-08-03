namespace DigitalBrain.Behaviors;

using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;

[GenerateSerializer]
[Alias("db.behavior.propose-revision")]
public sealed record ProposeBehaviorRevision(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ProgramSource,
    [property: Id(2)] IReadOnlyDictionary<string, string> Features,
    [property: Id(3)] string DisplayName,
    [property: Id(4)] string Description);

[GenerateSerializer]
[Alias("db.behavior.run-tests")]
public sealed record RunBehaviorTests(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ArtifactHash);

[GenerateSerializer]
[Alias("db.behavior.activate-revision")]
public sealed record ActivateBehaviorRevision(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ArtifactHash);

[GenerateSerializer]
[Alias("db.behavior.activate-bound")]
public sealed record ActivateBoundBehavior(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string ArtifactHash,
    [property: Id(2)] BehaviorActivationBinding Binding);

[GenerateSerializer]
[Alias("db.behavior.activation-goal")]
public sealed record BehaviorActivationGoal : Goal
{
    public BehaviorActivationGoal(
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

    // Carried on the goal because it is the only thing that survives Task, Worker, relay and the
    // HTTP hop into the host, where the broker turns it into the claim the silo then clamps.
    [Id(7)]
    public int HopsRemaining { get; init; } = BehaviorFactEmission.MaximumHops;

    public bool Equals(BehaviorActivationGoal? other)
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
            && HopsRemaining == other.HopsRemaining
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
        hash.Add(HopsRemaining);
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
[Alias("db.behavior.bound-activation-result")]
public sealed record BoundBehaviorActivationResult(
    [property: Id(0)] NeuronId TaskId,
    [property: Id(1)] TaskState State,
    [property: Id(2)] AttemptId? ActiveAttempt,
    [property: Id(3)] BehaviorTaskActivation? Activation);

[GenerateSerializer]
[Alias("db.behavior.rollback-revision")]
public sealed record RollbackBehaviorRevision(
    [property: Id(0)] CommandId CommandId);

[GenerateSerializer]
[Alias("db.behavior.execute-revision")]
public sealed record ExecuteBehaviorRevision(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string TriggerTypeName,
    [property: Id(2)] string TriggerJson);

[GenerateSerializer]
[Alias("db.behavior.stop")]
public sealed record StopBehavior(
    [property: Id(0)] CommandId CommandId);

[GenerateSerializer]
[Alias("db.behavior.start")]
public sealed record StartBehavior(
    [property: Id(0)] CommandId CommandId);

[GenerateSerializer]
[Alias("db.behavior.set-binding-enabled")]
public sealed record SetBehaviorBindingEnabled(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string BindingId,
    [property: Id(2)] bool Enabled);

[GenerateSerializer]
[Alias("db.behavior.emit-fact")]
public sealed record EmitBehaviorFact(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] string EmitAlias,
    [property: Id(2)] string PayloadJson)
{
    [Id(3)]
    public int HopsRemaining { get; init; } = BehaviorFactEmission.MaximumHops;
}
