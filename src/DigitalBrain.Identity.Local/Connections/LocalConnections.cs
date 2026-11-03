using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Connections;
using DigitalBrain.Capabilities.Connections.Broker;
using DigitalBrain.Capabilities.Connections.Requests;

namespace DigitalBrain.Identity.Local.Connections;

/// <summary>Implements <see cref="ICapabilityConnections"/> over local connection neurons.</summary>
public sealed class LocalConnections(Func<Type, string, object> neuronResolver, ConnectionNeuronTypes? neuronTypes = null)
    : ICapabilityConnections
{
    private readonly Func<Type, string, object> _neuronResolver = neuronResolver ?? throw new ArgumentNullException(nameof(neuronResolver));
    private readonly ConnectionNeuronTypes? _neuronTypes = neuronTypes;

    /// <inheritdoc />
    public async Task<IReadOnlyList<BrainConnection>> ListInOrder(BrainId brain, CancellationToken ct)
    {
        var summaries = await Requests(brain).ListConnections().WaitAsync(ct).ConfigureAwait(false);
        return [.. summaries.OrderBy(summary => summary.Order).Select(ToConnection)];
    }

    /// <inheritdoc />
    public async Task<TCapability> Bind<TCapability>(BrainId brain, string key, string callerApp, CancellationToken ct) where TCapability : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var (owner, _, _) = ConnectionKeys.Parse(key);
        if (!string.Equals(owner, brain.Value, StringComparison.Ordinal))
        {
            throw new KeyNotFoundException($"Connection '{key}' does not belong to brain '{brain.Value}'.");
        }

        var tracked = await Index(brain).List().WaitAsync(ct).ConfigureAwait(false);
        if (!tracked.Contains(key, StringComparer.Ordinal))
        {
            throw new KeyNotFoundException($"Connection '{key}' is not tracked in brain '{brain.Value}'.");
        }

        try
        {
            if (_neuronResolver(typeof(TCapability), key) is TCapability bound)
            {
                return bound;
            }
        }
        catch (Exception error) when (error is InvalidOperationException or KeyNotFoundException)
        {
            throw new KeyNotFoundException($"Connection '{key}' could not be bound as '{typeof(TCapability).Name}'.", error);
        }

        throw new KeyNotFoundException($"Connection '{key}' could not be bound as '{typeof(TCapability).Name}'.");
    }

    /// <inheritdoc />
    public async Task RequestConnection(BrainId brain, CapabilityId capability, string callerApp, CancellationToken ct)
    {
        var requests = Requests(brain);
        var kinds = (await requests.ListKinds().WaitAsync(ct).ConfigureAwait(false)).Select(kind => kind.Id).ToList();
        var candidates = _neuronTypes is { Types.Count: > 0 }
            ? kinds.Where(kind => _neuronTypes.ProvidesOf(kind).Any(provided => provided.Satisfies(capability))).ToList()
            : kinds;
        if (candidates.Count == 0)
        {
            return;
        }

        var pending = await requests.ListPending().WaitAsync(ct).ConfigureAwait(false);
        if (pending.Any(request => candidates.Contains(request.Kind, StringComparer.Ordinal)))
        {
            return;
        }

        _ = await requests.Request(candidates[0]).WaitAsync(ct).ConfigureAwait(false);
    }

    private IConnectionRequests Requests(BrainId brain)
        => (IConnectionRequests)_neuronResolver(typeof(IConnectionRequests), IConnectionRequests.Key(brain.Value));

    private IConnectionIndex Index(BrainId brain)
        => (IConnectionIndex)_neuronResolver(typeof(IConnectionIndex), IConnectionIndex.Key(brain.Value));

    private static BrainConnection ToConnection(ConnectionSummary summary)
        => new(
            summary.Neuron,
            summary.Account is { Length: > 0 } account ? account : summary.Id,
            string.Equals(summary.Status, "connected", StringComparison.OrdinalIgnoreCase),
            [.. summary.Provides.Select(text => CapabilityId.TryParse(text, out var id) ? id : (CapabilityId?)null).OfType<CapabilityId>()],
            null);
}
