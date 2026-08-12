using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

[GrainType(ISynapseGraph.GrainTypeName)]
internal sealed class SynapseGraphNeuron : Neuron, ISynapseGraph
{
    private const string ConnectionLogName = "graph.connections";

    private readonly IDurableList<byte[]> _connections;
    private readonly Serializer<SynapseConnection> _records;

    public SynapseGraphNeuron()
    {
        _connections = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(ConnectionLogName);
        _records = ServiceProvider.GetRequiredService<Serializer<SynapseConnection>>();
    }

    public Task HandleAsync(Connect synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireConnectionId(synapse.ConnectionId);
        RequireConnectable(synapse.Source);
        RequireConnectable(synapse.Target);
        RequirePrincipalAllowsEndpoint(synapse.Source, "source");
        RequirePrincipalAllowsEndpoint(synapse.Target, "target");
        RequireEndpointsSharePrincipal(synapse.Source, synapse.Target);

        if (string.IsNullOrWhiteSpace(synapse.SynapseAlias))
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' refuses a connection without a synapse alias.");
        }

        var alias = synapse.SynapseAlias.Trim();
        RequireKnownEndpoint(synapse.Source, "source");
        RequireKnownEndpoint(synapse.Target, "target");
        RequireWorkingTransform(synapse with { SynapseAlias = alias });
        var deliveredAlias = DeliveredAlias(alias, synapse.Transform);
        RequireTargetHandlesAlias(synapse.Target, deliveredAlias);
        SweepExpired();
        RequireNoDuplicateRoute(synapse.ConnectionId, synapse.Source, alias, synapse.Target);
        Remove(synapse.ConnectionId);
        _connections.Add(_records.SerializeToArray(new SynapseConnection(
            synapse.ConnectionId,
            synapse.Source,
            alias,
            synapse.Target,
            synapse.Transform,
            synapse.ExpiresAt,
            StampedProvenance(synapse.Intent))));

        return ReplyAsync(
            new Connected(synapse.ConnectionId, synapse.Source, alias, synapse.Target),
            cancellationToken);
    }

    // Only the stated intent comes from the caller. Author, time and correlation are taken from
    // the delivery, so a wire can never claim an origin it did not have.
    private Provenance? StampedProvenance(string? statedIntent)
        => CurrentDeliveryCaller is not { } author || CurrentDeliveryCorrelation is not { } correlation
            ? null
            : new Provenance(
                author,
                TimeProvider.GetUtcNow(),
                string.IsNullOrWhiteSpace(statedIntent) ? string.Empty : statedIntent.Trim(),
                correlation);

    public Task HandleAsync(Disconnect synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireConnectionId(synapse.ConnectionId);
        SweepExpired();

        if (!Remove(synapse.ConnectionId))
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' has no connection '{synapse.ConnectionId}' to disconnect.");
        }

        return ReplyAsync(new Disconnected(synapse.ConnectionId), cancellationToken);
    }

    public Task<IReadOnlyCollection<SynapseConnection>> ConnectionsFrom(NeuronId source, string synapseAlias)
    {
        var now = TimeProvider.GetUtcNow();
        List<SynapseConnection> live = [];

        foreach (var stored in _connections)
        {
            var connection = _records.Deserialize(stored);

            if (connection.Source == source
                && string.Equals(connection.SynapseAlias, synapseAlias, StringComparison.Ordinal)
                && IsLive(connection, now))
            {
                live.Add(connection);
            }
        }

        return Task.FromResult<IReadOnlyCollection<SynapseConnection>>(live);
    }

    public Task<SynapseConnection?> ConnectionOf(Guid connectionId)
    {
        var now = TimeProvider.GetUtcNow();

        foreach (var stored in _connections)
        {
            var connection = _records.Deserialize(stored);

            if (connection.ConnectionId == connectionId && IsLive(connection, now))
            {
                return Task.FromResult<SynapseConnection?>(connection);
            }
        }

        return Task.FromResult<SynapseConnection?>(null);
    }

    public Task<IReadOnlyCollection<SynapseConnection>> Connections()
    {
        var now = TimeProvider.GetUtcNow();
        List<SynapseConnection> live = [];

        foreach (var stored in _connections)
        {
            var connection = _records.Deserialize(stored);

            if (IsLive(connection, now))
            {
                live.Add(connection);
            }
        }

        return Task.FromResult<IReadOnlyCollection<SynapseConnection>>(live);
    }

    private static bool IsLive(SynapseConnection connection, DateTimeOffset now)
        => connection.ExpiresAt is not { } expiry || expiry > now;

    private bool Remove(Guid connectionId)
    {
        var removed = false;

        for (var index = _connections.Count - 1; index >= 0; index--)
        {
            if (_records.Deserialize(_connections[index]).ConnectionId == connectionId)
            {
                _connections.RemoveAt(index);
                removed = true;
            }
        }

        return removed;
    }

    private void SweepExpired()
    {
        var now = TimeProvider.GetUtcNow();

        for (var index = _connections.Count - 1; index >= 0; index--)
        {
            if (!IsLive(_records.Deserialize(_connections[index]), now))
            {
                _connections.RemoveAt(index);
            }
        }
    }

    // A morph that names a field neither contract has would refuse every delivery
    // silently later; a wiring typo must fail loudly at wiring time instead.
    private void RequireWorkingTransform(Connect synapse)
    {
        if (synapse.Transform is not { } transformName
            || !transformName.StartsWith("to:", StringComparison.Ordinal))
        {
            return;
        }

        if (DeclarativeSynapseTransform.TryParse(transformName) is not { } morph)
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' cannot parse transform '{transformName}'. Write it as "
                + "to:<synapse-alias>{TargetField=SourceField,...} with a known target alias.");
        }

        var sourceType = SynapseTypeIndex.FindByAlias(synapse.SynapseAlias);

        foreach (var (target, source) in morph.Mappings)
        {
            if (morph.TargetType.GetProperty(
                    target,
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.IgnoreCase) is null)
            {
                throw new NeuronAuthorizationException(
                    $"Transform '{transformName}' writes '{target}', but "
                    + $"'{morph.TargetType.Name}' has no such field. It has: "
                    + $"{FieldList(morph.TargetType)}.");
            }

            if (sourceType is not null
                && sourceType.GetProperty(
                    source,
                    System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.IgnoreCase) is null)
            {
                throw new NeuronAuthorizationException(
                    $"Transform '{transformName}' reads '{source}', but "
                    + $"'{sourceType.Name}' ({synapse.SynapseAlias}) has no such field. "
                    + $"It has: {FieldList(sourceType)}.");
            }
        }
    }

    private static string FieldList(Type synapseType)
        => string.Join(
            ", ",
            synapseType.GetProperties().Select(static property => property.Name));



    private static void RequireEndpointsSharePrincipal(NeuronId source, NeuronId target)
    {
        if (!PrincipalPartition.TryParse(source.Name, out var sourcePrincipal, out _)
            || !PrincipalPartition.TryParse(target.Name, out var targetPrincipal, out _))
        {
            return;
        }

        if (sourcePrincipal != targetPrincipal)
        {
            throw new NeuronAuthorizationException(
                $"Graph refuses cross-principal Connect {source} → {target}. "
                + "Wire within one principal partition, or share via grants (Read/Watch).");
        }
    }

    // A18: on a principal-scoped graph, partitioned endpoints must be the same principal.
    // Cross-principal Connect refuses by default (sharing is grants Read/Watch, not edges).
    private void RequirePrincipalAllowsEndpoint(NeuronId subject, string side)
    {
        if (!PrincipalPartition.TryParse(Id.Name, out var graphPrincipal, out _))
        {
            return;
        }

        if (!PrincipalPartition.TryParse(subject.Name, out var endpointPrincipal, out _))
        {
            return;
        }

        if (endpointPrincipal != graphPrincipal)
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' refuses cross-principal Connect on {side} '{subject}'. "
                + "Wire within one principal partition, or share via grants (Read/Watch) — "
                + "not by connecting another principal's neurons.");
        }
    }

    private void RequireConnectionId(Guid connectionId)
    {
        if (connectionId == Guid.Empty)
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' refuses an empty connection identity.");
        }
    }

    private void RequireConnectable(NeuronId subject)
    {
        if (subject.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' cannot connect '{subject}', which belongs to owner '{subject.Owner}'.");
        }

        if (subject == Id)
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' does not connect its own synapses.");
        }
    }

    private void RequireKnownEndpoint(NeuronId subject, string side)
    {
        // Cell is the interpreted tier — kinds live in the key, type is always "cell".
        if (string.Equals(subject.Type, ICell.GrainTypeName, StringComparison.OrdinalIgnoreCase))
        {
            if (subject.Name.IndexOf('@') <= 0 || subject.Name.EndsWith('@'))
            {
                throw new NeuronAuthorizationException(
                    $"Graph '{Id}' refuses {side} '{subject}': cell keys must be kind@instance.");
            }

            return;
        }

        var typeMap = ServiceProvider.GetService<ActiveModuleContractTypeMap>();
        if (typeMap is null)
        {
            return;
        }

        if (!typeMap.KnownGrainTypes.Contains(subject.Type.ToLowerInvariant())
            && !string.Equals(subject.Type, "surface-boot", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(subject.Type, IRegistry.GrainTypeName, StringComparison.OrdinalIgnoreCase))
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' refuses {side} '{subject}': grain type '{subject.Type}' "
                + "is not a known neuron type in this deployment.");
        }
    }

    // Alias the target actually receives after any morph (relay rewrites the payload).
    private static string DeliveredAlias(string sourceAlias, string? transform)
    {
        if (transform is { } name
            && name.StartsWith("to:", StringComparison.Ordinal)
            && DeclarativeSynapseTransform.TryParse(name) is { } morph
            && SynapseAlias.Of(morph.TargetType) is { } targetAlias)
        {
            return targetAlias;
        }

        return sourceAlias;
    }

    private void RequireTargetHandlesAlias(NeuronId target, string deliveredAlias)
    {
        // Cells accept kind-local and carrier facts; connect-time kind shape lands later.
        if (string.Equals(target.Type, ICell.GrainTypeName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var catalog = ServiceProvider.GetService<ActiveCapabilityCatalog>();
        var typeMap = ServiceProvider.GetService<ActiveModuleContractTypeMap>();
        if (catalog is null || typeMap is null)
        {
            return;
        }

        List<string> accepted = [];
        foreach (var module in catalog.Modules)
        {
            foreach (var neuron in module.Neurons)
            {
                if (!typeMap.TryGetNeuronGrainType(neuron.ContractId, out var grainType)
                    || grainType is null
                    || !string.Equals(grainType, target.Type, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var fact in neuron.Accepted)
                {
                    accepted.Add(fact.ContractId);
                    if (string.Equals(fact.ContractId, deliveredAlias, StringComparison.Ordinal))
                    {
                        return;
                    }
                }
            }
        }

        // surface-boot is kernel-only (no contracts assembly neuron id in the catalog).
        if (string.Equals(target.Type, "surface-boot", StringComparison.OrdinalIgnoreCase)
            && string.Equals(deliveredAlias, "db.digitalbrain-activated", StringComparison.Ordinal))
        {
            return;
        }

        var hint = accepted.Count == 0
            ? "it declares no accepted facts in the capability catalog"
            : "it accepts: " + string.Join(", ", accepted.Distinct(StringComparer.Ordinal).OrderBy(static a => a));

        throw new NeuronAuthorizationException(
            $"Graph '{Id}' refuses delivering '{deliveredAlias}' into '{target}': {hint}.");
    }

    private void RequireNoDuplicateRoute(
        Guid connectionId,
        NeuronId source,
        string alias,
        NeuronId target)
    {
        var now = TimeProvider.GetUtcNow();
        foreach (var stored in _connections)
        {
            var existing = _records.Deserialize(stored);
            if (!IsLive(existing, now))
            {
                continue;
            }

            if (existing.ConnectionId == connectionId)
            {
                continue;
            }

            if (existing.Source == source
                && existing.Target == target
                && string.Equals(existing.SynapseAlias, alias, StringComparison.Ordinal))
            {
                throw new NeuronAuthorizationException(
                    $"Graph '{Id}' already has connection '{existing.ConnectionId}' for "
                    + $"{source} --{alias}--> {target}. Disconnect it or reuse its ConnectionId.");
            }
        }
    }
}
