using DigitalBrain.Abstractions;

namespace DigitalBrain.Behaviors;

internal sealed partial class BehaviorNeuron
{
    private BehaviorData LoadOrEmpty()
        => _state.Value is { Length: > 0 } serialized
            ? _states.Deserialize(serialized)
            : BehaviorData.Empty;

    private async Task SaveAsync(BehaviorData data)
    {
        var previous = _state.Value is { Length: > 0 } serialized
            ? serialized.ToArray()
            : [];
        _state.Value = _states.SerializeToArray(data);
        try
        {
            await WriteStateAsync();
        }
        catch
        {
            _state.Value = previous;
            throw;
        }
    }

    private BehaviorSnapshot Snapshot(BehaviorData data)
        => new(
            BehaviorIdOfName(),
            data.Status,
            data.ProposedArtifactHash,
            data.ActiveArtifactHash,
            data.PriorArtifactHash,
            data.LastCompileFailure,
            data.TestsPassed,
            data.IsApproved,
            data.LastExecutionOutcome);

    private BehaviorId BehaviorIdOfName() => new(Id.Name);

    private static void ValidateCommand(CommandId commandId)
    {
        if (commandId.Value == Guid.Empty)
        {
            throw new ArgumentException("A behavior command id cannot be empty.", nameof(commandId));
        }
    }

    private static bool TryReceipt(BehaviorData data, CommandId commandId, out BehaviorSnapshot snapshot)
    {
        if (data.Receipts.TryGetValue(commandId.Value, out var receipt))
        {
            snapshot = receipt;
            return true;
        }

        snapshot = default!;
        return false;
    }

    private static BehaviorData WithReceipt(BehaviorData data, CommandId commandId, BehaviorSnapshot snapshot)
    {
        var receipts = new Dictionary<Guid, BehaviorSnapshot>(data.Receipts)
        {
            [commandId.Value] = snapshot,
        };
        while (receipts.Count > 64)
        {
            receipts.Remove(receipts.Keys.First());
        }

        return data with { Receipts = receipts };
    }

    private async Task<SynapseDelivery> ApprovalEvidenceAsync(BehaviorRevisionApproval approval)
    {
        var incoming = await ReadJournal(JournalKind.Incoming, afterSequence: 0);
        return incoming.Delta.FirstOrDefault(delivery =>
                delivery.Caller == approval.Approver
                && delivery.Synapse is BehaviorRevisionApproval recorded
                && recorded == approval)
            ?? throw new InvalidOperationException(
                $"Behavior approval '{approval.ApprovalId}' has no durable human delivery evidence.");
    }

    private void ValidateApprovalEvidence(BehaviorRevisionApproval approval, SynapseDelivery evidence)
    {
        if (approval.ApprovalId == Guid.Empty || evidence.SynapseId == default)
        {
            throw new ArgumentException("Durable approval identity and evidence are required.", nameof(approval));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(approval.Fingerprint);

        if (approval.Approver.Owner != Id.Owner
            || approval.Approver.Type != ISessionNeuron.GrainTypeName
            || approval.ApprovedAt == default)
        {
            throw new NeuronAuthorizationException(
                "Behavior revision approval must be issued by this owner's human session.");
        }

        if (evidence.Caller != approval.Approver
            || evidence.Synapse is not BehaviorRevisionApproval recorded
            || recorded != approval)
        {
            throw new NeuronAuthorizationException(
                $"Behavior revision '{approval.Fingerprint}' has no exact durable human approval evidence.");
        }
    }

    [GenerateSerializer]
    internal sealed record BehaviorData
    {
        public static BehaviorData Empty { get; } = new()
        {
            Status = BehaviorRevisionStatus.Empty,
            Receipts = new Dictionary<Guid, BehaviorSnapshot>(),
        };

        [Id(0)]
        public BehaviorRevisionStatus Status { get; init; }

        [Id(1)]
        public string? ProposedArtifactHash { get; init; }

        [Id(2)]
        public string? ActiveArtifactHash { get; init; }

        [Id(3)]
        public string? PriorArtifactHash { get; init; }

        [Id(4)]
        public string? LastCompileFailure { get; init; }

        [Id(5)]
        public bool TestsPassed { get; init; }

        [Id(6)]
        public bool IsApproved { get; init; }

        [Id(7)]
        public string? LastExecutionOutcome { get; init; }

        [Id(8)]
        public string? ProgramSource { get; init; }

        [Id(9)]
        public Dictionary<string, string>? Features { get; init; }

        [Id(10)]
        public string? DisplayName { get; init; }

        [Id(11)]
        public string? Description { get; init; }

        [Id(12)]
        public byte[]? ArtifactBytes { get; init; }

        [Id(13)]
        public byte[]? AssemblyBytes { get; init; }

        [Id(14)]
        public byte[]? ActiveArtifactBytes { get; init; }

        [Id(15)]
        public byte[]? ActiveAssemblyBytes { get; init; }

        [Id(16)]
        public string? ActiveProgramSource { get; init; }

        [Id(17)]
        public byte[]? PriorArtifactBytes { get; init; }

        [Id(18)]
        public byte[]? PriorAssemblyBytes { get; init; }

        [Id(19)]
        public string? PriorProgramSource { get; init; }

        [Id(20)]
        public BehaviorRevisionApproval? Approval { get; init; }

        [Id(21)]
        public SynapseId? ApprovalEvidence { get; init; }

        [Id(22)]
        public Dictionary<Guid, BehaviorSnapshot> Receipts { get; init; } = [];
    }
}
