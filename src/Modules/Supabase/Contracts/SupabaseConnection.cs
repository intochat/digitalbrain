namespace DigitalBrain.Supabase;

[GenerateSerializer]
[Alias("db.supabase.connection")]
public sealed record SupabaseConnection(
    [property: Id(0)] bool Connected,
    [property: Id(1)] string Database,
    [property: Id(2)] string? ServerVersion,
    [property: Id(3)] string Provider);
