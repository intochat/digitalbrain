using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Taxi.Contracts;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Kernel.Tests;

/// <summary>
/// Slice A: when an installed neuron declares <see cref="INeuronDefinition.PlanType"/>,
/// Cortex must resolve the plan grain via <see cref="IGrainFactory"/> and call
/// <see cref="INeuronPlan.ExecuteAsync"/> instead of single-firing the
/// canonical synapse. Validates the foundation that all future multi-hop
/// neurons (taxi.ride-home, travel.recall-near-miss, …) build on.
/// </summary>
public class CortexPlanDispatchTests
{
    static readonly DomainId TaxiDomain = DomainId.From("ino.domains.taxi");
    static readonly NeuronId RideHomeId = NeuronId.From("taxi.ride-home");

    public interface IFakePlan : INeuronPlan { }

    static IDiscoveryClient DiscoveryWithPlanNeuron(Type planType)
    {
        var discovery = Substitute.For<IDiscoveryClient>();
        var neuron = new NeuronDefinition(
            Id: RideHomeId,
            DisplayName: "Ride home",
            Description: "Hail a ride to the user's inferred home.",
            CanonicalSynapseType: typeof(FindRideRequest),
            PromptExamples: Array.Empty<string>())
        {
            PlanType = planType,
        };
        discovery.DumpNeuronsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<INeuronDefinition>>(new[] { (INeuronDefinition)neuron }));
        // Cortex still verifies the canonical handler exists before plan dispatch
        // — a neuron with a plan that fires FindRideRequest still depends on
        // RideSearchNeuron being installed on its silo.
        discovery.LookupCanonicalAsync(typeof(FindRideRequest), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(
                new CanonicalTarget(typeof(FindRideRequest), typeof(object), TaxiDomain, [])));
        return discovery;
    }

    static INeuronPromptCorpus Corpus(string pattern)
    {
        var scenario = new BddScenario(
            FeatureTitle: "test",
            ScenarioName: "ride-home",
            PromptPattern: pattern,
            ReplyText: "ok",
            Tags: new[] { $"@neuron:{RideHomeId.Value}" },
            SourceFile: "inline");
        return new BddScenarioPromptCorpus(new[] { scenario });
    }

    static IChatClient NoChat()
    {
        var c = Substitute.For<IChatClient>();
        c.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))));
        return c;
    }

    static NeuronContext Ctx(IFirePort firePort, string? userId = "u1") =>
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
    public async Task Plan_dispatch_resolves_plan_grain_and_calls_ExecuteAsync()
    {
        var firePort = Substitute.For<IFirePort>();
        var grainFactory = Substitute.For<IGrainFactory>();

        // Fake plan grain captures the inbound NeuronPlanContext + returns an Ok.
        var planGrain = Substitute.For<IFakePlan>();
        NeuronPlanContext? captured = null;
        planGrain.ExecuteAsync(Arg.Do<NeuronPlanContext>(c => captured = c), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok("plan-ok"));
        grainFactory.GetGrain(typeof(IFakePlan), Arg.Any<string>())
            .Returns(planGrain);

        var cortex = new CortexNeuron(
            DiscoveryWithPlanNeuron(typeof(IFakePlan)),
            firePort,
            NoChat(),
            Corpus("ride home|take me home"),
            grainFactory,
            NullLogger<CortexNeuron>.Instance);

        var result = await cortex.HandleAsync(
            new ChatIntent("take me home", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("plan-ok", result.Message);
        Assert.NotNull(captured);
        Assert.Equal("take me home", captured!.Prompt);
        Assert.Equal(RideHomeId, captured.NeuronId);

        // Cortex must NOT fire the canonical synapse when a plan is dispatched —
        // the plan owns synapse construction (it has the resolved Pickup/Dropoff
        // from BFS).
        await firePort.DidNotReceive().Fire(
            Arg.Any<FindRideRequest>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Plan_dispatch_uses_user_id_as_grain_primary_key()
    {
        var firePort = Substitute.For<IFirePort>();
        var grainFactory = Substitute.For<IGrainFactory>();
        var planGrain = Substitute.For<IFakePlan>();
        planGrain.ExecuteAsync(Arg.Any<NeuronPlanContext>(), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok());
        grainFactory.GetGrain(typeof(IFakePlan), Arg.Any<string>())
            .Returns(planGrain);

        var cortex = new CortexNeuron(
            DiscoveryWithPlanNeuron(typeof(IFakePlan)),
            firePort,
            NoChat(),
            Corpus("home"),
            grainFactory,
            NullLogger<CortexNeuron>.Instance);

        await cortex.HandleAsync(
            new ChatIntent("home", "user-42"),
            Ctx(firePort, userId: "user-42"),
            TestContext.Current.CancellationToken);

        // Per-user keying lets stateful plans accumulate journals across calls.
        grainFactory.Received(1).GetGrain(typeof(IFakePlan), "user-42");
    }

    [Fact]
    public async Task Plan_dispatch_falls_through_when_plan_type_is_not_an_INeuronPlan()
    {
        var firePort = Substitute.For<IFirePort>();
        UnroutedIntent? broadcast = null;
        firePort.FireBroadcast(Arg.Do<UnroutedIntent>(u => broadcast = u),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Misconfigured PlanType — string is not an INeuronPlan.
        var cortex = new CortexNeuron(
            DiscoveryWithPlanNeuron(typeof(string)),
            firePort,
            NoChat(),
            Corpus("home"),
            Substitute.For<IGrainFactory>(),
            NullLogger<CortexNeuron>.Instance);

        var result = await cortex.HandleAsync(
            new ChatIntent("home", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        // Cortex logs an error and falls through — does not crash, does not fire.
        Assert.True(result.Success);
        Assert.IsType<UnroutedIntent>(result.ResponsePayload);
        Assert.NotNull(broadcast);
    }
}
