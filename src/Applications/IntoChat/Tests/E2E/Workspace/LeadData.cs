using Aspire.Hosting.Testing;
using Npgsql;

namespace IntoChat.Tests.E2E.Workspace;

/// <summary>Scenario data in the application's own temporary database; owns no deployment.</summary>
internal static class LeadData
{
    public static async Task SeedAsync(E2EBrain brain, string marker, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(await brain.Application.GetConnectionStringAsync("supabase-database", ct));
        await connection.OpenAsync(ct);
        await using var schema = new NpgsqlCommand(
            "CREATE TABLE leads (id int PRIMARY KEY, company text, email text, active boolean)", connection);
        await schema.ExecuteNonQueryAsync(ct);
        await using var seed = new NpgsqlCommand(
            "INSERT INTO leads SELECT n, CASE WHEN n = 55 THEN $1 ELSE 'Company ' || n END, 'lead' || n || '@example.test', true FROM generate_series(1,60) n UNION ALL SELECT 61, 'Inactive control', 'inactive@example.test', false", connection);
        seed.Parameters.AddWithValue(marker);
        await seed.ExecuteNonQueryAsync(ct);
    }

    public static async Task DropAsync(E2EBrain brain, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(await brain.Application.GetConnectionStringAsync("supabase-database", ct));
        await connection.OpenAsync(ct);
        await using var command = new NpgsqlCommand("DROP TABLE leads", connection);
        await command.ExecuteNonQueryAsync(ct);
    }
}