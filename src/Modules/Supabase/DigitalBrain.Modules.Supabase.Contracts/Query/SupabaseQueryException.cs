namespace DigitalBrain.Supabase;

// Carries a safe error with SQLSTATE, excluding server details and connection credentials.
[GenerateSerializer, Alias("db.supabase.query-failed")]
public sealed class SupabaseQueryException(string message) : InvalidOperationException(message);

[GenerateSerializer, Alias("db.supabase.unavailable")]
public sealed class SupabaseUnavailableException(string message) : InvalidOperationException(message);
