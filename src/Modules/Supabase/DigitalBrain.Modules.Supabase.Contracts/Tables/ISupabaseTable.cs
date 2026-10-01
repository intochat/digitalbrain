using DigitalBrain.Contracts;
using Orleans.Concurrency;

namespace DigitalBrain.Supabase.Tables;

[Alias("supabase.table")]
public interface ISupabaseTable : INeuron
{
    // Creates a table whose rows are served live from a read-only Supabase query.
    Task<SupabaseTableSnapshot> CreateFromQuery(CreateQueryTable request);

    // Creates once for an operation; identical retries return the saved view.
    Task<SupabaseTableSnapshot> CreateFromQueryOnce(string operationId, CreateQueryTable request, CancellationToken cancellationToken = default);

    // Replaces the saved view at an expected revision.
    Task<SupabaseTableSnapshot> UpdateView(UpdateSupabaseTableView request);

    [ReadOnly]
    Task<SupabaseTableSnapshot?> Read(ReadSupabaseTable query);

    // Computes one aggregate over the saved view's filtered rows without returning rows.
    [ReadOnly]
    Task<SupabaseTableAggregate?> Aggregate(ReadSupabaseTableAggregate query);

    [ReadOnly]
    Task<SupabaseTableSummary?> ReadSummary();
}
