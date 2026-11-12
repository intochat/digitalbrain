namespace DigitalBrain.Capabilities.Connections.Catalog;

using DigitalBrain.Brains;
using DigitalBrain.Capabilities.Connections.Connectors;
using DigitalBrain.Capabilities.Connections.Requests;
using DigitalBrain.Neurons;

/// <summary>CQRS projection and request neuron managing connection catalog state for one brain.</summary>
[Neuron(IConnectionCatalog.TypeName)]
public sealed class ConnectionCatalogNeuron : Neuron<ConnectionCatalog>, IConnectionCatalog, IConnectionRequests
{
    private IConnectorOffers? _offers;

    /// <summary>Creates a new connection catalog neuron.</summary>
    public ConnectionCatalogNeuron(IConnectorOffers? offers = null)
    {
        _offers = offers;
    }

    private string BrainId => Id.Brain.Value;

    private IConnectorOffers Offers => _offers ??= ResolveOffers();

    private IConnectorOffers ResolveOffers()
        => Seat.Services.GetService(typeof(IConnectorOffers)) as IConnectorOffers
           ?? new DefaultConnectorOffers(
               Seat.Services.GetService(typeof(IReadOnlyList<ConnectorDefinition>)) as IReadOnlyList<ConnectorDefinition>,
               Seat.Services.GetService(typeof(ConnectionNeuronTypes)) as ConnectionNeuronTypes);

    /// <inheritdoc />
    protected override async Task OnActivate(CancellationToken cancellationToken)
    {
        await Context.Synapses.EnsureBrainWide(
            typeof(ConnectionConnected),
            typeof(ConnectionExpired),
            typeof(ConnectionFailing),
            typeof(ConnectionAdopted),
            typeof(ConnectionDisconnected)).ConfigureAwait(true);
    }

    /// <summary>Handles connection connected facts to update the catalog row.</summary>
    [On]
    public Task On(ConnectionConnected signal)
    {
        var key = ConnectionKeys.For(BrainId, signal.ConnectorId, signal.ConnectionId);
        var existing = State.RowOf(key);
        var lastProbed = signal.ProbedAt ?? existing?.LastProbedAt;
        var row = new CatalogRow(key, signal.ConnectorId, signal.ConnectionId, ConnectionStatus.Connected, signal.Account, lastProbed);
        return Apply(State.Put(row));
    }

    /// <summary>Handles connection expired facts to update the catalog row.</summary>
    [On]
    public Task On(ConnectionExpired signal)
    {
        var key = ConnectionKeys.For(BrainId, signal.ConnectorId, signal.ConnectionId);
        var existing = State.RowOf(key);
        var lastProbed = signal.ProbedAt ?? existing?.LastProbedAt;
        var row = new CatalogRow(key, signal.ConnectorId, signal.ConnectionId, ConnectionStatus.Expired, existing?.Account, lastProbed);
        return Apply(State.Put(row));
    }

    /// <summary>Handles connection failing facts to update the catalog row.</summary>
    [On]
    public Task On(ConnectionFailing signal)
    {
        var key = ConnectionKeys.For(BrainId, signal.ConnectorId, signal.ConnectionId);
        var existing = State.RowOf(key);
        var lastProbed = signal.ProbedAt ?? existing?.LastProbedAt;
        var row = new CatalogRow(key, signal.ConnectorId, signal.ConnectionId, ConnectionStatus.Failing, existing?.Account, lastProbed);
        return Apply(State.Put(row));
    }

    /// <summary>Handles connection adopted facts to register metadata awaiting import.</summary>
    [On]
    public Task On(ConnectionAdopted signal)
    {
        var key = ConnectionKeys.For(BrainId, signal.ConnectorId, signal.ConnectionId);
        var existing = State.RowOf(key);
        var row = new CatalogRow(key, signal.ConnectorId, signal.ConnectionId, ConnectionStatus.Disconnected, signal.Account, existing?.LastProbedAt);
        return Apply(State.Put(row));
    }

    /// <summary>Handles connection disconnected facts to remove the connection from the catalog.</summary>
    [On]
    public Task On(ConnectionDisconnected signal)
    {
        var key = ConnectionKeys.For(BrainId, signal.ConnectorId, signal.ConnectionId);
        return Apply(State.Without(key));
    }

    private Task Apply(ConnectionCatalog next)
    {
        if (ReferenceEquals(next, State))
        {
            return Task.CompletedTask;
        }

        return Save(next with { Revision = State.Revision + 1 }, new ConnectionsChanged(State.Revision + 1));
    }

    /// <inheritdoc />
    public Task Subscribed() => Task.CompletedTask;

    /// <inheritdoc />
    public Task<ConnectionCatalog> Read() => Task.FromResult(State);

    /// <inheritdoc />
    public Task<ConnectorKind[]> ListKinds()
        => Task.FromResult(Offers.Declared.Select(d => new ConnectorKind(d.Id, d.DisplayName)).ToArray());

    /// <inheritdoc />
    public Task<ConnectionRef[]> ListPending()
        => Task.FromResult<ConnectionRef[]>([.. State.Pending]);

    /// <inheritdoc />
    public Task<ConnectionSummary[]> ListConnections()
    {
        var result = new List<ConnectionSummary>();
        var rows = Ordered(State.Rows);
        foreach (var row in rows)
        {
            result.Add(new ConnectionSummary(
                row.ConnectorId,
                row.ConnectionId,
                row.Key,
                row.Status.ToString().ToLowerInvariant(),
                row.Account ?? row.ConnectionId,
                row.LastProbedAt)
            {
                Provides = [.. Offers.ProvidesOf(row.ConnectorId).Select(c => c.ToString())],
                Order = result.Count,
            });
        }

        return Task.FromResult<ConnectionSummary[]>([.. result]);
    }

    /// <inheritdoc />
    public async Task Reorder(string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var known = State.Rows.Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var listed = keys.Where(known.Contains).Distinct(StringComparer.Ordinal).ToList();
        var order = listed.Concat(OrderedKeys(known).Where(key => !listed.Contains(key, StringComparer.Ordinal))).ToArray();
        if (order.SequenceEqual(State.Order, StringComparer.Ordinal))
        {
            return;
        }

        var next = State with
        {
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
        var known = Offers.Offered;
        if (known.Count > 0)
        {
            await Check("Request", new IntegrationIsAvailable(kind, known.Any(definition => definition.Id == id))).ConfigureAwait(true);
        }

        var account = new ConnectionRef(id, Guid.NewGuid().ToString("n"));
        var next = State with
        {
            Pending = [.. State.Pending, account],
        };
        await Save(next, new ConnectionRequested(account.Kind, account.Id)).ConfigureAwait(true);
        return account;
    }

    /// <inheritdoc />
    public Task<ConnectorOffer[]> ListOffers()
    {
        var known = Offers.Offered;
        if (known.Count == 0)
        {
            return Task.FromResult<ConnectorOffer[]>([]);
        }

        var connected = State.Rows
            .Where(row => row.Status == ConnectionStatus.Connected)
            .Select(row => row.ConnectorId)
            .ToHashSet(StringComparer.Ordinal);

        var result = known
            .Where(connector => connector.OAuthMethods is { Length: > 0 })
            .Select(connector => new ConnectorOffer(
                connector.Id,
                connector.DisplayName,
                true,
                connected.Contains(connector.Id)))
            .ToArray();

        return Task.FromResult(result);
    }

    private List<CatalogRow> Ordered(IEnumerable<CatalogRow> rows)
    {
        var rowMap = rows.ToDictionary(r => r.Key, StringComparer.Ordinal);
        var ordered = new List<CatalogRow>();
        foreach (var key in State.Order)
        {
            if (rowMap.Remove(key, out var row))
            {
                ordered.Add(row);
            }
        }

        ordered.AddRange(rowMap.Values);
        return ordered;
    }

    private IEnumerable<string> OrderedKeys(IEnumerable<string> keys)
    {
        var all = keys.ToList();
        var present = all.ToHashSet(StringComparer.Ordinal);
        return State.Order
            .Where(present.Contains)
            .Concat(all.Where(key => !State.Order.Contains(key, StringComparer.Ordinal)))
            .Distinct(StringComparer.Ordinal);
    }
}
