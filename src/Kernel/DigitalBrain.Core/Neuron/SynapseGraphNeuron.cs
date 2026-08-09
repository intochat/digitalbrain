using DigitalBrain.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

[GrainType(ISynapseGraph.GrainTypeName)]
internal sealed class SynapseGraphNeuron : Neuron, ISynapseGraph
{
    private const string BindingLogName = "graph.bindings";

    private readonly IDurableList<byte[]> _bindings;
    private readonly Serializer<SynapseBinding> _records;

    public SynapseGraphNeuron()
    {
        _bindings = ServiceProvider.GetRequiredKeyedService<IDurableList<byte[]>>(BindingLogName);
        _records = ServiceProvider.GetRequiredService<Serializer<SynapseBinding>>();
    }

    public Task HandleAsync(Bind synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        RequireRoutable(synapse.Source);
        RequireRoutable(synapse.Target);

        if (string.IsNullOrWhiteSpace(synapse.SynapseAlias))
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' refuses a route without a synapse alias.");
        }

        Remove(synapse.BindingId);
        _bindings.Add(_records.SerializeToArray(new SynapseBinding(
            synapse.BindingId,
            synapse.Source,
            synapse.SynapseAlias,
            synapse.Target,
            synapse.Transform,
            synapse.ExpiresAt)));

        return ReplyAsync(
            new Bound(synapse.BindingId, synapse.Source, synapse.SynapseAlias, synapse.Target),
            cancellationToken);
    }

    public Task HandleAsync(Unbind synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        Remove(synapse.BindingId);

        return ReplyAsync(new Unbound(synapse.BindingId), cancellationToken);
    }

    public Task<IReadOnlyCollection<SynapseRoute>> RoutesFor(NeuronId source, string synapseAlias)
    {
        var now = TimeProvider.GetUtcNow();
        List<SynapseRoute> routes = [];

        foreach (var stored in _bindings)
        {
            var binding = _records.Deserialize(stored);

            if (binding.Source == source
                && string.Equals(binding.SynapseAlias, synapseAlias, StringComparison.Ordinal)
                && IsLive(binding, now))
            {
                routes.Add(new SynapseRoute(binding.BindingId, binding.Target, binding.Transform));
            }
        }

        return Task.FromResult<IReadOnlyCollection<SynapseRoute>>(routes);
    }

    public Task<SynapseRoute?> RouteOf(Guid bindingId)
    {
        var now = TimeProvider.GetUtcNow();

        foreach (var stored in _bindings)
        {
            var binding = _records.Deserialize(stored);

            if (binding.BindingId == bindingId && IsLive(binding, now))
            {
                return Task.FromResult<SynapseRoute?>(
                    new SynapseRoute(binding.BindingId, binding.Target, binding.Transform));
            }
        }

        return Task.FromResult<SynapseRoute?>(null);
    }

    public Task<IReadOnlyCollection<SynapseBinding>> Bindings()
    {
        var now = TimeProvider.GetUtcNow();
        List<SynapseBinding> live = [];

        foreach (var stored in _bindings)
        {
            var binding = _records.Deserialize(stored);

            if (IsLive(binding, now))
            {
                live.Add(binding);
            }
        }

        return Task.FromResult<IReadOnlyCollection<SynapseBinding>>(live);
    }

    private static bool IsLive(SynapseBinding binding, DateTimeOffset now)
        => binding.ExpiresAt is not { } expiry || expiry > now;

    private void Remove(Guid bindingId)
    {
        for (var index = _bindings.Count - 1; index >= 0; index--)
        {
            if (_records.Deserialize(_bindings[index]).BindingId == bindingId)
            {
                _bindings.RemoveAt(index);
            }
        }
    }

    private void RequireRoutable(NeuronId subject)
    {
        if (subject.Owner != Id.Owner)
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' cannot route for '{subject}', which belongs to owner '{subject.Owner}'.");
        }

        if (subject == Id)
        {
            throw new NeuronAuthorizationException(
                $"Graph '{Id}' does not route its own synapses.");
        }
    }
}
