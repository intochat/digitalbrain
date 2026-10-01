using DigitalBrain.Core;
using Orleans.Concurrency;

namespace DigitalBrain.ClickHouse.Query;

// Read-only door to the configured database. Nothing is persisted: every query, schema read and
// ping is served live by the provider, so the neuron carries no snapshot of its own.
[GrainType(ClickHouseNames.NeuronType)]
internal sealed class ClickHouseNeuron(IClickHouseProvider provider) : Neuron, IClickHouse
{
    private const int MaxTableNameLength = 255;

    [ReadOnly]
    public async Task<ClickHouseQueryResult> Query(ClickHouseQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Sql))
        {
            throw new ClickHouseQueryException("sql must not be blank. " + ClickHouseQueryGuard.Reason);
        }

        if (query.MaxRows is < 1 or > ClickHouseQuery.MaxRowsLimit)
        {
            throw new ClickHouseQueryException($"maxRows must be between 1 and {ClickHouseQuery.MaxRowsLimit}.");
        }

        try
        {
            ClickHouseQueryGuard.Validate(query.Sql);
        }
        catch (ArgumentException error)
        {
            throw new ClickHouseQueryException(error.Message);
        }

        return await provider.QueryAsync(query.Sql, query.MaxRows, cancellationToken).ConfigureAwait(true);
    }

    [ReadOnly]
    public async Task<ClickHouseSchema> ReadSchema(ReadClickHouseSchema query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var table = string.IsNullOrWhiteSpace(query.Table) ? null : query.Table.Trim();
        if (table is not null && (table.Length > MaxTableNameLength || !table.All(character => char.IsAsciiLetterOrDigit(character) || character == '_')))
        {
            throw new ClickHouseQueryException("table must be a plain table name: letters, digits and underscores.");
        }

        return await provider.ReadSchemaAsync(table, cancellationToken).ConfigureAwait(true);
    }

    [ReadOnly]
    public Task<ClickHouseConnection> ReadConnection(CancellationToken cancellationToken = default)
        => provider.PingAsync(cancellationToken);
}
