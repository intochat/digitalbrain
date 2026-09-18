using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Flutter;

namespace DigitalBrain.Supabase;

[Alias("supabase.table")]
public interface ISupabaseTable : ITable
{
    /// <summary>Schedules creation of a table whose rows are served live from a Supabase query.</summary>
    [Alias("create-query")]
    Task<Accepted<string>> CreateFromQuery(CreateQueryTableCommand command, CancellationToken cancellationToken = default);
}
