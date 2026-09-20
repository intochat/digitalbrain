namespace DigitalBrain.Supabase.Tables;

[GenerateSerializer]
[Alias("supabase.read-table")]
public sealed record ReadSupabaseTable(
    [property: Id(0)] int Offset = 0,
    [property: Id(1)] int Limit = 50);