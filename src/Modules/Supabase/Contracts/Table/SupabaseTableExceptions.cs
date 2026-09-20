namespace DigitalBrain.Supabase.Tables;

[GenerateSerializer, Alias("supabase.table-validation-failed")]
public sealed class SupabaseTableValidationException(string message) : ArgumentException(message);

[GenerateSerializer, Alias("supabase.table-not-found")]
public sealed class SupabaseTableNotFoundException(string id) : KeyNotFoundException($"Table '{id}' was not found.");

[GenerateSerializer, Alias("supabase.table-revision-conflict")]
public sealed class SupabaseTableRevisionConflictException(string message) : InvalidOperationException(message);

// Raised when the query behind a live table was refused or the database is unreachable.
[GenerateSerializer, Alias("supabase.table-source-failed")]
public sealed class SupabaseTableSourceException(string message) : InvalidOperationException(message);