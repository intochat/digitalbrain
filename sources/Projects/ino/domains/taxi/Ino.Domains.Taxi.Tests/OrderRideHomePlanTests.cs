using Ino.Core;
using Ino.Core.Hosting;
using Ino.Domains.Location.Contracts;
using Ino.Domains.Taxi.Contracts;
using Ino.Domains.Taxi.Plans;
using Ino.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ino.Domains.Taxi.Tests;

/// <summary>
/// Slice B: <see cref="OrderRideHomePlan"/> is the first multi-hop plan —
/// resolves "home" + current pickup from the user's location journal, then
/// fires <see cref="FindRideRequest"/>. These tests exercise the BFS body
/// against a real <see cref="TraversalEngine"/> backed by the test silo's
/// <see cref="ILocationNeuron"/>, with <see cref="IFirePort"/> stubbed so the
/// test asserts on the synapse the plan emits.
///
/// The plan grain wiring (Cortex → IGrainFactory.GetGrain → ExecuteAsync) is
/// covered in the Slice A <c>CortexPlanDispatchTests</c>; here we only test
/// what the plan *does* once invoked.
/// </summary>
[Collection(nameof(InoTestCollection))]
public sealed class OrderRideHomePlanTests
{
    private readonly InoTestSiloFixture _fixture;

    public OrderRideHomePlanTests(InoTestSiloFixture fixture)
    {
        _fixture = fixture;
    }

    static NeuronContext BuildContext(IFirePort firePort, string userId) =>
        new(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("kernel")),
            SourceStream: new StreamKey("<gateway>"),
            UserId: userId)
        {
            FirePort = firePort,
            Logger = NullLogger.Instance,
        };

    [Fact]
    public async Task When_no_home_anchor_returns_friendly_clarification_without_firing()
    {
        var userId = $"user-{Guid.NewGuid():n}";
        var firePort = Substitute.For<IFirePort>();
        var ctx = BuildContext(firePort, userId);
        var engine = new TraversalEngine(_fixture.Grains, firePort, ctx);

        var result = await OrderRideHomePlan.ExecuteAsync(
            userId, engine, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("home", result.Message, StringComparison.OrdinalIgnoreCase);
        // The whole point of "ride home" is to NOT bother the user with
        // pickup/dropoff prompts — but if home is unknown we'd produce a
        // FindRideRequest with empty Dropoff which is worse than silently
        // asking.
        await firePort.DidNotReceive().Fire(
            Arg.Any<FindRideRequest>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task When_home_anchor_set_fires_FindRideRequest_with_resolved_endpoints()
    {
        var userId = $"user-{Guid.NewGuid():n}";
        var corr = Guid.NewGuid().ToString("n");
        var loc = _fixture.Grains.GetGrain<ILocationNeuron>(userId);
        await loc.RecordAsync("221B Baker Street, London", "home", corr);
        await loc.RecordAsync("Office, Soho", "office", corr);
        await loc.RecordAsync("Cafe Luxembourg", null, corr);

        var firePort = Substitute.For<IFirePort>();
        FindRideRequest? captured = null;
        firePort.Fire(Arg.Do<FindRideRequest>(r => captured = r),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok("ride-narrated"));

        var ctx = BuildContext(firePort, userId);
        var engine = new TraversalEngine(_fixture.Grains, firePort, ctx);

        var result = await OrderRideHomePlan.ExecuteAsync(
            userId, engine, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("ride-narrated", result.Message);
        Assert.NotNull(captured);
        // Pickup = most-recent visit. Dropoff = explicit "home" anchor.
        // BFS picked the right slots from the journal.
        Assert.Equal("Cafe Luxembourg", captured!.Pickup);
        Assert.Equal("221B Baker Street, London", captured.Dropoff);
    }

    [Fact]
    public async Task Most_recent_home_anchor_wins_when_user_has_re_anchored()
    {
        // User moved — the journal has two "home" entries. The plan must use
        // the latest, not the first. Demonstrates that "memory IS synapses"
        // gives us free temporal correctness without a separate "current home"
        // store.
        var userId = $"user-{Guid.NewGuid():n}";
        var corr = Guid.NewGuid().ToString("n");
        var loc = _fixture.Grains.GetGrain<ILocationNeuron>(userId);
        await loc.RecordAsync("Old Address", "home", corr);
        await loc.RecordAsync("New Address", "home", corr);

        var firePort = Substitute.For<IFirePort>();
        FindRideRequest? captured = null;
        firePort.Fire(Arg.Do<FindRideRequest>(r => captured = r),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok());

        var ctx = BuildContext(firePort, userId);
        var engine = new TraversalEngine(_fixture.Grains, firePort, ctx);

        await OrderRideHomePlan.ExecuteAsync(
            userId, engine, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("New Address", captured!.Dropoff);
    }

    [Fact]
    public async Task When_home_anchor_exists_but_no_recent_visits_uses_fallback_pickup()
    {
        // Edge case: only event in the journal is the home anchor itself.
        // Pickup should still be defined — fall back to the home address (it's
        // the most recent visit) so the ride request is well-formed.
        var userId = $"user-{Guid.NewGuid():n}";
        var corr = Guid.NewGuid().ToString("n");
        var loc = _fixture.Grains.GetGrain<ILocationNeuron>(userId);
        await loc.RecordAsync("Home Place", "home", corr);

        var firePort = Substitute.For<IFirePort>();
        FindRideRequest? captured = null;
        firePort.Fire(Arg.Do<FindRideRequest>(r => captured = r),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok());

        var ctx = BuildContext(firePort, userId);
        var engine = new TraversalEngine(_fixture.Grains, firePort, ctx);

        await OrderRideHomePlan.ExecuteAsync(
            userId, engine, NullLogger.Instance, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("Home Place", captured!.Pickup);
        Assert.Equal("Home Place", captured.Dropoff);
    }
}
