using DigitalBrain;
using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Supabase;

[Alias("supabase")]
public interface ISupabase : INeuron
{
    // Server-side caps.
    [ReadOnly, Alias("query")]
    Task<SupabaseQueryResult> Query(SupabaseQuery query);

    // Reads tables and columns of the configured database; omit Table for the index.
    [ReadOnly, Alias("schema")]
    Task<SupabaseSchema> ReadSchema(ReadSupabaseSchema query);

    [ReadOnly, Alias("connection")]
    Task<SupabaseConnection> ReadConnection();
}
