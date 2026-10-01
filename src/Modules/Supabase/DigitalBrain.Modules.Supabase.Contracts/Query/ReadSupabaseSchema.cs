namespace DigitalBrain.Supabase;

[GenerateSerializer]
[Alias("db.supabase.read-schema")]
public sealed record ReadSupabaseSchema([property: Id(0)] string? Table = null);
