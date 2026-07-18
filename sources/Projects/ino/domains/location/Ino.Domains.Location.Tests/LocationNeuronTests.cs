using Ino.Core.Hosting;
using Ino.Domains.Location.Contracts;
using Ino.Testing;
using Xunit;

namespace Ino.Domains.Location.Tests;

/// <summary>
/// Slice B: <see cref="ILocationNeuron"/> is the per-user location journal that
/// plans across other domains read for "home", "current location", and (later)
/// frequency-based anchor inference. These tests verify the basic
/// record + read pipeline against the in-memory test silo, plus the cross-grain
/// readability via <see cref="IJournaledNeuronQuery{TEvent}"/> that
/// <see cref="TraversalEngine.VisitAsync"/> depends on.
/// </summary>
[Collection(nameof(InoTestCollection))]
public sealed class LocationNeuronTests
{
    private readonly InoTestSiloFixture _fixture;

    public LocationNeuronTests(InoTestSiloFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RecordAsync_appends_LocationVisited_to_user_journal()
    {
        var userId = $"user-{Guid.NewGuid():n}";
        var loc = _fixture.Grains.GetGrain<ILocationNeuron>(userId);

        await loc.RecordAsync("221B Baker Street, London", "home", Guid.NewGuid().ToString("n"));

        var history = await loc.GetHistoryAsync();
        Assert.Single(history);
        Assert.Equal("221B Baker Street, London", history[0].Place);
        Assert.Equal("home", history[0].Label);
    }

    [Fact]
    public async Task Multiple_visits_append_in_order()
    {
        var userId = $"user-{Guid.NewGuid():n}";
        var loc = _fixture.Grains.GetGrain<ILocationNeuron>(userId);
        var corr = Guid.NewGuid().ToString("n");

        await loc.RecordAsync("Office", "office", corr);
        await loc.RecordAsync("Cafe Luxembourg", null, corr);
        await loc.RecordAsync("221B Baker Street", "home", corr);

        var history = await loc.GetHistoryAsync();
        Assert.Equal(3, history.Count);
        Assert.Equal("Office", history[0].Place);
        Assert.Equal("Cafe Luxembourg", history[1].Place);
        Assert.Equal("221B Baker Street", history[2].Place);
    }

    [Fact]
    public async Task Per_user_isolation_two_users_have_distinct_journals()
    {
        var alice = _fixture.Grains.GetGrain<ILocationNeuron>($"alice-{Guid.NewGuid():n}");
        var bob = _fixture.Grains.GetGrain<ILocationNeuron>($"bob-{Guid.NewGuid():n}");
        var corr = Guid.NewGuid().ToString("n");

        await alice.RecordAsync("Wonderland", "home", corr);
        await bob.RecordAsync("Castle", "home", corr);

        var aliceHistory = await alice.GetHistoryAsync();
        var bobHistory = await bob.GetHistoryAsync();

        Assert.Single(aliceHistory);
        Assert.Single(bobHistory);
        Assert.Equal("Wonderland", aliceHistory[0].Place);
        Assert.Equal("Castle", bobHistory[0].Place);
    }

    [Fact]
    public async Task Journal_is_readable_via_typed_IJournaledNeuronQuery_interface()
    {
        // This is what TraversalEngine.VisitAsync<LocationVisited> resolves to
        // when a plan reads cross-silo. Verifying the closed-generic interface
        // routes to the same grain pins the contract LocationNeuron exposes
        // to the BFS engine.
        var userId = $"user-{Guid.NewGuid():n}";
        var loc = _fixture.Grains.GetGrain<ILocationNeuron>(userId);
        await loc.RecordAsync("Home", "home", Guid.NewGuid().ToString("n"));

        var query = _fixture.Grains.GetGrain<IJournaledNeuronQuery<LocationVisited>>(userId);
        var history = await query.GetHistoryWithMetadataAsync();

        Assert.Single(history);
        Assert.Equal("Home", history[0].Payload.Place);
        Assert.Equal("home", history[0].Payload.Label);
    }
}
