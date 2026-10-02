using DigitalBrain.Contracts.Data;
using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Modules.Supabase.Tests.Unit;

public sealed class SupabaseRowSourceFacts
{
    [Fact]
    public async Task ReadCompilesRowQueryIntoGuardSafeSql()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new RecordingSupabaseProvider();
        await using var brain = await UnitTest.Create().ConfigureSilo(silo =>
            silo.Services.AddSingleton<ISupabaseProvider>(provider)).StartAsync(ct);
        var source = brain.Get<ISupabaseRows>("people");

        var page = await source.Read(new RowQuery { Columns = ["name"] });

        Assert.Equal("ada", Assert.Single(Assert.Single(page.Rows).Values));
        var sql = Assert.Single(provider.Sql);
        Assert.Equal(sql, SupabaseQueryGuard.Normalize(sql));
        Assert.Contains("""FROM "people" """, sql, StringComparison.Ordinal);
    }

    private sealed class RecordingSupabaseProvider : ISupabaseProvider
    {
        public List<string> Sql { get; } = [];

        public Task<SupabaseQueryResult> QueryAsync(string sql, int maxRows, CancellationToken cancellationToken)
        {
            Sql.Add(sql);
            return Task.FromResult(new SupabaseQueryResult([new("name", "text", "text")], [["\"ada\""]], 1, false, 1));
        }

        public Task<SupabaseSchema> ReadSchemaAsync(string? table, CancellationToken cancellationToken)
            => Task.FromResult(new SupabaseSchema("test", [new(table!, "BASE TABLE", 1, [new("name", "text", "text")])]));

        public Task<SupabaseConnection> PingAsync(CancellationToken cancellationToken)
            => Task.FromResult(new SupabaseConnection(true, "test", "16", "Npgsql"));

        public Task<QueryPage> ExecutePlanAsync(QueryPlan plan, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<SupabaseColumn>> DescribeAsync(string sql, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<SupabaseTableAggregate> AggregateAsync(QueryPlan plan, string function, string columnId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
