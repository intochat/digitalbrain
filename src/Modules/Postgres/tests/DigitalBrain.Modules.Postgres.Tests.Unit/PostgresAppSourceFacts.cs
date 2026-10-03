using DigitalBrain.Postgres;
using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;
using DigitalBrain.Contracts.Enforcement;
using DigitalBrain.Kernel.Enforcement;
using DigitalBrain.Sdk.Capacity;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using DigitalBrain.Supabase.Windows;

namespace DigitalBrain.Modules.Postgres.Tests.Unit;

public sealed class PostgresAppSourceFacts
{
    [Fact]
    public async Task PendingDefinitionDoesNotHideHealthyTablesInAnotherApp()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new PostgresWriteTableFacts.MemoryTables { LoseDefineResponse = true };
        await using var brain = await UnitTest.Create().WithModule<PostgresModule>().ConfigureSilo(silo =>
        {
            silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=platform;Username=reader";
            silo.Services.AddSingleton<IPostgresTableProvider>(provider);
            silo.Services.AddSingleton<ICapacity>(new ResearchCapacity());
        }).StartAsync(ct);
        Stamp(CallerKind.App);
        await Assert.ThrowsAsync<IOException>(() => brain.Get<IPostgresTable>("pending-discovery").Define(new([new("id", "text")], ["id"])));
        var scope = BrainScope.CurrentId();
        CallerContextStamper.Stamp(new() { PrincipalId = "alice", AccountId = "alice", BrainId = "brain",
            Kind = CallerKind.App, AppId = "healthy-install", StampedBy = TrustedEdge.AppProxy });
        await brain.Get<IPostgresTable>("healthy-discovery").Define(new([new("id", "text")], ["id"]));
        Stamp(CallerKind.Platform);
        var result = await new PostgresAppResources(brain).CollectInstalled(["install", "healthy-install"], ct);
        Assert.Equal("healthy-discovery", Assert.Single(result.Resources).TableId);
        var failure = Assert.Single(result.Errors);
        Assert.Equal("install", failure.AppId);
        Assert.Equal("pending-discovery", failure.TableId);
        Assert.Contains("not finished", failure.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<PostgresQueryException>(() => brain.Get<IPostgresTableResource>("pending-discovery")
            .DescribeResource(JsonSerializer.Serialize(new[] { scope, "install" })));
    }

    [Fact]
    public async Task AnUnavailablePinnedSourceReturnsAnActionableWindowError()
    {
        var resource = new PostgresAppTableResource("install", "results", "db:research", new("db_results", new([new("name", "text")], ["name"]), 1));
        var app = new PostgresAppLiveSource(() => Task.FromResult(resource), _ => throw new PostgresUnavailableException("This table's database is not reachable from this deployment."));
        var error = await Assert.ThrowsAsync<SupabaseTableSourceException>(() => app.DescribeAsync(PostgresAppLiveSource.Select(resource), TestContext.Current.CancellationToken));
        Assert.Contains("not reachable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppWindowsSupportThePostgresReadAndRefinePolicy()
    {
        var ct = TestContext.Current.CancellationToken;
        const string resource = "postgres-app:discovered-resource";
        await using var brain = await UnitTest.Create().WithModule<DigitalBrain.Flutter.FlutterModule>().WithModule<PostgresModule>()
            .ConfigureSilo(silo =>
            {
                silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=sample;Username=reader";
                silo.Services.AddKeyedSingleton<ILiveTableSource>(resource, new RecordingSource());
            }).StartAsync(ct);
        var windows = new LiveTableWindows(brain);
        var opened = await windows.OpenAsync("workspace", "run", "app-table", "Results", "SELECT name FROM results", ct, resource);
        await windows.ReadAsync("workspace", opened.TableId, null, null, null, 25, ct, "postgres");
        await windows.RefineAsync("workspace", opened.TableId, [], null, null, ct, "postgres");
        await Assert.ThrowsAsync<SupabaseTableValidationException>(() => windows.ReadAsync("workspace", opened.TableId, null, null, null, 25, ct, "supabase"));
    }

    [Fact]
    public async Task HostDiscoveryRetainsPinnedOriginAndRejectsAppAndOtherBrainCallers()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await UnitTest.Create().WithModule<PostgresModule>().ConfigureSilo(silo =>
        {
            silo.Configuration["ConnectionStrings:postgres"] = "Host=localhost;Database=platform;Username=reader";
            silo.Services.AddSingleton<IPostgresTableProvider>(new PostgresWriteTableFacts.MemoryTables());
            silo.Services.AddSingleton<ICapacity>(new ResearchCapacity());
        }).StartAsync(ct);
        Stamp(CallerKind.App);
        var table = brain.Get<IPostgresTable>("research-resource");
        await table.Define(new([new("id", "text")], ["id"]));
        var owner = JsonSerializer.Serialize(new[] { BrainScope.CurrentId(), "install" });
        var resource = brain.Get<IPostgresTableResource>("research-resource");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resource.DescribeResource(owner));
        Stamp(CallerKind.Platform);
        var discovered = await resource.DescribeResource(owner);
        Assert.Equal("db:research", discovered.Origin);
        Assert.Equal("install", discovered.AppId);
        Assert.Equal(["research-resource"], await brain.Get<IPostgresAppStorage>(owner).ReadTables());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resource.DescribeResource(JsonSerializer.Serialize(new[] { BrainScope.CurrentId(), "other-install" })));
        Stamp(CallerKind.Platform, "other-brain");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => resource.DescribeResource(owner));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => brain.Get<IPostgresAppStorage>(owner).ReadTables());
    }

    private static void Stamp(CallerKind kind, string brain = "brain")
        => CallerContextStamper.Stamp(new() { PrincipalId = "alice", AccountId = "alice", BrainId = brain,
            Kind = kind, AppId = kind == CallerKind.App ? "install" : null,
            StampedBy = kind == CallerKind.App ? TrustedEdge.AppProxy : TrustedEdge.Platform });

    private sealed class ResearchCapacity : ICapacity
    {
        public ValueTask<ResolvedCapacity> Resolve(string kind, CapacityScope scope, CancellationToken ct = default)
            => ValueTask.FromResult(new ResolvedCapacity(kind, "db:research"));
        public ValueTask<ResolvedCapacity> Provision(string kind, CapacityScope scope, CancellationToken ct = default)
            => throw new InvalidOperationException();
    }

    [Fact]
    public async Task ProvisionedResourceRoutesEveryOperationToPinnedOrigin()
    {
        var ct = TestContext.Current.CancellationToken;
        var origins = new List<string>();
        var source = new RecordingSource();
        var resource = new PostgresAppTableResource("install", "results", "db:research", new("db_results", new([new("name", "text")], ["name"]), 1));
        var resolutions = 0;
        var app = new PostgresAppLiveSource(async () => { resolutions++; await Task.Yield(); return resource; }, origin => { origins.Add(origin); return source; });
        var sql = PostgresAppLiveSource.Select(resource);
        var columns = await app.DescribeAsync(sql, ct);
        var plan = new QueryPlan(sql, columns, [], null, 0, 25);
        await app.ExecutePlanAsync(plan, ct);
        await app.AggregateAsync(plan, "count", "", ct);
        Assert.Equal(3, resolutions);
        Assert.Equal(["db:research", "db:research", "db:research"], origins);
        Assert.Equal(1, source.Pages);
    }

    [Fact]
    public async Task ResourceNeverAcceptsAnArbitraryTableOrOriginThroughSql()
    {
        var resource = new PostgresAppTableResource("install", "results", "db:research", new("db_results", new([new("name", "text")], ["name"]), 1));
        var source = new RecordingSource();
        var app = new PostgresAppLiveSource(() => Task.FromResult(resource), _ => source);
        await Assert.ThrowsAsync<SupabaseTableValidationException>(() => app.DescribeAsync("SELECT name FROM someone_else", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<SupabaseTableValidationException>(() => app.ExecutePlanAsync(new("SELECT name FROM someone_else", [], [], null, 0, 25), TestContext.Current.CancellationToken));
        Assert.Equal(0, source.Pages);
    }

    [Fact]
    public async Task RevokedOwnershipFailsBeforeTouchingTheSource()
    {
        var touched = false;
        var app = new PostgresAppLiveSource(() => throw new UnauthorizedAccessException("This table belongs to another brain or app."), _ => { touched = true; return new RecordingSource(); });
        await Assert.ThrowsAsync<SupabaseTableValidationException>(() => app.DescribeAsync("SELECT name FROM db_results", TestContext.Current.CancellationToken));
        Assert.False(touched);
    }

    private sealed class RecordingSource : ILiveTableSource
    {
        public int Pages;
        public Task<IReadOnlyList<SupabaseColumn>> DescribeAsync(string sql, CancellationToken ct) => Task.FromResult<IReadOnlyList<SupabaseColumn>>([new("name", "text", "text")]);
        public Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken ct) { Pages++; return Task.FromResult(new QueryPage([], 0, 0)); }
        public Task<SupabaseTableAggregate> AggregateAsync(QueryPlan plan, string function, string columnId, CancellationToken ct = default) => Task.FromResult(new SupabaseTableAggregate(columnId, function, "0"));
    }
}
