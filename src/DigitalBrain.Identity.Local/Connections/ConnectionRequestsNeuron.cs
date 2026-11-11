using DigitalBrain.Neurons;
using DigitalBrain.Capabilities.Connections;
using DigitalBrain.Capabilities.Connections.Connectors;
using DigitalBrain.Capabilities.Connections.Requests;

namespace DigitalBrain.Identity.Local.Connections;

/// <summary>Persisted state of <see cref="ConnectionRequestsNeuron"/>.</summary>
public sealed class ConnectionRequestsState
{
    /// <summary>Pending connection requests in the brain.</summary>
    public ConnectionRef[] Pending { get; set; } = [];

    /// <summary>Catalog revision counter.</summary>
    public long Revision { get; set; }

    /// <summary>The brain's connection order by connection key.</summary>
    public string[] Order { get; set; } = [];
}

/// <summary>Plain neuron managing connection requests and ordering for one brain.</summary>
[Neuron(IConnectionCatalogUpdates.TypeName)]
public sealed class ConnectionRequestsNeuron(IEnumerable<ConnectorDefinition>? connectors = null)
    : Neuron<ConnectionRequestsState>, IConnectionRequests, IConnectionCatalogUpdates
{
    private readonly ConnectorDefinition[] _connectors = connectors?.ToArray() ?? [];

    private string BrainId => Id.Brain.Value;

    private IReadOnlyList<ConnectorDefinition> Connectors
        => _connectors.Length > 0
            ? _connectors
            : Seat.Services.GetService(typeof(IReadOnlyList<ConnectorDefinition>)) as IReadOnlyList<ConnectorDefinition> ?? [];

    /// <inheritdoc />
    public async Task Changed(string connectionKey)
    {
        await Check("Changed", new ConnectionBelongsToBrain(ConnectionKeys.Parse(connectionKey).BrainId, BrainId)).ConfigureAwait(true);
        var next = new ConnectionRequestsState
        {
            Pending = State.Pending,
            Revision = State.Revision + 1,
            Order = State.Order,
        };
        await Save(next, new ConnectionsChanged(next.Revision)).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public Task<ConnectorKind[]> ListKinds()
        => Task.FromResult(Connectors.Select(definition => new ConnectorKind(definition.Id, definition.DisplayName)).ToArray());

    /// <inheritdoc />
    public Task<ConnectionRef[]> ListPending()
        => Task.FromResult<ConnectionRef[]>([.. State.Pending]);

    /// <inheritdoc />
    public async Task<ConnectionSummary[]> ListConnections()
    {
        var result = new List<ConnectionSummary>();
        var hosted = Seat.Services.GetService(typeof(ConnectionNeuronTypes)) as ConnectionNeuronTypes;
        var keys = await Get<IConnectionIndex>(IConnectionIndex.Id(BrainId)).List().ConfigureAwait(true);
        foreach (var key in Ordered(keys))
        {
            var identity = ConnectionKeys.Parse(key);
            var status = "connected";
            var account = identity.ConnectionId;
            DateTimeOffset? lastProbedAt = null;
            if (hosted?.NeuronOf(identity.ConnectorId) is not null)
            {
                try
                {
                    var view = await Get<IConnection>(ConnectionKeys.IdFor(key)).Read().ConfigureAwait(true);
                    if (!string.IsNullOrEmpty(view.AuthMethodId))
                    {
                        status = view.Status.ToString().ToLowerInvariant();
                        account = view.Account ?? identity.ConnectionId;
                        lastProbedAt = view.LastProbedAt;
                    }
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                }
            }

            result.Add(new ConnectionSummary(
                identity.ConnectorId,
                identity.ConnectionId,
                key,
                status,
                account,
                lastProbedAt)
            {
                Provides = Provides(hosted, identity.ConnectorId),
                Order = result.Count,
            });
        }

        return [.. result];
    }

    /// <inheritdoc />
    public async Task Reorder(string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var known = (await Get<IConnectionIndex>(IConnectionIndex.Id(BrainId)).List().ConfigureAwait(true)).ToHashSet(StringComparer.Ordinal);
        var listed = keys.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
        var order = listed.Concat(Ordered(known).Where(key => !listed.Contains(key, StringComparer.Ordinal))).ToArray();
        if (order.SequenceEqual(State.Order, StringComparer.Ordinal))
        {
            return;
        }

        var next = new ConnectionRequestsState
        {
            Pending = State.Pending,
            Revision = State.Revision + 1,
            Order = order,
        };
        await Save(next, new ConnectionsChanged(next.Revision)).ConfigureAwait(true);
    }

    /// <inheritdoc />
    public async Task<ConnectionRef> Request(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        var id = kind.Trim();
        var known = Connectors;
        if (known.Count > 0)
        {
            await Check("Request", new IntegrationIsAvailable(kind, known.Any(definition => definition.Id == id))).ConfigureAwait(true);
        }

        var account = new ConnectionRef(id, Guid.NewGuid().ToString("n"));
        var next = new ConnectionRequestsState
        {
            Pending = [.. State.Pending, account],
            Revision = State.Revision,
            Order = State.Order,
        };
        await Save(next, new ConnectionRequested(account.Kind, account.Id)).ConfigureAwait(true);
        return account;
    }

    /// <inheritdoc />
    public async Task<ConnectorOffer[]> ListOffers()
    {
        var known = Connectors;
        if (known.Count == 0)
        {
            return [];
        }

        var connected = (await ListConnections().ConfigureAwait(true))
            .Where(row => string.Equals(row.Status, "connected", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Kind)
            .ToHashSet(StringComparer.Ordinal);
        return [.. known
            .Where(connector => connector.OAuthMethods is { Length: > 0 })
            .Select(connector => new ConnectorOffer(
                connector.Id,
                connector.DisplayName,
                true,
                connected.Contains(connector.Id)))];
    }

    private IEnumerable<string> Ordered(IEnumerable<string> keys)
    {
        var all = keys.ToList();
        var present = all.ToHashSet(StringComparer.Ordinal);
        return State.Order
            .Where(present.Contains)
            .Concat(all.Where(key => !State.Order.Contains(key, StringComparer.Ordinal)))
            .Distinct(StringComparer.Ordinal);
    }

    private static string[] Provides(ConnectionNeuronTypes? hosted, string connector)
        => hosted is null
            ? []
            : [.. hosted.ProvidesOf(connector).Select(capability => capability.ToString())];
}
