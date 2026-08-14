using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Brain.Abstractions.Context;
using Brain.Abstractions.Contracts;
using Brain.Abstractions.Graph;
using Brain.Abstractions.Identity;
using Brain.Abstractions.Modules;
using Brain.Abstractions.Policy;
using Brain.Core.Endpoints;
using Brain.Core.Modules;
using Brain.Core.Outbox;

namespace Brain.Core.Graph;

internal sealed record SynapseDefinition(
    SynapseKey Key,
    EndpointAddress Source,
    ContractId Contract,
    EndpointAddress Target,
    ReshapeId? Reshape,
    string Scope,
    WiringSlotId WiringSlot,
    ActivityContext Provenance,
    int Revision);

internal sealed record SynapseRevision(
    SynapseDefinition Definition,
    ContractId OutputContract,
    SynapseRevisionStatus Status,
    GraphReason? Reason)
{
    internal SynapseKey Key => Definition.Key;
    internal EndpointAddress Source => Definition.Source;
    internal ContractId Contract => Definition.Contract;
    internal EndpointAddress Target => Definition.Target;
    internal string Scope => Definition.Scope;
    internal WiringSlotId WiringSlot => Definition.WiringSlot;
    internal int Revision => Definition.Revision;
}

internal sealed record GraphDeliverySnapshot(
    SynapseKey SynapseKey,
    int SynapseRevision,
    EndpointAddress Target,
    ContractId InputContract,
    ContractId OutputContract,
    ReshapeId? Reshape);

internal sealed class GraphResolution(IEnumerable<GraphDeliverySnapshot> deliveries)
{
    internal IImmutableList<GraphDeliverySnapshot> Deliveries { get; } = deliveries
        ?.ToImmutableList()
        ?? throw new ArgumentNullException(nameof(deliveries));
}

// This proof models the authoritative shard storage in-process. Shard identity is
// still derived solely from Source, and no cache or global query surface exists.
internal sealed class BrainGraphShardGrain
{
    private readonly BrainGraphShardState _state = new();
    private readonly SynapseRevisionValidator _validator;
    private readonly GraphShardResolver _shards;

    internal BrainGraphShardGrain(ModuleSet modules, IWorkspacePolicyEvaluator policy, GraphShardResolver shards)
    {
        _validator = new SynapseRevisionValidator(modules, policy);
        _shards = shards ?? throw new ArgumentNullException(nameof(shards));
    }

    internal Task<SynapseRevision> InstallAsync(SynapseChangeRequest request)
    {
        _validator.ValidateInstallOrReplace(request, GraphChangeKind.Install);
        var shard = _shards.Resolve(request.Source);
        var route = new StableRoute(request.Source, request.Contract, request.Scope, request.WiringSlot);
        if (_state.TryGetKey(route, out var existingKey))
        {
            var history = _state.History(existingKey);
            if (history[^1].Status == SynapseRevisionStatus.Live)
            {
                throw new GraphValidationException("A live synapse already occupies the stable route.");
            }

            return Task.FromResult(Append(existingKey, request, history[^1].Revision + 1, SynapseRevisionStatus.Live, null, shard));
        }

        return Task.FromResult(Append(SynapseKey.New(), request, 1, SynapseRevisionStatus.Live, null, shard));
    }

    internal Task<SynapseRevision> ReplaceAsync(SynapseKey key, SynapseChangeRequest request)
    {
        var history = _state.History(key);
        var current = history[^1];
        _validator.ValidateInstallOrReplace(request, GraphChangeKind.Replace);
        if (current.Status != SynapseRevisionStatus.Live)
        {
            throw new GraphValidationException("A retired synapse must be reinstalled, not replaced.");
        }

        if (current.Source != request.Source || current.Contract != request.Contract
            || !string.Equals(current.Scope, request.Scope, StringComparison.Ordinal)
            || current.WiringSlot != request.WiringSlot)
        {
            throw new GraphValidationException("Replace cannot alter the stable synapse route dimensions.");
        }

        var shard = _shards.Resolve(request.Source);
        return Task.FromResult(Append(key, request, current.Revision + 1, SynapseRevisionStatus.Live, null, shard));
    }

    internal Task RetireAsync(SynapseKey key, GraphReason reason, ActivityContext provenance)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        var current = _state.History(key)[^1];
        _validator.ValidateRetire(current, provenance);
        if (current.Status != SynapseRevisionStatus.Live)
        {
            throw new GraphValidationException("Only a live synapse can be retired.");
        }

        var definition = current.Definition with { Provenance = provenance, Revision = current.Revision + 1 };
        _state.Add(
            new SynapseRevision(definition, current.OutputContract, SynapseRevisionStatus.Retired, reason),
            _shards.Resolve(current.Source));
        return Task.CompletedTask;
    }

    internal Task<GraphResolution> ResolveAsync(EndpointAddress source, ContractId contract)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(contract.Value))
        {
            throw new ArgumentException("An event contract is required.", nameof(contract));
        }

        _ = _shards.Resolve(source);
        var deliveries = _state.LatestFor(source, contract)
            .Select(static revision => new GraphDeliverySnapshot(
                revision.Key,
                revision.Revision,
                revision.Target,
                revision.Contract,
                revision.OutputContract,
                revision.Definition.Reshape))
            .ToImmutableList();
        return Task.FromResult(new GraphResolution(deliveries));
    }

    internal Task<IImmutableList<SynapseRevision>> HistoryAsync(SynapseKey key)
        => Task.FromResult<IImmutableList<SynapseRevision>>(_state.History(key));

    internal Task<int> RevisionCountAsync() => Task.FromResult(_state.RevisionCount);

    private SynapseRevision Append(
        SynapseKey key,
        SynapseChangeRequest request,
        int revision,
        SynapseRevisionStatus status,
        GraphReason? reason,
        string shard)
    {
        ReshapeId? reshapeId = request.Reshape is null ? null : ToReshapeId(request.Reshape);
        var definition = new SynapseDefinition(
            key,
            request.Source,
            request.Contract,
            request.Target,
            reshapeId,
            request.Scope,
            request.WiringSlot,
            request.Provenance,
            revision);
        var outputContract = request.Reshape?.OutputEvent ?? request.Contract;
        var appended = new SynapseRevision(definition, outputContract, status, reason);
        _state.Add(appended, shard);
        return appended;
    }

    private static ReshapeId ToReshapeId(ReshapeDescriptor reshape)
    {
        var material = $"{reshape.Owner.Value}|{reshape.InputEvent.Value}|{reshape.OutputEvent.Value}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var value = new Guid(bytes.AsSpan(0, 16));
        return new ReshapeId(value == Guid.Empty ? new Guid(bytes.AsSpan(16, 16)) : value);
    }
}
