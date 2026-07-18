using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Travel.Contracts;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Kernel.Tests;

/// <summary>
/// Verifies slice 15 wiring — Cortex calls BddMockChatClient with the resolved
/// target neuron id and the matched scenario lands on IReasoningProbe under
/// that id. This is what the inspector Reasoning panel reads to show
/// "mocked via BDD · {scenario}".
/// </summary>
public class CortexReasoningAnnotationTests
{
    static readonly DomainId TravelDomain = DomainId.From("ino.domains.travel");

    sealed class FlightSearchStub { }

    public interface IFakeFindFlightsPlan : INeuronPlan { }

    static readonly NeuronId FindFlightsId = NeuronId.From("travel.find-flights");

    static BddScenario Scenario() => new(
        FeatureTitle: "Travel — intent routing",
        ScenarioName: "Find flights",
        PromptPattern: "find.*flight",
        ReplyText: "Searching flights via the FlightSearch neuron.",
        // Tag the scenario for the corpus too — Cortex's regex fast-path
        // walks neurons, looks up patterns by NeuronId, and only
        // then annotates via the BDD mock.
        Tags: new[] { "@neuron:travel.find-flights" },
        SourceFile: "inline");

    static INeuronDefinition FindFlightsNeuronDefinition() => new NeuronDefinition(
        Id: FindFlightsId,
        DisplayName: "Find flights",
        Description: "Search flights between two cities.",
        CanonicalSynapseType: typeof(FindFlightsRequest),
        PromptExamples: Array.Empty<string>())
    {
        // Phase 4 Slice A: every routable neuron needs a PlanType.
        PlanType = typeof(IFakeFindFlightsPlan),
    };

    static IGrainFactory GrainFactoryWithStubPlan(NeuronResult result)
    {
        var grainFactory = Substitute.For<IGrainFactory>();
        var plan = Substitute.For<IFakeFindFlightsPlan>();
        plan.ExecuteAsync(Arg.Any<NeuronPlanContext>(), Arg.Any<CancellationToken>())
            .Returns(result);
        grainFactory.GetGrain(typeof(IFakeFindFlightsPlan), Arg.Any<string>())
            .Returns(plan);
        return grainFactory;
    }

    [Fact]
    public async Task Cortex_annotates_reasoning_probe_under_resolved_target_grain_type()
    {
        var discovery = Substitute.For<IDiscoveryClient>();
        var flightTarget = new CanonicalTarget(
            typeof(FindFlightsRequest), typeof(FlightSearchStub), TravelDomain, Array.Empty<Capability>());
        discovery.LookupCanonicalAsync(typeof(FindFlightsRequest), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(flightTarget));
        discovery.DumpNeuronsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<INeuronDefinition>>(new[] { FindFlightsNeuronDefinition() }));

        var firePort = Substitute.For<IFirePort>();

        var probe = new InMemoryReasoningProbe();
        var chat = new BddMockChatClient(new[] { Scenario() }, probe);

        var corpus = new BddScenarioPromptCorpus(new[] { Scenario() });
        var cortex = new CortexNeuron(
            discovery, firePort, chat, corpus,
            GrainFactoryWithStubPlan(NeuronResult.Ok("flights-ok")),
            NullLogger<CortexNeuron>.Instance);

        var ctx = new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("kernel")),
            SourceStream: new StreamKey("<gateway>"))
        {
            FirePort = firePort,
            Logger = NullLogger.Instance,
        };

        var result = await cortex.HandleAsync(
            new ChatIntent("find me a flight to Bali", "u1"),
            ctx,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);

        var targetId = typeof(FlightSearchStub).FullName!;
        // Cortex must record the BDD scenario under the discovered target grain type so the inspector panel can read it
        Assert.True(probe.TryGet(targetId, out var hit));
        Assert.Equal("Find flights", hit.ScenarioName);
        Assert.Equal("Travel — intent routing", hit.FeatureTitle);
        Assert.Equal("bdd-mock", hit.Source);
    }

    [Fact]
    public async Task Missing_scenario_does_not_fail_the_route()
    {
        var discovery = Substitute.For<IDiscoveryClient>();
        var flightTarget = new CanonicalTarget(
            typeof(FindFlightsRequest), typeof(FlightSearchStub), TravelDomain, Array.Empty<Capability>());
        discovery.LookupCanonicalAsync(typeof(FindFlightsRequest), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(flightTarget));
        discovery.DumpNeuronsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<INeuronDefinition>>(new[] { FindFlightsNeuronDefinition() }));

        var firePort = Substitute.For<IFirePort>();

        // Empty scenario list — BddMockChatClient will throw on any prompt.
        var probe = new InMemoryReasoningProbe();
        var chat = new BddMockChatClient(Array.Empty<BddScenario>(), probe);

        var corpus = new BddScenarioPromptCorpus(new[] { Scenario() });
        var cortex = new CortexNeuron(
            discovery, firePort, chat, corpus,
            GrainFactoryWithStubPlan(NeuronResult.Ok("flights-ok")),
            NullLogger<CortexNeuron>.Instance);
        var ctx = new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("kernel")),
            SourceStream: new StreamKey("<gateway>"))
        {
            FirePort = firePort,
            Logger = NullLogger.Instance,
        };

        // BddMockMissException must not propagate — routing stays keyword-based.
        var result = await cortex.HandleAsync(
            new ChatIntent("find me a flight to Bali", "u1"),
            ctx,
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("flights-ok", result.Message);
        Assert.Empty(probe.KnownNeurons());
    }
}
