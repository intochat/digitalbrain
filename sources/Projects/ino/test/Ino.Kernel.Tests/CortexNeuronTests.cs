using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Taxi.Contracts;
using Ino.Domains.Travel.Contracts;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Xunit;

namespace Ino.Kernel.Tests;

/// <summary>
/// CortexNeuron routing tests. Phase 4 Slice A removed the legacy single-hop
/// switch from Cortex — every routable neuron must declare an
/// <see cref="INeuronDefinition.PlanType"/>, and Cortex's only routing surface is
/// "resolve the plan grain and invoke ExecuteAsync." These tests assert on
/// that seam (grain factory + plan grain), not on synapse construction (which
/// is now the plan body's responsibility — covered by per-domain plan tests).
/// </summary>
public class CortexNeuronTests
{
    static readonly DomainId TravelDomain = DomainId.From("ino.domains.travel");
    static readonly DomainId TaxiDomain = DomainId.From("ino.domains.taxi");

    static readonly NeuronId FindFlightsId = NeuronId.From("travel.find-flights");
    static readonly NeuronId FindHotelsId = NeuronId.From("travel.find-hotels");
    static readonly NeuronId FindPlacesId = NeuronId.From("travel.find-places");
    static readonly NeuronId PlanTripId = NeuronId.From("travel.plan-trip");
    static readonly NeuronId FindRideId = NeuronId.From("taxi.find-ride");

    // Fake plan interfaces local to the test surface — production plan
    // interfaces live in <domain>.Contracts. Cortex resolves whichever
    // PlanType the neuron declares, so the test substitute uses these.
    public interface IFakeFindFlightsPlan : INeuronPlan { }
    public interface IFakeFindHotelsPlan : INeuronPlan { }
    public interface IFakeFindPlacesPlan : INeuronPlan { }
    public interface IFakePlanTripPlan : INeuronPlan { }
    public interface IFakeFindRidePlan : INeuronPlan { }

    /// <summary>No-op IChatClient for tests that don't exercise BDD-mock reasoning.</summary>
    static IChatClient NoChat()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))));
        return client;
    }

    static CortexNeuron NewCortex(
        IDiscoveryClient discovery,
        IFirePort firePort,
        INeuronPromptCorpus corpus,
        IChatClient? chatClient = null,
        IGrainFactory? grainFactory = null) =>
        new(discovery,
            firePort,
            chatClient ?? NoChat(),
            corpus,
            grainFactory ?? Substitute.For<IGrainFactory>(),
            NullLogger<CortexNeuron>.Instance);

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

    /// <summary>
    /// Mocks <c>grainFactory.GetGrain(typeof(TPlan), …)</c> to return a fresh
    /// plan substitute whose <c>ExecuteAsync</c> returns <paramref name="result"/>
    /// and captures the inbound <see cref="NeuronPlanContext"/> into
    /// <paramref name="captured"/>. Returns the plan substitute so tests can
    /// assert on call counts.
    /// </summary>
    static TPlan MockPlan<TPlan>(IGrainFactory grainFactory, NeuronResult result, out CapturedContext captured)
        where TPlan : class, INeuronPlan
    {
        var plan = Substitute.For<TPlan>();
        var box = new CapturedContext();
        plan.ExecuteAsync(Arg.Do<NeuronPlanContext>(c => box.Value = c), Arg.Any<CancellationToken>())
            .Returns(result);
        grainFactory.GetGrain(typeof(TPlan), Arg.Any<string>())
            .Returns(plan);
        captured = box;
        return plan;
    }

    sealed class CapturedContext
    {
        public NeuronPlanContext? Value { get; set; }
    }

    /// <summary>
    /// Builds an IDiscoveryClient that exposes the given (synapseType, planType)
    /// pairs as both installed canonicals AND as installed neurons. Cortex
    /// walks neurons first then re-checks installation per synapse — both
    /// paths need to agree.
    /// </summary>
    static IDiscoveryClient DiscoveryWith(params (Type Synapse, Type Plan)[] installed)
    {
        var discovery = Substitute.For<IDiscoveryClient>();
        var neurons = installed.Select(p => BuildNeuronDefinition(p.Synapse, p.Plan)).ToArray();
        discovery.DumpNeuronsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<INeuronDefinition>>(neurons));
        foreach (var (synapse, _) in installed)
        {
            discovery.LookupCanonicalAsync(synapse, Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<CanonicalTarget?>(
                    new CanonicalTarget(synapse, typeof(object), DomainForSynapse(synapse), [])));
        }
        return discovery;
    }

    static INeuronDefinition BuildNeuronDefinition(Type synapseType, Type planType) => new NeuronDefinition(
        Id: NeuronIdFor(synapseType),
        DisplayName: synapseType.Name,
        Description: $"Test neuron for {synapseType.Name}",
        CanonicalSynapseType: synapseType,
        PromptExamples: Array.Empty<string>())
    {
        PlanType = planType,
    };

    /// <summary>Variant for tests that explicitly need a plan-less neuron
    /// (e.g. fall-through-to-unrouted scenarios).</summary>
    static INeuronDefinition BuildNeuronDefinitionWithoutPlan(Type synapseType) => new NeuronDefinition(
        Id: NeuronIdFor(synapseType),
        DisplayName: synapseType.Name,
        Description: $"Test neuron for {synapseType.Name}",
        CanonicalSynapseType: synapseType,
        PromptExamples: Array.Empty<string>());

    static NeuronId NeuronIdFor(Type t)
    {
        if (t == typeof(FindFlightsRequest)) return FindFlightsId;
        if (t == typeof(FindHotelsRequest)) return FindHotelsId;
        if (t == typeof(FindPlacesRequest)) return FindPlacesId;
        if (t == typeof(PlanTripRequest)) return PlanTripId;
        if (t == typeof(FindRideRequest)) return FindRideId;
        return NeuronId.From($"test.{t.Name.ToLowerInvariant()}");
    }

    static DomainId DomainForSynapse(Type t) =>
        t == typeof(FindRideRequest) ? TaxiDomain : TravelDomain;

    /// <summary>
    /// Builds an INeuronPromptCorpus from inline (neuronId, regex) tuples.
    /// Mirrors what BddScenarioPromptCorpus would produce from tagged .feature
    /// scenarios in real silos, without needing to load files.
    /// </summary>
    static INeuronPromptCorpus Corpus(params (NeuronId Id, string Pattern)[] entries)
    {
        var scenarios = entries.Select((e, i) => new BddScenario(
            FeatureTitle: "test",
            ScenarioName: $"scenario-{i}",
            PromptPattern: e.Pattern,
            ReplyText: "ok",
            Tags: new[] { $"@neuron:{e.Id.Value}" },
            SourceFile: "inline")).ToArray();
        return new BddScenarioPromptCorpus(scenarios);
    }

    [Fact]
    public async Task Routes_flight_pattern_to_FindFlightsPlan()
    {
        var firePort = Substitute.For<IFirePort>();
        var grainFactory = Substitute.For<IGrainFactory>();
        var plan = MockPlan<IFakeFindFlightsPlan>(grainFactory, NeuronResult.Ok("flights-ok"), out var captured);

        var cortex = NewCortex(
            DiscoveryWith((typeof(FindFlightsRequest), typeof(IFakeFindFlightsPlan))),
            firePort,
            Corpus((FindFlightsId, "find.*flight|flights? to")),
            grainFactory: grainFactory);

        var result = await cortex.HandleAsync(
            new ChatIntent("find me a flight to Bali", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("flights-ok", result.Message);
        Assert.NotNull(captured.Value);
        Assert.Equal("find me a flight to Bali", captured.Value!.Prompt);
        Assert.Equal(FindFlightsId, captured.Value.NeuronId);
        await plan.Received(1).ExecuteAsync(Arg.Any<NeuronPlanContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Routes_taxi_pattern_to_FindRidePlan()
    {
        var firePort = Substitute.For<IFirePort>();
        var grainFactory = Substitute.For<IGrainFactory>();
        var plan = MockPlan<IFakeFindRidePlan>(grainFactory, NeuronResult.Ok("ride-ok"), out var captured);

        var cortex = NewCortex(
            DiscoveryWith((typeof(FindRideRequest), typeof(IFakeFindRidePlan))),
            firePort,
            Corpus((FindRideId, "book a taxi|ride uber|need an? uber")),
            grainFactory: grainFactory);

        var result = await cortex.HandleAsync(
            new ChatIntent("book a taxi to the airport", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("ride-ok", result.Message);
        Assert.NotNull(captured.Value);
        Assert.Equal("book a taxi to the airport", captured.Value!.Prompt);
        Assert.Equal(FindRideId, captured.Value.NeuronId);
        await plan.Received(1).ExecuteAsync(Arg.Any<NeuronPlanContext>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("I want to ride uber across town")]
    [InlineData("need an Uber")]
    [InlineData("Book a TAXI now")]
    public async Task Taxi_patterns_are_case_insensitive(string text)
    {
        var firePort = Substitute.For<IFirePort>();
        var grainFactory = Substitute.For<IGrainFactory>();
        var plan = MockPlan<IFakeFindRidePlan>(grainFactory, NeuronResult.Ok(), out _);

        var cortex = NewCortex(
            DiscoveryWith((typeof(FindRideRequest), typeof(IFakeFindRidePlan))),
            firePort,
            Corpus((FindRideId, "book a taxi|ride uber|need an? uber")),
            grainFactory: grainFactory);

        await cortex.HandleAsync(new ChatIntent(text, "u"), Ctx(firePort), TestContext.Current.CancellationToken);

        await plan.Received(1).ExecuteAsync(Arg.Any<NeuronPlanContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Routes_plan_pattern_to_PlanTripPlan()
    {
        var firePort = Substitute.For<IFirePort>();
        var grainFactory = Substitute.For<IGrainFactory>();
        var plan = MockPlan<IFakePlanTripPlan>(grainFactory, NeuronResult.Ok("plan-ok"), out var captured);

        var cortex = NewCortex(
            DiscoveryWith((typeof(PlanTripRequest), typeof(IFakePlanTripPlan))),
            firePort,
            Corpus((PlanTripId, "plan.*trip")),
            grainFactory: grainFactory);

        var result = await cortex.HandleAsync(
            new ChatIntent("plan a week-long trip to Bali", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.NotNull(captured.Value);
        Assert.Equal("plan a week-long trip to Bali", captured.Value!.Prompt);
        Assert.Equal(PlanTripId, captured.Value.NeuronId);
        await plan.Received(1).ExecuteAsync(Arg.Any<NeuronPlanContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Matched_pattern_but_uninstalled_handler_falls_through_to_UnroutedIntent()
    {
        var firePort = Substitute.For<IFirePort>();
        UnroutedIntent? broadcast = null;
        firePort.FireBroadcast(Arg.Do<UnroutedIntent>(u => broadcast = u),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // NeuronDefinition advertised, corpus has its pattern, but Discovery says
        // its canonical handler isn't installed — Cortex must skip and emit
        // unrouted rather than dispatching the plan against a missing handler.
        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.DumpNeuronsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<INeuronDefinition>>(
                new[] { BuildNeuronDefinition(typeof(FindFlightsRequest), typeof(IFakeFindFlightsPlan)) }));
        discovery.LookupCanonicalAsync(Arg.Any<Type>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(null));

        var grainFactory = Substitute.For<IGrainFactory>();
        var cortex = NewCortex(
            discovery,
            firePort,
            Corpus((FindFlightsId, "find.*flight")),
            grainFactory: grainFactory);
        var result = await cortex.HandleAsync(
            new ChatIntent("find me a flight to Bali", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var unrouted = Assert.IsType<UnroutedIntent>(result.ResponsePayload);
        Assert.Equal("find me a flight to Bali", unrouted.Text);
        Assert.NotNull(broadcast);
        Assert.Equal("u1", broadcast!.UserId);

        grainFactory.DidNotReceive().GetGrain(typeof(IFakeFindFlightsPlan), Arg.Any<string>());
    }

    [Fact]
    public async Task Plan_less_neuron_falls_through_to_UnroutedIntent()
    {
        // After Slice A every routable neuron must declare a PlanType.
        // A neuron without one reaches the routing tail, logs a debug
        // line, and falls through — the same observable shape as "no
        // canonical handler installed."
        var firePort = Substitute.For<IFirePort>();
        UnroutedIntent? broadcast = null;
        firePort.FireBroadcast(Arg.Do<UnroutedIntent>(u => broadcast = u),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.DumpNeuronsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<INeuronDefinition>>(
                new[] { BuildNeuronDefinitionWithoutPlan(typeof(FindFlightsRequest)) }));
        discovery.LookupCanonicalAsync(typeof(FindFlightsRequest), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(
                new CanonicalTarget(typeof(FindFlightsRequest), typeof(object), TravelDomain, [])));

        // Plan-less neurons are also rejected by the LLM-classifier filter
        // (CanConstructSynapse), so they don't even reach the classifier prompt.
        var cortex = NewCortex(
            discovery,
            firePort,
            Corpus((FindFlightsId, "find.*flight")));

        var result = await cortex.HandleAsync(
            new ChatIntent("find me a flight to Bali", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.IsType<UnroutedIntent>(result.ResponsePayload);
        Assert.NotNull(broadcast);
    }

    [Fact]
    public async Task Unmatched_text_with_mock_llm_falls_back_to_UnroutedIntent()
    {
        var firePort = Substitute.For<IFirePort>();
        UnroutedIntent? broadcast = null;
        firePort.FireBroadcast(Arg.Do<UnroutedIntent>(u => broadcast = u),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Mock chat client returns empty text — JSON parse fails, classifier
        // returns null. With the corpus also missing this prompt, unrouted is
        // the correct outcome.
        var grainFactory = Substitute.For<IGrainFactory>();
        var cortex = NewCortex(
            DiscoveryWith(
                (typeof(FindFlightsRequest), typeof(IFakeFindFlightsPlan)),
                (typeof(FindRideRequest), typeof(IFakeFindRidePlan)),
                (typeof(PlanTripRequest), typeof(IFakePlanTripPlan))),
            firePort,
            Corpus(
                (FindFlightsId, "find.*flight"),
                (FindRideId, "book a taxi|need an? uber"),
                (PlanTripId, "plan.*trip")),
            grainFactory: grainFactory);

        var result = await cortex.HandleAsync(
            new ChatIntent("what's the weather in Paris", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.IsType<UnroutedIntent>(result.ResponsePayload);
        Assert.NotNull(broadcast);
        Assert.Equal("what's the weather in Paris", broadcast!.Text);

        // No plan grain should have been resolved.
        grainFactory.DidNotReceive().GetGrain(typeof(IFakeFindFlightsPlan), Arg.Any<string>());
        grainFactory.DidNotReceive().GetGrain(typeof(IFakeFindRidePlan), Arg.Any<string>());
        grainFactory.DidNotReceive().GetGrain(typeof(IFakePlanTripPlan), Arg.Any<string>());
    }

    [Fact]
    public async Task First_pattern_match_in_neuron_walk_order_wins()
    {
        var firePort = Substitute.For<IFirePort>();
        var grainFactory = Substitute.For<IGrainFactory>();
        var flightsPlan = MockPlan<IFakeFindFlightsPlan>(grainFactory, NeuronResult.Ok(), out _);
        var planTripPlan = MockPlan<IFakePlanTripPlan>(grainFactory, NeuronResult.Ok(), out _);

        // Both neurons would match this prompt. Walk order is
        // DumpNeuronsAsync's order, so the test mocks Discovery to return
        // flight first.
        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.DumpNeuronsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<INeuronDefinition>>(new[]
            {
                BuildNeuronDefinition(typeof(FindFlightsRequest), typeof(IFakeFindFlightsPlan)),
                BuildNeuronDefinition(typeof(PlanTripRequest), typeof(IFakePlanTripPlan)),
            }));
        discovery.LookupCanonicalAsync(typeof(FindFlightsRequest), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(
                new CanonicalTarget(typeof(FindFlightsRequest), typeof(object), TravelDomain, [])));
        discovery.LookupCanonicalAsync(typeof(PlanTripRequest), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(
                new CanonicalTarget(typeof(PlanTripRequest), typeof(object), TravelDomain, [])));

        var cortex = NewCortex(
            discovery,
            firePort,
            Corpus(
                (FindFlightsId, "find.*flight"),
                (PlanTripId, "plan.*trip")),
            grainFactory: grainFactory);

        await cortex.HandleAsync(
            new ChatIntent("find me a flight for my trip to Bali", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        await flightsPlan.Received(1).ExecuteAsync(Arg.Any<NeuronPlanContext>(), Arg.Any<CancellationToken>());
        await planTripPlan.DidNotReceive().ExecuteAsync(Arg.Any<NeuronPlanContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LLM_classifier_picks_neuron_when_corpus_misses()
    {
        // Ambiguous prompt that no regex pattern catches — the JSON-mode
        // classifier picks travel.plan-trip and Cortex routes accordingly.
        var firePort = Substitute.For<IFirePort>();
        var grainFactory = Substitute.For<IGrainFactory>();
        var planTripPlan = MockPlan<IFakePlanTripPlan>(grainFactory, NeuronResult.Ok("plan-ok"), out var captured);

        var chat = Substitute.For<IChatClient>();
        chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var options = call.Arg<ChatOptions?>();
                // The classifier call sets ResponseFormat=Json; the reasoning
                // annotation call sets AdditionalProperties[NeuronIdKey]. Use
                // that to distinguish.
                if (options?.ResponseFormat is ChatResponseFormatJson)
                {
                    return Task.FromResult(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, "{\"neuronId\":\"travel.plan-trip\"}")));
                }
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));
            });

        var cortex = NewCortex(
            DiscoveryWith(
                (typeof(FindFlightsRequest), typeof(IFakeFindFlightsPlan)),
                (typeof(PlanTripRequest), typeof(IFakePlanTripPlan))),
            firePort,
            // Corpus deliberately empty for this prompt — neither flight nor
            // plan regex hits "i was thinking maybe Bali this autumn".
            Corpus(
                (FindFlightsId, "^impossible$"),
                (PlanTripId, "^impossible$")),
            chat,
            grainFactory: grainFactory);

        var result = await cortex.HandleAsync(
            new ChatIntent("i was thinking maybe Bali this autumn", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("plan-ok", result.Message);
        Assert.NotNull(captured.Value);
        Assert.Equal("i was thinking maybe Bali this autumn", captured.Value!.Prompt);
        Assert.Equal(PlanTripId, captured.Value.NeuronId);
        await planTripPlan.Received(1).ExecuteAsync(Arg.Any<NeuronPlanContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LLM_classifier_returning_null_falls_through_to_UnroutedIntent()
    {
        var firePort = Substitute.For<IFirePort>();
        UnroutedIntent? broadcast = null;
        firePort.FireBroadcast(Arg.Do<UnroutedIntent>(u => broadcast = u),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var chat = Substitute.For<IChatClient>();
        chat.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var options = call.Arg<ChatOptions?>();
                if (options?.ResponseFormat is ChatResponseFormatJson)
                {
                    return Task.FromResult(new ChatResponse(
                        new ChatMessage(ChatRole.Assistant, "{\"neuronId\":null}")));
                }
                return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "")));
            });

        var cortex = NewCortex(
            DiscoveryWith((typeof(FindFlightsRequest), typeof(IFakeFindFlightsPlan))),
            firePort,
            Corpus((FindFlightsId, "^impossible$")),
            chat);

        var result = await cortex.HandleAsync(
            new ChatIntent("tell me a joke", "u1"),
            Ctx(firePort),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.IsType<UnroutedIntent>(result.ResponsePayload);
        Assert.NotNull(broadcast);
    }
}
