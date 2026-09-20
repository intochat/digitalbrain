using DigitalBrain.Supabase;
using DigitalBrain.Supabase.Tables;
using DigitalBrain.Supabase.Tables.Signals;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class SupabaseTableFacts
{
    [Fact]
    public async Task CreateFromQuerySavesViewAndPublishes()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(new FakeSupabaseProvider(), ct);
        var table = brain.Get<ISupabaseTable>("sbtable-1");
        await using var changed = await brain.Observe<SupabaseTableChanged>(table, ct);

        var snapshot = await table.CreateFromQuery(new("People", "select id from people"));

        Assert.Equal("sbtable-1", snapshot.Id);
        Assert.Equal("People", snapshot.Title);
        Assert.Equal(1, snapshot.Revision);
        Assert.Empty(snapshot.Rows);
        Assert.Equal("id", Assert.Single(snapshot.Columns).Id);
        var published = await changed.NextAsync(ct: ct);
        Assert.Equal("sbtable-1", published.Name);
        Assert.Equal(1, published.Revision);
        Assert.Equal("People", (await table.ReadSummary())!.Title);
    }

    [Fact]
    public async Task ReadServesLiveRowsFromTheProvider()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(new FakeSupabaseProvider(), ct);
        var table = brain.Get<ISupabaseTable>("sbtable-2");
        await table.CreateFromQuery(new("People", "select id from people"));

        var page = await table.Read(new(0, 50));

        Assert.NotNull(page);
        Assert.Equal(1, page!.TotalRows);
        Assert.Equal(1, page.FilteredRows);
        Assert.Equal("row-0", Assert.Single(page.Rows).Id);
    }

    [Fact]
    public async Task UpdateViewIncrementsRevisionAndPublishes()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new FakeSupabaseProvider();
        await using var brain = await StartAsync(provider, ct);
        var table = brain.Get<ISupabaseTable>("sbtable-3");
        await table.CreateFromQuery(new("People", "select id from people"));
        await using var changed = await brain.Observe<SupabaseTableChanged>(table, ct);

        var updated = await table.UpdateView(new(1, [new SupabaseTableFilter("id", "eq", "7")], null, ["id"]));

        Assert.Equal(2, updated.Revision);
        Assert.Equal("id", Assert.Single(updated.VisibleColumns));
        Assert.Equal(2, (await changed.NextAsync(ct: ct)).Revision);
        await table.Read(new(0, 50));
        Assert.Equal("id", Assert.Single(provider.LastPlan!.Filters).ColumnId);
    }

    [Fact]
    public async Task MissingTableReadsNullAndUpdateFails()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(new FakeSupabaseProvider(), ct);
        var table = brain.Get<ISupabaseTable>("sbtable-missing");

        Assert.Null(await table.Read(new(0, 50)));
        await Assert.ThrowsAsync<SupabaseTableNotFoundException>(() => table.UpdateView(new(1, [], null, ["id"])));
    }

    [Fact]
    public async Task InvalidCreateIsRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await StartAsync(new FakeSupabaseProvider(), ct);
        var table = brain.Get<ISupabaseTable>("sbtable-invalid");

        await Assert.ThrowsAsync<SupabaseTableValidationException>(() => table.CreateFromQuery(new("", "select id")));
        await Assert.ThrowsAsync<SupabaseTableValidationException>(() => table.CreateFromQuery(new("People", "delete from people")));
    }

    private static Task<UnitBrain> StartAsync(FakeSupabaseProvider provider, CancellationToken ct)
        => UnitTest.StartAsync(new()
        {
            Modules = [new DigitalBrain.Core.ModuleDefinition(typeof(SupabaseModule))],
            ConfigureSilo = silo => silo.Services.AddSingleton<ISupabaseProvider>(provider),
        }, ct);
}