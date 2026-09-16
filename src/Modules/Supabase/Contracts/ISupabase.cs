using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Supabase;

[Alias("supabase")]
public interface ISupabase : INeuron
{
    /// <summary>Runs one read-only SELECT with server-side caps and returns typed rows.</summary>
    [ReadOnly, Alias("query")]
    Task<SupabaseQueryResult> Query(SupabaseQuery query, CancellationToken cancellationToken = default);

    /// <summary>Reads tables and columns of the configured database; omit Table for the index.</summary>
    [ReadOnly, Alias("schema")]
    Task<SupabaseSchema> ReadSchema(ReadSupabaseSchema query, CancellationToken cancellationToken = default);

    [ReadOnly, Alias("connection")]
    Task<SupabaseConnection> ReadConnection(CancellationToken cancellationToken = default);
}
