using System.Text.Json;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Core.Enforcement;
using DigitalBrain.Postgres;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Modules.Postgres.Tests.Unit;

public sealed class PostgresWriteTableFacts
{
    private static readonly TableDefinition Definition = new([new("id", "text"), new("value", "text")], ["id"]);
    private static TableValue[] Key(string id = "one") => [new("id", JsonSerializer.Serialize(id))];
    private static TableValue[] Values(string value) => [new("value", JsonSerializer.Serialize(value))];

    [Fact]
    public async Task DefiningTheSameSchemaIsANoopAndSurvivesReactivation()
    {
        var provider = new MemoryTables();
        await using var brain = await Start(provider);
        Stamp();
        var table = brain.Get<IPostgresTable>("definitions");
        var accepted = await table.Define(Definition);
        Assert.Equal(1, accepted.Revision);
        Assert.Equal(accepted.Table, (await table.Define(new(Definition.Columns.Reverse().ToArray(), ["id"]))).Table);
        await brain.DeactivateAsync(table, TestContext.Current.CancellationToken);
        Assert.Equal(accepted.Table, (await table.Define(Definition)).Table);
        Assert.Equal(1, provider.Definitions);
        var error = await Assert.ThrowsAsync<PostgresQueryException>(() => table.Define(new([new("id", "text"), new("value", "number")], ["id"])));
        Assert.Contains("Incompatible", error.Message);
    }

    [Fact]
    public async Task InvalidSchemasAndValuesAreRefusedBeforeTheProviderRuns()
    {
        var provider = new MemoryTables();
        await using var brain = await Start(provider);
        Stamp();
        var table = brain.Get<IPostgresTable>("invalid");
        foreach (var name in new[] { "bad-name", "x; DROP TABLE x", "é", "1name", new string('a', 64), "" })
        { await Assert.ThrowsAsync<PostgresQueryException>(() => table.Define(new([new(name, "text")], [name]))); }
        await Assert.ThrowsAsync<PostgresQueryException>(() => table.Define(new([new("id", "varchar")], ["id"])));
        await Assert.ThrowsAsync<PostgresQueryException>(() => table.Define(new([new("id", "text"), new("id", "text")], ["id"])));
        await Assert.ThrowsAsync<PostgresQueryException>(() => table.Define(new([new("id", "text")], ["missing"])));
        Assert.Equal(0, provider.Definitions);
        await table.Define(Definition);
        await Assert.ThrowsAsync<PostgresQueryException>(() => table.Upsert(Key(), [new("value", "42")]));
        await Assert.ThrowsAsync<PostgresQueryException>(() => table.Upsert([new("id", "null")], Values("test")));
        await Assert.ThrowsAsync<PostgresQueryException>(() => table.Upsert(Key(), [new("value", "not json")]));
        await Assert.ThrowsAsync<PostgresQueryException>(() => table.Upsert(Key(), []));
        await Assert.ThrowsAsync<PostgresQueryException>(() => table.Page(-1, 10));
        await Assert.ThrowsAsync<PostgresQueryException>(() => table.Page(0, 1001));
        await Assert.ThrowsAsync<PostgresQueryException>(() => table.Page(0, 0));
    }

    [Fact]
    public async Task UpsertsUpdateOneKeyAndIdenticalRetriesAreNoOps()
    {
        await using var brain = await Start(new MemoryTables());
        Stamp();
        var table = brain.Get<IPostgresTable>("rows");
        await table.Define(Definition);
        await ExerciseRows(table);
    }

    [Fact]
    public async Task OtherBrainsAndAppsCannotReadOrClobberAnOwnedTable()
    {
        await using var brain = await Start(new MemoryTables());
        Stamp();
        var first = brain.Get<IPostgresTable>("first");
        var physical = (await first.Define(Definition)).Table;
        await first.Upsert(Key(), Values("first"));
        foreach (var (brainId, appId) in new[] { ("other", "app"), ("brain", "other-app") })
        {
            Stamp(brainId, appId);
            var second = brain.Get<IPostgresTable>(brainId + appId);
            Assert.NotEqual(physical, (await second.Define(Definition)).Table);
            Assert.Null(await second.Read(Key()));
            await second.Upsert(Key(), Values("second"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first.Define(Definition));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first.Read(Key()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first.Page());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first.Upsert(Key(), Values("clobber")));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => first.Delete(Key()));
        }
        Stamp();
        Assert.Equal("\"first\"", (await first.Read(Key()))!.Single(v => v.Column == "value").Json);
        RequestContext.Remove(CallerContextStamper.RequestContextKey);
        await Assert.ThrowsAsync<UntrustedCallerException>(() => first.Read(Key()));
    }

    [Fact]
    public async Task RowSignalsCarryTheKeyAndTheKernelStampedPublisher()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new MemoryTables());
        Stamp();
        var table = brain.Get<IPostgresTable>("signals");
        await using var definitions = await brain.SubscribeAsync<TableDefined>(table, ct);
        var accepted = await table.Define(Definition);
        await using var defined = definitions.ReadAllAsync(ct).GetAsyncEnumerator(ct);
        Assert.True(await defined.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(accepted.Table, defined.Current.Table);
        await using var stream = await brain.SubscribeAsync<RowUpserted>(table, ct);
        await using var reader = stream.ReadAllAsync(ct).GetAsyncEnumerator(ct);
        await table.Upsert(Key(), Values("payload"));
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(table.GetGrainId().ToString(), reader.Current.Publisher);
        Assert.Equal(accepted.Table, reader.Current.Table);
        Assert.Equal(Key(), reader.Current.Key);
        await using var deleted = await brain.SubscribeAsync<RowDeleted>(table, ct);
        await table.Delete(Key());
        await using var deletion = deleted.ReadAllAsync(ct).GetAsyncEnumerator(ct);
        Assert.True(await deletion.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.Equal(table.GetGrainId().ToString(), deletion.Current.Publisher);
    }

    [Fact]
    public async Task CustomerResearchCanBeWrittenUsingOnlyTheNeuronContract()
    {
        await using var brain = await Start(new MemoryTables());
        Stamp();
        await Research(brain.Get<IPostgresTable>("research-results"));
    }

    [Fact]
    public async Task LiveTablesAreParameterizedIdempotentAndVisibleThroughTheReadDoor()
    {
        var connection = Environment.GetEnvironmentVariable("DIGITALBRAIN_POSTGRES_TEST_CONNECTION")
            ?? Environment.GetEnvironmentVariable("CUSTOMER_RESEARCH_TEST_POSTGRES");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(connection), "Set DIGITALBRAIN_POSTGRES_TEST_CONNECTION or CUSTOMER_RESEARCH_TEST_POSTGRES to run live table writes.");
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(null, connection!);
        Stamp();
        var table = brain.Get<IPostgresTable>("live-" + Guid.NewGuid().ToString("N"));
        var research = brain.Get<IPostgresTable>("research-" + Guid.NewGuid().ToString("N"));
        var accepted = await table.Define(Definition);
        string? researchName = null;
        string? otherName = null;
        try
        {
            await ExerciseRows(table);
            await table.Upsert(Key(), Values("'; DROP TABLE --"));
            var result = await brain.Get<IPostgres>("default").Query(new($"SELECT value FROM public.{accepted.Table}"));
            Assert.Equal("'; DROP TABLE --", JsonSerializer.Deserialize<string>(Assert.Single(Assert.Single(result.Rows))));
            await Assert.ThrowsAsync<PostgresQueryException>(() => brain.Get<IPostgres>("default").Query(new($"DELETE FROM public.{accepted.Table}")));
            var provider = brain.SiloServices.GetRequiredService<IPostgresTableProvider>();
            await provider.DefineAsync(PostgresCapacityKind.PlatformOrigin, accepted.Table, Definition, ct);
            await Assert.ThrowsAsync<PostgresQueryException>(() => provider.DefineAsync(PostgresCapacityKind.PlatformOrigin, accepted.Table,
                new([new("id", "text"), new("value", "number")], ["id"]), ct));
            Stamp("other-brain");
            var other = brain.Get<IPostgresTable>("other-" + Guid.NewGuid().ToString("N"));
            otherName = (await other.Define(Definition)).Table;
            Assert.Null(await other.Read(Key()));
            await other.Upsert(Key(), Values("other"));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => table.Read(Key()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => table.Upsert(Key(), Values("clobber")));
            Stamp();
            Assert.Equal("'; DROP TABLE --", JsonSerializer.Deserialize<string>((await table.Read(Key()))!.Single(v => v.Column == "value").Json));
            researchName = await Research(research);
            await brain.DeactivateAsync(table, ct);
            Assert.Equal(accepted.Table, (await table.Define(Definition)).Table);
        }
        finally
        {
            await using var source = NpgsqlDataSource.Create(connection!);
            foreach (var name in new[] { accepted.Table, researchName, otherName }.OfType<string>())
            {
                PostgresTablePolicy.Identifier(name);
                await using var cleanup = source.CreateCommand($"DROP TABLE public.\"{name}\"");
                await cleanup.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private static async Task ExerciseRows(IPostgresTable table)
    {
        Assert.True(await table.Upsert(Key(), Values("'; DROP TABLE --")));
        Assert.False(await table.Upsert(Key(), Values("'; DROP TABLE --")));
        Assert.Equal("'; DROP TABLE --", JsonSerializer.Deserialize<string>((await table.Read(Key()))!.Single(v => v.Column == "value").Json));
        Assert.True(await table.Upsert(Key(), Values("updated")));
        Assert.Single(await table.Page());
        Assert.Equal("\"updated\"", (await table.Read(Key()))!.Single(v => v.Column == "value").Json);
        Assert.Empty(await table.Page(1, 1));
        Assert.True(await table.Delete(Key()));
        Assert.False(await table.Delete(Key()));
        Assert.Null(await table.Read(Key()));
    }

    // The script needs only the Contracts assembly: no store, Npgsql or caller-supplied workspace.
    private static async Task<string> Research(IPostgresTable table)
    {
        var columns = new[] { "research_id", "company_name", "website", "location", "email", "phone", "industry", "summary" }
            .Select(name => new TableColumn(name, "text")).Concat([new("evidence", "jsonb"), new("updated_at", "timestamptz")]).ToArray();
        var accepted = await table.Define(new(columns, ["research_id"]));
        TableValue[] key = [new("research_id", "\"retry\"")];
        var values = columns.Where(c => c.Name != "research_id").Select(c => new TableValue(c.Name, c.Name switch
        {
            "company_name" => "\"O'Reilly; DROP TABLE customers; --\"",
            "evidence" => "[{\"Field\":\"Website\",\"Value\":\"https://example.com\",\"Url\":\"https://example.com\",\"Quote\":\"Evidence\"}]",
            "updated_at" => "\"2026-10-01T12:00:00Z\"",
            _ => "null"
        })).ToArray();
        Assert.True(await table.Upsert(key, values));
        Assert.False(await table.Upsert(key, values));
        values = values.Select(v => v.Column == "summary" ? new TableValue("summary", "\"Updated\"") : v).ToArray();
        Assert.True(await table.Upsert(key, values));
        var row = (await table.Read(key))!;
        Assert.Equal("\"Updated\"", row.Single(v => v.Column == "summary").Json);
        using var evidence = JsonDocument.Parse(row.Single(v => v.Column == "evidence").Json);
        Assert.Equal("Evidence", evidence.RootElement[0].GetProperty("Quote").GetString());
        return accepted.Table;
    }

    private static void Stamp(string brain = "brain", string app = "app")
        => CallerContextStamper.Stamp(new()
        {
            PrincipalId = "alice",
            AccountId = "alice",
            BrainId = brain,
            Kind = CallerKind.App,
            StampedBy = TrustedEdge.AppProxy,
            AppId = app
        });

    private static Task<UnitBrain> Start(IPostgresTableProvider? provider, string connection = "Host=localhost;Database=sample;Username=reader")
        => UnitTest.Create().WithModule<PostgresModule>().ConfigureSilo(silo =>
        {
            silo.Configuration["ConnectionStrings:postgres"] = connection;
            if (provider is not null) { silo.Services.AddSingleton(provider); }
        }).StartAsync(TestContext.Current.CancellationToken);

    private sealed class MemoryTables : IPostgresTableProvider
    {
        private readonly Dictionary<string, Dictionary<string, TableValue[]>> tables = [];
        public int Definitions { get; private set; }
        public Task DefineAsync(string origin, string table, TableDefinition definition, CancellationToken ct)
        { Definitions++; tables.TryAdd(table, []); return Task.CompletedTask; }
        private static string RowKey(TableValue[] key) => JsonSerializer.Serialize(key);
        public Task<bool> UpsertAsync(string origin, string table, TableDefinition definition, TableValue[] key, TableValue[] values, CancellationToken ct)
        {
            var row = key.Concat(values).ToArray();
            var changed = !tables[table].TryGetValue(RowKey(key), out var previous) || !row.SequenceEqual(previous);
            tables[table][RowKey(key)] = row;
            return Task.FromResult(changed);
        }
        public Task<bool> DeleteAsync(string origin, string table, TableDefinition definition, TableValue[] key, CancellationToken ct)
            => Task.FromResult(tables[table].Remove(RowKey(key)));
        public Task<TableValue[]?> ReadAsync(string origin, string table, TableDefinition definition, TableValue[] key, CancellationToken ct)
            => Task.FromResult(tables[table].GetValueOrDefault(RowKey(key)));
        public Task<TableValue[][]> PageAsync(string origin, string table, TableDefinition definition, int offset, int limit, CancellationToken ct)
            => Task.FromResult(tables[table].OrderBy(p => p.Key, StringComparer.Ordinal).Skip(offset).Take(limit).Select(p => p.Value).ToArray());
    }
}
