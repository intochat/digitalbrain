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
    GraphReason? Reason,
    BrainActivityId? Activation = null)
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

// A grain handle is bound to exactly one source-owned state entry selected by the
// injected directory. It cannot inspect or mutate a different source shard.
internal sealed class BrainGraphShardGrain
{
    private readonly EndpointAddress _source;
    private readonly GraphShardEntry _entry;
    private readonly SynapseRevisionValidator _validator;
    private readonly GraphActivationRegistry _activations;

    internal BrainGraphShardGrain(
        EndpointAddress source,
        GraphShardEntry entry,
        SynapseRevisionValidator validator,
        GraphActivationRegistry activations)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _entry = entry ?? throw new ArgumentNullException(nameof(entry));
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _activations = activations ?? throw new ArgumentNullException(nameof(activations));
        if (_entry.Source != _source)
        {
            throw new ArgumentException("A graph grain must be bound to its entry source.", nameof(entry));
        }
    }

    internal Task<SynapseRevision> InstallAsync(SynapseChangeRequest request)
    {
        EnsureAssignedSource(request.Source);
        _validator.ValidateInstallOrReplace(request, GraphChangeKind.Install);
        lock (_entry.Gate)
        {
            var route = new StableRoute(request.Source, request.Contract, request.Scope, request.WiringSlot);
            if (_entry.State.TryGetKey(route, out var existingKey))
            {
                var history = _entry.State.History(existingKey);
                if (history[^1].Status == SynapseRevisionStatus.Live)
                {
                    throw new GraphValidationException("A live synapse already occupies the stable route.");
                }

                return Task.FromResult(Append(existingKey, request, history[^1].Revision + 1, SynapseRevisionStatus.Live, null));
            }

            return Task.FromResult(Append(SynapseKey.New(), request, 1, SynapseRevisionStatus.Live, null));
        }
    }

    internal Task<SynapseRevision> ReplaceAsync(SynapseKey key, SynapseChangeRequest request)
    {
        EnsureAssignedSource(request.Source);
        _validator.ValidateInstallOrReplace(request, GraphChangeKind.Replace);
        lock (_entry.Gate)
        {
            var current = _entry.State.History(key)[^1];
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

            return Task.FromResult(Append(key, request, current.Revision + 1, SynapseRevisionStatus.Live, null));
        }
    }

    internal Task<SynapseRevision> StageAsync(SynapseChangeRequest request, BrainActivityId activation)
    {
        EnsureAssignedSource(request.Source);
        if (activation.Value == Guid.Empty)
        {
            throw new ArgumentException("A staged graph revision requires an activation.", nameof(activation));
        }

        _validator.ValidateInstallOrReplace(request, GraphChangeKind.Install);
        lock (_entry.Gate)
        {
            var route = new StableRoute(request.Source, request.Contract, request.Scope, request.WiringSlot);
            if (_entry.State.TryGetKey(route, out var existingKey))
            {
                var history = _entry.State.History(existingKey);
                var current = history[^1];
                if (current.Status == SynapseRevisionStatus.Staged && current.Activation == activation)
                {
                    return Task.FromResult(current);
                }

                return Task.FromResult(Append(existingKey, request, current.Revision + 1, SynapseRevisionStatus.Staged, null, activation));
            }

            return Task.FromResult(Append(SynapseKey.New(), request, 1, SynapseRevisionStatus.Staged, null, activation));
        }
    }

    internal Task RetireAsync(SynapseKey key, GraphReason reason, ActivityContext provenance)
    {
        if (!Enum.IsDefined(reason))
        {
            throw new ArgumentOutOfRangeException(nameof(reason));
        }

        lock (_entry.Gate)
        {
            var current = _entry.State.History(key)[^1];
            _validator.ValidateRetire(current, provenance);
            if (current.Status != SynapseRevisionStatus.Live)
            {
                throw new GraphValidationException("Only a live synapse can be retired.");
            }

            var definition = current.Definition with { Provenance = provenance, Revision = current.Revision + 1 };
            _entry.State.Add(new SynapseRevision(definition, current.OutputContract, SynapseRevisionStatus.Retired, reason));
        }
        return Task.CompletedTask;
    }

    internal Task<GraphResolution> ResolveAsync(EndpointAddress source, ContractId contract)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(contract.Value))
        {
            throw new ArgumentException("An event contract is required.", nameof(contract));
        }

        EnsureAssignedSource(source);
        lock (_entry.Gate)
        {
            var deliveries = _entry.State.LatestFor(source, contract, _activations.IsActive)
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
    }

    internal Task<IImmutableList<SynapseRevision>> HistoryAsync(SynapseKey key)
    {
        lock (_entry.Gate)
        {
            return Task.FromResult<IImmutableList<SynapseRevision>>(_entry.State.History(key));
        }
    }

    internal Task<int> RevisionCountAsync()
    {
        lock (_entry.Gate)
        {
            return Task.FromResult(_entry.State.RevisionCount);
        }
    }

    private SynapseRevision Append(
        SynapseKey key,
        SynapseChangeRequest request,
        int revision,
        SynapseRevisionStatus status,
        GraphReason? reason,
        BrainActivityId? activation = null)
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
        var appended = new SynapseRevision(definition, outputContract, status, reason, activation);
        _entry.State.Add(appended);
        return appended;
    }

    private void EnsureAssignedSource(EndpointAddress source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source != _source)
        {
            throw new GraphValidationException("This graph shard only accepts its assigned outbound source endpoint.");
        }
    }

    private static ReshapeId ToReshapeId(ReshapeDescriptor reshape)
    {
        var material = $"{reshape.Owner.Value}|{reshape.InputEvent.Value}|{reshape.OutputEvent.Value}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var value = new Guid(bytes.AsSpan(0, 16));
        return new ReshapeId(value == Guid.Empty ? new Guid(bytes.AsSpan(16, 16)) : value);
    }
}
