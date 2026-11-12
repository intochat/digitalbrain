using DigitalBrain.Neurons;
using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Connections;
using DigitalBrain.Capabilities.Connections.Broker;
using DigitalBrain.Capabilities.Connections.Catalog;
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
        var catalog = await Catalog(brain).Read().WaitAsync(ct).ConfigureAwait(false);
        var rows = Ordered(catalog.Rows, catalog.Order);
        var result = new List<BrainConnection>();
        foreach (var row in rows)
        {
            var provides = _neuronTypes?.ProvidesOf(row.ConnectorId) ?? [];
            result.Add(new BrainConnection(
                row.Key,
                row.Account is { Length: > 0 } account ? account : row.ConnectionId,
                row.Status == ConnectionStatus.Connected,
                [.. provides],
                null));
        }

        return result;
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

        var catalog = await Catalog(brain).Read().WaitAsync(ct).ConfigureAwait(false);
        if (catalog.RowOf(key) is null)
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

    private IConnectionCatalog Catalog(BrainId brain)
        => (IConnectionCatalog)_neuronResolver(typeof(IConnectionCatalog), IConnectionCatalog.Key(brain.Value));

    private IConnectionRequests Requests(BrainId brain)
        => (IConnectionRequests)_neuronResolver(typeof(IConnectionRequests), IConnectionRequests.Key(brain.Value));

    private static List<CatalogRow> Ordered(CatalogRow[] rows, string[] order)
    {
        var rowMap = rows.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var ordered = new List<CatalogRow>();
        foreach (var key in order)
        {
            if (rowMap.Remove(key, out var row))
            {
                ordered.Add(row);
            }
        }

        ordered.AddRange(rowMap.Values);
        return ordered;
    }
}
