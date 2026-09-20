using DigitalBrain.Supabase;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SupabaseQueryFacts
{
    [Fact]
    public async Task QueryReturnsTypedRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(new FakeSupabaseProvider(), ct);
        var supabase = brain.Get<ISupabase>(SupabaseNames.DefaultNeuron);

        var result = await supabase.Query(new("select id from people"));

        Assert.Equal(1, result.RowCount);
        Assert.False(result.Truncated);
        Assert.Equal("id", Assert.Single(result.Columns).Name);
        Assert.Equal("7", Assert.Single(Assert.Single(result.Rows)));
    }

    [Fact]
    public async Task ReadConnectionReportsTheProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(new FakeSupabaseProvider(), ct);
        var supabase = brain.Get<ISupabase>(SupabaseNames.DefaultNeuron);

        var connection = await supabase.ReadConnection();

        Assert.True(connection.Connected);
        Assert.Equal("Npgsql", connection.Provider);
    }

    [Fact]
    public async Task BlankOrWriteSqlIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(new FakeSupabaseProvider(), ct);
        var supabase = brain.Get<ISupabase>(SupabaseNames.DefaultNeuron);

        await Assert.ThrowsAsync<SupabaseQueryException>(() => supabase.Query(new("   ")));
        await Assert.ThrowsAsync<SupabaseQueryException>(() => supabase.Query(new("delete from people")));
        await Assert.ThrowsAsync<SupabaseQueryException>(() => supabase.Query(new("select 1", 0)));
    }

    private static Task<UnitBrain> StartAsync(FakeSupabaseProvider provider, CancellationToken ct)
        => UnitTest.StartAsync(new()
        {
            Modules = [new DigitalBrain.Core.ModuleDefinition(typeof(SupabaseModule))],
            ConfigureSilo = silo => silo.Services.AddSingleton<ISupabaseProvider>(provider),
        }, ct);
}