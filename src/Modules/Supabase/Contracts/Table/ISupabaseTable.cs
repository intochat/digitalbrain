using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Supabase.Tables;

[Alias("supabase.table")]
public interface ISupabaseTable : INeuron
{
    /// <summary>Creates a table whose rows are served live from a read-only Supabase query.</summary>
    Task<SupabaseTableSnapshot> CreateFromQuery(CreateQueryTable request);

    /// <summary>Creates once for an operation; identical retries return the saved view.</summary>
    Task<SupabaseTableSnapshot> CreateFromQueryOnce(string operationId, CreateQueryTable request, CancellationToken cancellationToken = default);

    /// <summary>Replaces the saved view at an expected revision.</summary>
    Task<SupabaseTableSnapshot> UpdateView(UpdateSupabaseTableView request);

    [ReadOnly]
    Task<SupabaseTableSnapshot?> Read(ReadSupabaseTable query);

    [ReadOnly]
    Task<SupabaseTableSummary?> ReadSummary();
}
