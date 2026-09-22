using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Concurrency;

namespace DigitalBrain.Supabase;

// Read-only door to the configured database. Nothing is persisted: every query, schema read and
// ping is served live by the provider, so the neuron carries no snapshot of its own.
[GrainType(SupabaseNames.NeuronType)]
internal sealed class SupabaseNeuron : Neuron, ISupabase
{
    private const int MaxTableNameLength = 255;

    private ISupabaseProvider? _provider;

    private ISupabaseProvider Provider => _provider ??= ServiceProvider.GetRequiredService<ISupabaseProvider>();

    [ReadOnly]
    public async Task<SupabaseQueryResult> Query(SupabaseQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (string.IsNullOrWhiteSpace(query.Sql))
        {
            throw new SupabaseQueryException("sql must not be blank. " + SupabaseQueryGuard.Reason);
        }

        if (query.MaxRows is < 1 or > SupabaseQuery.MaxRowsLimit)
        {
            throw new SupabaseQueryException($"maxRows must be between 1 and {SupabaseQuery.MaxRowsLimit}.");
        }

        string sql;
        try
        {
            sql = SupabaseQueryGuard.Normalize(query.Sql);
        }
        catch (ArgumentException error)
        {
            throw new SupabaseQueryException(error.Message);
        }

        return await Provider.QueryAsync(sql, query.MaxRows, CancellationToken.None);
    }

    [ReadOnly]
    public async Task<SupabaseSchema> ReadSchema(ReadSupabaseSchema query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var table = string.IsNullOrWhiteSpace(query.Table) ? null : query.Table.Trim();
        if (table is not null && (table.Length > MaxTableNameLength || !table.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '.')))
        {
            throw new SupabaseQueryException("table must be a plain table name, optionally prefixed with public.");
        }

        return await Provider.ReadSchemaAsync(table, CancellationToken.None);
    }

    [ReadOnly]
    public Task<SupabaseConnection> ReadConnection() => Provider.PingAsync(CancellationToken.None);
}