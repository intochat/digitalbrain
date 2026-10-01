using System.Text.Json;
using DigitalBrain.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace DigitalBrain.CustomerResearcher;

public sealed record ResearchEvidence(string Field, string Value, string Url, string Quote);
public sealed record CompanyResearch(string CompanyName, string? Website, string? Location, string? Email,
    string? Phone, string? Industry, string? Summary, IReadOnlyList<ResearchEvidence> Evidence);

public interface ICompanyResearchStore
{
    Task Save(string workspace, string researchId, CompanyResearch company, CancellationToken ct);
}

// Typed application writes; the general Postgres query interface remains read-only.
public sealed class CompanyResearchStore([FromKeyedServices(PostgresHosting.DataSourceKey)] NpgsqlDataSource source) : ICompanyResearchStore
{
    public async Task Save(string workspace, string researchId, CompanyResearch company, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(researchId);
        await using var connection = await source.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        // Transaction-scoped lock makes first-use schema setup safe across windows/processes.
        await using (var setup = new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(721940239);
            CREATE TABLE IF NOT EXISTS customer_research (
              workspace text NOT NULL, research_id text NOT NULL, company_name text NOT NULL,
              website text, location text, email text, phone text, industry text, summary text,
              evidence jsonb NOT NULL, updated_at timestamptz NOT NULL DEFAULT now(),
              PRIMARY KEY(workspace, research_id));
            """, connection, transaction)) { await setup.ExecuteNonQueryAsync(ct); }
        await using var command = new NpgsqlCommand("""
            INSERT INTO customer_research(workspace,research_id,company_name,website,location,email,phone,industry,summary,evidence)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10)
            ON CONFLICT(workspace,research_id) DO UPDATE SET company_name=EXCLUDED.company_name,
              website=EXCLUDED.website,location=EXCLUDED.location,email=EXCLUDED.email,phone=EXCLUDED.phone,
              industry=EXCLUDED.industry,summary=EXCLUDED.summary,evidence=EXCLUDED.evidence,updated_at=now()
            """, connection, transaction);
        foreach (var value in new[] { workspace, researchId, company.CompanyName, company.Website, company.Location, company.Email, company.Phone, company.Industry, company.Summary })
        { command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)value ?? DBNull.Value }); }
        command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Jsonb, Value = JsonSerializer.Serialize(company.Evidence) });
        await command.ExecuteNonQueryAsync(ct);
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
    }
}
