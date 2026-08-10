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

        if (string.IsNullOrWhiteSpace(synapse.SynapseAlias))
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' refuses a connection without a synapse alias.");
        }

        RequireWorkingTransform(synapse);
        SweepExpired();
        Remove(synapse.ConnectionId);
        _connections.Add(_records.SerializeToArray(new SynapseConnection(
            synapse.ConnectionId,
            synapse.Source,
            synapse.SynapseAlias,
            synapse.Target,
            synapse.Transform,
            synapse.ExpiresAt)));

        return ReplyAsync(
            new Connected(synapse.ConnectionId, synapse.Source, synapse.SynapseAlias, synapse.Target),
            cancellationToken);
    }

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
}
