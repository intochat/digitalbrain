using Npgsql;

namespace DigitalBrain.Compute;

// The ledger's own database. A distinct type keeps it from colliding with any other module's
// NpgsqlDataSource (Supabase registers one for the customer's data).
internal sealed record ComputeDatabase(NpgsqlDataSource Source);
