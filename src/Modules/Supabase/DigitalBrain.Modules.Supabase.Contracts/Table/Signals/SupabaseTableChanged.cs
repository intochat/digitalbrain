using DigitalBrain.Contracts;

namespace DigitalBrain.Supabase.Tables.Signals;

// Published when a live query table is created or its saved view changes.
[GenerateSerializer, Alias("supabase.table-changed")]
public sealed record SupabaseTableChanged(
    [property: Id(0)] string Name,
    [property: Id(1)] string Title,
    [property: Id(2)] long Revision) : Signal;