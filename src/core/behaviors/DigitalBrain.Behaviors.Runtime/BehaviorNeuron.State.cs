using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors.Artifacts;
using DigitalBrain.Behaviors.Manifest;

namespace DigitalBrain.Behaviors.Runtime;

internal sealed partial class BehaviorNeuron
{
    private BehaviorData LoadOrEmpty()
        => _state.Value is { Length: > 0 } serialized
            ? RepairActiveRehydrate(_states.Deserialize(serialized))
            : BehaviorData.Empty;

    internal static BehaviorData RepairActiveRehydrate(BehaviorData data)
    {
        if (data.Status == BehaviorRevisionStatus.Active
            && data.RunState == BehaviorRunState.Idle
            && data.ActiveArtifactHash is not null)
        {
            return data with
            {
                RunState = BehaviorRunState.Running,
                ActivationGateOpen = true,
            };
        }

        return data;
    }

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
    {
        var featureName = data.Features?
            .Keys
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        var featureText = data.Features is null || data.Features.Count == 0
            ? null
            : FeatureSourceOf(data.Features);
        var displayName = data.DisplayName;
        var scenarios = featureText is null
            ? Array.Empty<BehaviorScenarioSnapshot>()
            : BehaviorScenarioBinder.DeriveScenarios(featureText)
                .Select(static scenario => new BehaviorScenarioSnapshot(
                    scenario.ScenarioId,
                    scenario.Title,
                    scenario.BindingKey,
                    Passed: null,
                    Detail: null))
                .ToArray();
        var overview = displayName is null
            ? null
            : BehaviorScenarioBinder.ProjectOverview(
                displayName,
                featureText is null
                    ? []
                    : BehaviorScenarioBinder.DeriveScenarios(featureText));
        var signature = data.ActiveArtifactSignature ?? data.ArtifactSignature;
        var signatureHex = signature is null
            ? null
            : Convert.ToHexString(signature);
        var bindings = (data.RegisteredBindings ?? [])
            .Select(static binding => new BehaviorBindingSnapshot(
                binding.BindingId,
                binding.SourceModule,
                binding.SourceSynapse,
                binding.TargetCase,
                binding.ContractVersion,
                binding.Enabled,
                binding.ConfigurationHint))
            .ToArray();

        return new(
            BehaviorIdOfName(),
            data.Status,
            data.ProposedArtifactHash,
            data.ActiveArtifactHash,
            data.PriorArtifactHash,
            data.LastCompileFailure,
            data.TestsPassed,
            data.IsApproved,
            data.LastExecutionOutcome,
            data.RunState,
            data.ActivationGateOpen,
            displayName,
            data.Description,
            data.ProgramSource,
            featureName,
            featureText,
            overview,
            signatureHex,
            data.ActiveTaskIds.Count,
            bindings,
            scenarios);
    }

    private BehaviorId BehaviorIdOfName() => new(Id.Name);

    private static string FeatureSourceOf(IReadOnlyDictionary<string, string> features)
        => string.Join(
            "\n",
            features
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal)
                .Select(static pair => pair.Value));

    internal static BehaviorArtifactEnvelope CreateProposalEnvelope(
        BehaviorId behaviorId,
        string displayName,
        string description,
        string programSource,
        string featureSource,
        ReadOnlyMemory<byte> assemblyBytes,
        string compilerEvidenceJson,
        BehaviorContractManifest contract,
        IReadOnlyList<BehaviorCapabilityGrant> capabilityGrants,
        IReadOnlyList<string> eventAliases,
        IReadOnlyList<string>? broadcastEmitAliases = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(capabilityGrants);
        ArgumentNullException.ThrowIfNull(eventAliases);
        if (contract.Cases.Count == 0
            || string.IsNullOrWhiteSpace(contract.OneOfSchemaJson)
            || contract.OneOfSchemaJson.Contains("\"oneOf\":[]", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "A behavior proposal requires the compiler's non-empty input contract.");
        }

        var scenarios = BehaviorScenarioBinder.DeriveScenarios(featureSource);
        var overview = BehaviorScenarioBinder.ProjectOverview(displayName, scenarios);

        return new(
            new BehaviorDefinitionManifest(
                behaviorId,
                displayName,
                description,
                new BehaviorEntryPoints(
                    eventAliases,
                    contract)
                {
                    BroadcastEmitAliases = broadcastEmitAliases,
                },
                scenarios,
                overview,
                BehaviorInputContractCompiler.DefaultPolicy,
                capabilityGrants,
                new BehaviorResourceLimits(1_000, 64 * 1024 * 1024, 30_000)),
            programSource,
            featureSource,
            """{"libraries":{},"version":1}""",
            assemblyBytes,
            """{"runtimeTarget":{"name":"net11.0"}}""",
            compilerEvidenceJson,
            """{"policy":"v1","result":"pending"}""",
            BehaviorScenarioBinder.EvidenceJson(false, scenarios.Count, "pending", scenarios));
    }

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

    private static BehaviorData WithEmitReceipt(BehaviorData data, CommandId commandId, string outcome)
    {
        var receipts = new Dictionary<Guid, string>(data.EmitReceipts)
        {
            [commandId.Value] = outcome,
        };
        while (receipts.Count > 64)
        {
            receipts.Remove(receipts.Keys.First());
        }

        return data with { EmitReceipts = receipts };
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
            RunState = BehaviorRunState.Idle,
            ActivationGateOpen = false,
            ActiveTaskIds = [],
            RegisteredBindings = [],
            Receipts = new Dictionary<Guid, BehaviorSnapshot>(),
            EmitReceipts = new Dictionary<Guid, string>(),
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

        [Id(23)]
        public byte[]? ArtifactSignature { get; init; }

        [Id(24)]
        public byte[]? ActiveArtifactSignature { get; init; }

        [Id(25)]
        public byte[]? PriorArtifactSignature { get; init; }

        [Id(26)]
        public BehaviorRunState RunState { get; init; }

        [Id(27)]
        public bool ActivationGateOpen { get; init; }

        [Id(28)]
        public List<NeuronId> ActiveTaskIds { get; init; } = [];

        [Id(29)]
        public List<BehaviorRegisteredBinding> RegisteredBindings { get; init; } = [];

        // Receipts carries snapshots and cannot hold an emit outcome, so emissions get a sibling.
        [Id(30)]
        public Dictionary<Guid, string> EmitReceipts { get; init; } = [];
    }

    [GenerateSerializer]
    internal sealed record BehaviorRegisteredBinding(
        [property: Id(0)] string BindingId,
        [property: Id(1)] string SourceModule,
        [property: Id(2)] string SourceSynapse,
        [property: Id(3)] string TargetCase,
        [property: Id(4)] string ContractVersion,
        [property: Id(5)] bool Enabled,
        [property: Id(6)] string ConfigurationHint);
}
