using Aspire.Hosting;
using Aspire.Hosting.Testing;
using DigitalBrain.Flutter;
using DigitalBrain.Supabase;
using Npgsql;
using DigitalBrain.Testing.Integration;

namespace IntoChat.Tests.Fixtures;

public sealed class AgentDataFixture : IAsyncDisposable
{
    private IntegrationBrain? _database;
    private IntoChatTestDeployment? _deployment;
    private string? _ownerConnection;
    public E2EBrain Brain { get; private set; } = null!;
    public ScriptedModelServer Model { get; private set; } = null!;
    public string Marker { get; } = "Company-" + Guid.NewGuid().ToString("N");
    public static async Task<AgentDataFixture> StartAsync(CancellationToken ct, bool web = true, bool live = false)
    {
        var fixture = new AgentDataFixture();
        try { await fixture.Start(ct, web, live); return fixture; }
        catch { await fixture.DisposeAsync(); throw; }
    }
    private async Task Start(CancellationToken ct, bool web, bool live)
    {
        var liveKey = live ? Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_MODEL_API_KEY") : null;
        if (live && string.IsNullOrWhiteSpace(liveKey))
            { throw new InvalidOperationException("Live E2E requires DIGITALBRAIN_E2E_MODEL_API_KEY. This lane makes paid model calls."); }
        _database = await IntegrationTest.Create().WithModule<SupabaseModule>(database => database.WithPostgres()).StartAsync(ct);
        var ownerConnection = await _database.Application.GetConnectionStringAsync("supabase-database", ct);
        _ownerConnection = ownerConnection;
        await using var owner = new NpgsqlConnection(ownerConnection);
        await owner.OpenAsync(ct);
        var password = Guid.NewGuid().ToString("N");
        // Password is generated hex; all user/data values below use parameters.
        await using (var schema = new NpgsqlCommand($"CREATE ROLE agent_reader LOGIN PASSWORD '{password}'; CREATE TABLE leads (id int PRIMARY KEY, company text, email text, active boolean); GRANT USAGE ON SCHEMA public TO agent_reader; GRANT SELECT ON leads TO agent_reader;", owner))
            { await schema.ExecuteNonQueryAsync(ct); }
        await using (var seed = new NpgsqlCommand("INSERT INTO leads SELECT n, CASE WHEN n = 55 THEN $1 ELSE 'Company ' || n END, 'lead' || n || '@example.test', true FROM generate_series(1,60) n UNION ALL SELECT 61, 'Inactive control', 'inactive@example.test', false", owner))
        { seed.Parameters.AddWithValue(Marker); await seed.ExecuteNonQueryAsync(ct); }
        var appConnection = new NpgsqlConnectionStringBuilder(ownerConnection) { Username = "agent_reader", Password = password }.ConnectionString;
        await using (var reader = new NpgsqlConnection(appConnection))
        {
            await reader.OpenAsync(ct);
            await using var forbidden = new NpgsqlCommand("DELETE FROM leads", reader);
            var error = await Assert.ThrowsAsync<PostgresException>(() => forbidden.ExecuteNonQueryAsync(ct));
            Assert.Equal("42501", error.SqlState);
        }
        if (!live) { Model = await ScriptedModelServer.StartAsync(ct); }
        _deployment = await IntoChatTestDeployment.CreateAsync(ct);
        var endpoint = live ? new Uri(Environment.GetEnvironmentVariable("DIGITALBRAIN_E2E_MODEL_ENDPOINT") ?? "https://api.openai.com/v1/") : Model.Endpoint;
        var test = _deployment.Configure(E2ETest.For<Projects.IntoChat_AppHost>(), endpoint, _deployment.SolutionPath,
            new Dictionary<string, string?> { ["Parameters:supabase-connection"] = appConnection, ["Parameters:openai-api-key"] = liveKey ?? "fixture-key" })
            .ConfigureModule<SupabaseModule>(database => database.WithConnection("supabase"));
        if (!web) { test.ConfigureModule<FlutterModule>(flutter => flutter.WithoutHost()); }
        Brain = await test.StartAsync(ct);
        var actual = await Brain.Get<ISupabase>("fixture-preflight").ReadSchema(new("public.leads"));
        Assert.Equal(new NpgsqlConnectionStringBuilder(appConnection).Database, actual.Database);
        Assert.Contains(Assert.Single(actual.Tables).Columns, column => column.Name == "company");
        Assert.Equal("number", actual.Tables.Single().Columns.Single(column => column.Name == "id").TableType);
    }
    public async ValueTask DisposeAsync()
    {
        try { if (Brain is not null) { await Brain.DisposeAsync(); } }
        finally
        {
            try { if (_deployment is not null) { await _deployment.DisposeAsync(); } }
            finally
            {
                try { if (Model is not null) { await Model.DisposeAsync(); } }
                finally { if (_database is not null) { await _database.DisposeAsync(); } }
            }
        }
    }
    public async Task RevokeDataAccess(CancellationToken ct)
    {
        await using var owner = new NpgsqlConnection(_ownerConnection);
        await owner.OpenAsync(ct);
        await using var command = new NpgsqlCommand("REVOKE SELECT ON leads FROM agent_reader", owner);
        await command.ExecuteNonQueryAsync(ct);
    }
    public Task StopDatabase(CancellationToken ct) => _database!.Application.StopAsync(ct);
}

