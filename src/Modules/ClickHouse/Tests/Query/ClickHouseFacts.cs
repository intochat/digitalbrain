using DigitalBrain.ClickHouse;
using DigitalBrain.ClickHouse.Query;
using DigitalBrain.Testing.Unit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ClickHouseFacts
{
    [Fact]
    public async Task QueryReturnsTypedRows()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FakeClickHouseProvider(), ct);
        var clickHouse = brain.Get<IClickHouse>(ClickHouseNames.DefaultNeuron);

        var result = await clickHouse.Query(new("SELECT count() AS value FROM companies"), ct);

        Assert.Equal(1, result.RowCount);
        var column = Assert.Single(result.Columns);
        Assert.Equal("value", column.Name);
        Assert.Equal("42", Assert.Single(Assert.Single(result.Rows)));
    }

    [Fact]
    public async Task QueryRejectsWritesAndBadBounds()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var brain = await Start(new FakeClickHouseProvider(), ct);
        var clickHouse = brain.Get<IClickHouse>(ClickHouseNames.DefaultNeuron);

        await Assert.ThrowsAsync<ClickHouseQueryException>(() => clickHouse.Query(new("DROP TABLE companies"), ct));
        await Assert.ThrowsAsync<ClickHouseQueryException>(() => clickHouse.Query(new("   "), ct));
        await Assert.ThrowsAsync<ClickHouseQueryException>(() => clickHouse.Query(new("SELECT 1", 0), ct));
        await Assert.ThrowsAsync<ClickHouseQueryException>(() => clickHouse.Query(new("SELECT 1", ClickHouseQuery.MaxRowsLimit + 1), ct));
    }

    [Fact]
    public void GuardRefusesCommentsAndExternalTableFunctions()
    {
        Assert.Throws<ArgumentException>(() => ClickHouseQueryGuard.Validate("SELECT 1 -- trailing"));
        Assert.Throws<ArgumentException>(() => ClickHouseQueryGuard.Validate("SELECT * FROM url('http://example/x')"));
        Assert.Throws<ArgumentException>(() => ClickHouseQueryGuard.Validate("SELECT 1; SELECT 2"));
        ClickHouseQueryGuard.Validate("WITH x AS (SELECT 1) SELECT * FROM x");
    }

    [Fact]
    public async Task ReadConnectionReportsProviderState()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new FakeClickHouseProvider();
        await using var brain = await Start(provider, ct);
        var clickHouse = brain.Get<IClickHouse>(ClickHouseNames.DefaultNeuron);

        var connection = await clickHouse.ReadConnection(ct);

        Assert.True(connection.Connected);
        Assert.Equal("digitalbrain", connection.Database);
        Assert.Equal("Fake", connection.Provider);
    }

    private static Task<UnitBrain> Start(FakeClickHouseProvider provider, CancellationToken ct)
        => UnitTest.Create().WithModule<ClickHouseModule>()
            .ConfigureSilo(silo => silo.Services.AddSingleton<IClickHouseProvider>(provider))
            .StartAsync(ct);
}
