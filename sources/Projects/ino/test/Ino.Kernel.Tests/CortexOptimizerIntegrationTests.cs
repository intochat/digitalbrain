using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Core.Hosting.ML;
using Ino.Domains.Travel.Contracts;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Kernel.Tests;

/// <summary>
/// Phase 4 Slice D.2: Cortex consults a per-user
/// <see cref="INeuronOptimizer"/> before invoking the LLM classifier; when
/// the model is confident the prompt won't route, the LLM call is
/// skipped to save tokens. Records every routing decision back to the
/// optimizer so the model evolves with the user's history.
///
/// Optimizer failures (transport blips, missing grain in non-IAW silos)
/// MUST NOT break routing — these tests pin the defensive try/catch
/// behaviour explicitly.
/// </summary>
public class CortexOptimizerIntegrationTests
{
    static readonly DomainId TravelDomain = DomainId.From("ino.domains.travel");
    static readonly NeuronId FindFlightsId = NeuronId.From("travel.find-flights");

    public interface IFakeFindFlightsPlan : INeuronPlan { }

    static IChatClient TrackedChat(out int callCount)
    {
        var counter = new[] { 0 };
        var c = Substitute.For<IChatClient>();
        c.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                counter[0]++;
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));
            });
        callCount = counter[0];
        return c;
    }

    static int CountChatCalls(IChatClient client) =>
        client.ReceivedCalls()
            .Count(c => c.GetMethodInfo().Name == nameof(IChatClient.GetResponseAsync));

    static IDiscoveryClient DiscoveryWith(Type synapse, Type plan)
    {
        var discovery = Substitute.For<IDiscoveryClient>();
        var neuron = new NeuronDefinition(
            Id: FindFlightsId,
            DisplayName: "flights",
            Description: "find flights",
            CanonicalSynapseType: synapse,
            PromptExamples: Array.Empty<string>())
        {
            PlanType = plan,
        };
        discovery.DumpNeuronsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<INeuronDefinition>>(new[] { (INeuronDefinition)neuron }));
        discovery.LookupCanonicalAsync(synapse, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(
                new CanonicalTarget(synapse, typeof(object), TravelDomain, [])));
        return discovery;
    }

    static INeuronPromptCorpus EmptyCorpus() => new BddScenarioPromptCorpus(Array.Empty<BddScenario>());

    static NeuronContext Ctx(IFirePort firePort) =>
        new(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("kernel")),
            SourceStream: new StreamKey("<gateway>"),
            UserId: "u1")
        {
            FirePort = firePort,
            Logger = NullLogger.Instance,
        };

    [Fact]
    public async Task Confident_unrouted_prediction_skips_LLM_classifier()
    {
        var firePort = Substitute.For<IFirePort>();
        firePort.FireBroadcast(Arg.Any<UnroutedIntent>(),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var optimizer = Substitute.For<INeuronOptimizer>();
        optimizer.Predict(Arg.Any<float[]>())
            .Returns(Task.FromResult<OptimizationResult?>(new OptimizationResult(false, 0.95f)));

        var grainFactory = Substitute.For<IGrainFactory>();
        grainFactory.GetGrain<INeuronOptimizer>("cortex-u1").Returns(optimizer);

        var chat = TrackedChat(out _);

        var cortex = new CortexNeuron(
            DiscoveryWith(typeof(FindFlightsRequest), typeof(IFakeFindFlightsPlan)),
            firePort, chat, EmptyCorpus(), grainFactory,
            NullLogger<CortexNeuron>.Instance);

        var result = await cortex.HandleAsync(
            new ChatIntent("totally unrelated to anything we know about", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.IsType<UnroutedIntent>(result.ResponsePayload);

        // Optimizer was consulted exactly once.
        await optimizer.Received(1).Predict(Arg.Any<float[]>());
        // The decision was recorded as unrouted (label=false).
        await optimizer.Received(1).Record(
            Arg.Is<DecisionRecord>(r => r != null && r.Label == false));
        // The LLM classifier was NOT invoked — that's the whole point of the slice.
        Assert.Equal(0, CountChatCalls(chat));
    }

    [Fact]
    public async Task Confident_routed_prediction_still_runs_LLM_classifier()
    {
        // Routability prediction alone doesn't tell Cortex which neuron
        // to route to; the LLM classifier is still the source of truth for
        // neuron-id selection. Only the negative-skip path saves tokens.
        var firePort = Substitute.For<IFirePort>();
        var optimizer = Substitute.For<INeuronOptimizer>();
        optimizer.Predict(Arg.Any<float[]>())
            .Returns(Task.FromResult<OptimizationResult?>(new OptimizationResult(true, 0.99f)));

        var grainFactory = Substitute.For<IGrainFactory>();
        grainFactory.GetGrain<INeuronOptimizer>("cortex-u1").Returns(optimizer);

        var chat = TrackedChat(out _);

        var cortex = new CortexNeuron(
            DiscoveryWith(typeof(FindFlightsRequest), typeof(IFakeFindFlightsPlan)),
            firePort, chat, EmptyCorpus(), grainFactory,
            NullLogger<CortexNeuron>.Instance);

        await cortex.HandleAsync(
            new ChatIntent("find me a flight to bali", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        // LLM classifier IS called even with high-confidence routable
        // prediction — that's the source of the neuron-id pick.
        var chatCalls = CountChatCalls(chat);
        Assert.True(chatCalls >= 1,
            $"expected at least one IChatClient call but got {chatCalls}");
    }

    [Fact]
    public async Task Untrained_optimizer_returns_null_and_routing_proceeds_to_LLM()
    {
        // Cold-start case — no model trained yet, Predict returns null,
        // Cortex falls through to the LLM classifier as if the optimizer
        // wasn't there.
        var firePort = Substitute.For<IFirePort>();
        var optimizer = Substitute.For<INeuronOptimizer>();
        optimizer.Predict(Arg.Any<float[]>())
            .Returns(Task.FromResult<OptimizationResult?>(null));

        var grainFactory = Substitute.For<IGrainFactory>();
        grainFactory.GetGrain<INeuronOptimizer>("cortex-u1").Returns(optimizer);

        var chat = TrackedChat(out _);

        var cortex = new CortexNeuron(
            DiscoveryWith(typeof(FindFlightsRequest), typeof(IFakeFindFlightsPlan)),
            firePort, chat, EmptyCorpus(), grainFactory,
            NullLogger<CortexNeuron>.Instance);

        await cortex.HandleAsync(
            new ChatIntent("anything at all", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(CountChatCalls(chat) >= 1);
        // Decision still recorded — that's how the model bootstraps.
        await optimizer.Received(1).Record(Arg.Any<DecisionRecord>());
    }

    [Fact]
    public async Task Optimizer_failure_does_not_break_routing()
    {
        // Production resilience guarantee: a misconfigured optimizer grain
        // (e.g. domain silo without IAW substrate, transport error) MUST
        // NOT propagate up and break the user's chat turn. The defensive
        // try/catch in Cortex swallows + logs.
        var firePort = Substitute.For<IFirePort>();
        firePort.FireBroadcast(Arg.Any<UnroutedIntent>(),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var optimizer = Substitute.For<INeuronOptimizer>();
        optimizer.Predict(Arg.Any<float[]>())
            .Returns<Task<OptimizationResult?>>(_ => throw new InvalidOperationException("transport blip"));
        optimizer.Record(Arg.Any<DecisionRecord>())
            .Returns<Task>(_ => throw new InvalidOperationException("transport blip"));

        var grainFactory = Substitute.For<IGrainFactory>();
        grainFactory.GetGrain<INeuronOptimizer>("cortex-u1").Returns(optimizer);

        var cortex = new CortexNeuron(
            DiscoveryWith(typeof(FindFlightsRequest), typeof(IFakeFindFlightsPlan)),
            firePort, TrackedChat(out _), EmptyCorpus(), grainFactory,
            NullLogger<CortexNeuron>.Instance);

        // Must not throw.
        var result = await cortex.HandleAsync(
            new ChatIntent("anything", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
    }
}
