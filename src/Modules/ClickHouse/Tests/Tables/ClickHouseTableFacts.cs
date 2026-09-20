using DigitalBrain.ClickHouse;
using DigitalBrain.ClickHouse.Tables;
using DigitalBrain.ClickHouse.Tables.Signals;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ClickHouseTableFacts
{
    private const string LeadSql = "SELECT id, name FROM leads";

    [Fact]
    public async Task CreateFromQueryRendersAndServesLiveRows()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new FakeClickHouseProvider();
        await using var brain = await Start(provider, ct);
        var table = brain.Get<IClickHouseTable>(ClickHouseNames.TableIdPrefix + "leads");
        await using var rendered = await brain.Observe<TableRendered>(table, ct);

        var view = await table.CreateFromQuery(new("Leads", LeadSql));

        Assert.Equal(1, view.Revision);
        Assert.Equal(["id", "name"], view.Columns.Select(column => column.Id));
        var signal = await rendered.NextAsync(ct: ct);
        Assert.Equal("Leads", signal.Title);

        var snapshot = await table.Read(new(0, 50));
        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.Rows.Count);
        Assert.Equal(2, snapshot.TotalRows);
        Assert.Equal(LeadSql, provider.LastPlan!.BaseSql);
    }

    [Fact]
    public async Task UpdateIncrementsRevisionPublishesAndForwardsFilters()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new FakeClickHouseProvider();
        await using var brain = await Start(provider, ct);
        var table = brain.Get<IClickHouseTable>(ClickHouseNames.TableIdPrefix + "leads");
        await table.CreateFromQuery(new("Leads", LeadSql));
        await using var rendered = await brain.Observe<TableRendered>(table, ct);

        var updated = await table.Update(new(1,
            [new ClickHouseTableFilter("name", "eq", "\"alpha\"")], null, ["id", "name"]));

        Assert.Equal(2, updated.Revision);
        Assert.Equal("Leads", (await rendered.NextAsync(ct: ct)).Title);
        var snapshot = await table.Read(new(0, 50));
        Assert.Equal(1, snapshot!.FilteredRows);
        Assert.Equal("name", Assert.Single(provider.LastPlan!.Filters).ColumnId);
    }

    [Fact]
    public async Task InvalidCreateAndStaleRevisionAreRejected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FakeClickHouseProvider(), ct);
        var table = brain.Get<IClickHouseTable>(ClickHouseNames.TableIdPrefix + "leads");

        await Assert.ThrowsAsync<ClickHouseTableValidationException>(() => table.CreateFromQuery(new("Leads", "DELETE FROM leads")));
        await table.CreateFromQuery(new("Leads", LeadSql));
        await Assert.ThrowsAsync<ClickHouseTableValidationException>(() => table.CreateFromQuery(new("Again", LeadSql)));
        await Assert.ThrowsAsync<ClickHouseTableValidationException>(() => table.Update(new(99, [], null, ["id"])));
        var summary = await table.ReadSummary();
        Assert.Equal(1, summary!.Revision);
    }

    [Fact]
    public async Task RenderRepublishesWithoutChangingRevision()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FakeClickHouseProvider(), ct);
        var table = brain.Get<IClickHouseTable>(ClickHouseNames.TableIdPrefix + "leads");
        await table.CreateFromQuery(new("Leads", LeadSql));
        await using var rendered = await brain.Observe<TableRendered>(table, ct);

        var summary = await table.Render();

        Assert.Equal(1, summary!.Revision);
        Assert.Equal(ClickHouseNames.TableIdPrefix + "leads", (await rendered.NextAsync(ct: ct)).Name);
    }

    private static Task<UnitBrain> Start(FakeClickHouseProvider provider, CancellationToken ct)
        => UnitTest.Create().WithModule<ClickHouseModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IClickHouseProvider>(provider))
            .StartAsync(ct);
}
