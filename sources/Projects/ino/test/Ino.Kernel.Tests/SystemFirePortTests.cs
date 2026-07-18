using System.Diagnostics;
using System.Text.Json;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace Ino.Kernel.Tests;

public class SystemFirePortTests
{
    public sealed record TestSynapse(string Tag) : ISynapse;
    public sealed record ChildSynapse : ISynapse;

    public interface ITestNeuron : INeuron<TestSynapse> { }
    public interface IChildNeuron : INeuron<ChildSynapse> { }
    public interface ITestReactor : IReactsTo<TestSynapse> { }

    static readonly DomainId CallerDomain = DomainId.From("caller");
    static readonly DomainId TargetDomain = DomainId.From("target");
    static readonly DomainId AlphaDomain = DomainId.From("alpha");
    static readonly DomainId BetaDomain = DomainId.From("beta");

    static NeuronContext AmbientCtx(IFirePort port) =>
        new(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("kernel")),
            SourceStream: new StreamKey("<gateway>"))
        {
            FirePort = port,
            Logger = NullLogger.Instance,
        };

    static ActivityListener AttachListener(string sourceName, List<Activity> sink)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == sourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = sink.Add,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    [Fact]
    public async Task Fire_returns_NoCanonicalHandler_when_discovery_has_no_match()
    {
        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupCanonicalAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(null));
        var grains = Substitute.For<IGrainFactory>();
        var port = new SystemFirePort(grains, discovery, new ActivitySource("test"), new InMemoryInoEventBus(), new InMemorySynapseJournal(), new InMemoryReasoningProbe());

        var result = await port.Fire(new TestSynapse("x"), AmbientCtx(port), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(SynapseErrorCode.NoCanonicalHandler, result.Error!.Code);
    }

    [Fact]
    public async Task Fire_resolves_canonical_grain_by_interface_only_and_rewrites_caller_to_target_domain()
    {
        var sourceName = $"test-{Guid.NewGuid():N}";
        var activities = new List<Activity>();
        using var listener = AttachListener(sourceName, activities);

        var target = new CanonicalTarget(
            typeof(TestSynapse), typeof(ITestNeuron), TargetDomain, []);

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupCanonicalAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(target));

        NeuronContext? capturedChild = null;
        var grain = Substitute.For<ITestNeuron>();
        grain.HandleAsync(
                Arg.Any<TestSynapse>(),
                Arg.Do<NeuronContext>(c => capturedChild = c),
                Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok("ok"));

        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<INeuron<TestSynapse>>(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(grain);

        var port = new SystemFirePort(grains, discovery, new ActivitySource(sourceName), new InMemoryInoEventBus(), new InMemorySynapseJournal(), new InMemoryReasoningProbe());
        var ctx = AmbientCtx(port);

        var result = await port.Fire(new TestSynapse("x"), ctx, TestContext.Current.CancellationToken);

        Assert.True(result.Success);

        // CRITICAL: grainClassNamePrefix must be null — passing target.GrainType.FullName
        // silently mismatches against Orleans' lowercased GrainType.Name (see CLAUDE.md traps).
        grains.Received(1).GetGrain<INeuron<TestSynapse>>(ctx.CorrelationId.Value, null);

        Assert.NotNull(capturedChild);
        var childCaller = Assert.IsType<Caller.FromDomain>(capturedChild!.Source);
        Assert.Equal(TargetDomain, childCaller.Domain);
        Assert.Equal(ctx.CorrelationId, capturedChild.CorrelationId);
        Assert.NotEqual(ctx.SynapseId, capturedChild.SynapseId);

        var span = activities.Single(a => a.OperationName == Telemetry.Spans.Fire(typeof(TestSynapse)));
        Assert.Equal(typeof(TestSynapse).FullName, span.GetTagItem(Telemetry.Tags.SynapseType));
        Assert.Equal(TargetDomain.Value, span.GetTagItem(Telemetry.Tags.TargetDomain));
        Assert.Equal(ctx.CorrelationId.Value, span.GetTagItem(Telemetry.Tags.CorrelationId));
        Assert.Equal(true, span.GetTagItem(Telemetry.Tags.ResultSuccess));
    }

    [Fact]
    public async Task Fire_tags_span_with_error_code_when_handler_returns_failed_result()
    {
        var sourceName = $"test-{Guid.NewGuid():N}";
        var activities = new List<Activity>();
        using var listener = AttachListener(sourceName, activities);

        var target = new CanonicalTarget(typeof(TestSynapse), typeof(ITestNeuron), TargetDomain, []);
        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupCanonicalAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(target));

        var grain = Substitute.For<ITestNeuron>();
        grain.HandleAsync(Arg.Any<TestSynapse>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Fail(SynapseErrorCode.GrainActivationFailed, "boom"));

        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<INeuron<TestSynapse>>(Arg.Any<string>(), Arg.Any<string?>()).Returns(grain);

        var port = new SystemFirePort(grains, discovery, new ActivitySource(sourceName), new InMemoryInoEventBus(), new InMemorySynapseJournal(), new InMemoryReasoningProbe());

        var result = await port.Fire(new TestSynapse("x"), AmbientCtx(port), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        var span = activities.Single(a => a.OperationName == Telemetry.Spans.Fire(typeof(TestSynapse)));
        Assert.Equal(false, span.GetTagItem(Telemetry.Tags.ResultSuccess));
        Assert.Equal(SynapseErrorCode.GrainActivationFailed.ToString(), span.GetTagItem(Telemetry.Tags.ErrorCode));
    }

    [Fact]
    public async Task FireBroadcast_short_circuits_when_no_reactive_targets()
    {
        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupReactiveAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ReactiveTarget>>(Array.Empty<ReactiveTarget>()));
        var grains = Substitute.For<IGrainFactory>();
        var port = new SystemFirePort(grains, discovery, new ActivitySource("test"), new InMemoryInoEventBus(), new InMemorySynapseJournal(), new InMemoryReasoningProbe());

        await port.FireBroadcast(new TestSynapse("x"), AmbientCtx(port), TestContext.Current.CancellationToken);

        grains.DidNotReceiveWithAnyArgs().GetGrain<IReactsTo<TestSynapse>>(default!, default);
    }

    [Fact]
    public async Task FireBroadcast_dispatches_to_all_reactive_targets_in_parallel()
    {
        IReadOnlyList<ReactiveTarget> targets = new[]
        {
            new ReactiveTarget(typeof(TestSynapse), typeof(ITestReactor), AlphaDomain),
            new ReactiveTarget(typeof(TestSynapse), typeof(ITestReactor), BetaDomain),
        };

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupReactiveAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(targets));

        var grain = Substitute.For<ITestReactor>();
        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<IReactsTo<TestSynapse>>(Arg.Any<string>(), Arg.Any<string?>()).Returns(grain);

        var port = new SystemFirePort(grains, discovery, new ActivitySource("test"), new InMemoryInoEventBus(), new InMemorySynapseJournal(), new InMemoryReasoningProbe());

        await port.FireBroadcast(new TestSynapse("x"), AmbientCtx(port), TestContext.Current.CancellationToken);

        await grain.Received(2).ReactAsync(
            Arg.Any<TestSynapse>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FireBroadcast_aggregates_Orleans_transport_failures_and_rethrows()
    {
        var sourceName = $"test-{Guid.NewGuid():N}";
        var activities = new List<Activity>();
        using var listener = AttachListener(sourceName, activities);

        IReadOnlyList<ReactiveTarget> targets = new[]
        {
            new ReactiveTarget(typeof(TestSynapse), typeof(ITestReactor), AlphaDomain),
            new ReactiveTarget(typeof(TestSynapse), typeof(ITestReactor), BetaDomain),
        };

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupReactiveAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(targets));

        var grain = Substitute.For<ITestReactor>();
        grain.ReactAsync(Arg.Any<TestSynapse>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new SiloUnavailableException("silo down")));

        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<IReactsTo<TestSynapse>>(Arg.Any<string>(), Arg.Any<string?>()).Returns(grain);

        var port = new SystemFirePort(grains, discovery, new ActivitySource(sourceName), new InMemoryInoEventBus(), new InMemorySynapseJournal(), new InMemoryReasoningProbe());

        var act = async () =>
            await port.FireBroadcast(new TestSynapse("x"), AmbientCtx(port), TestContext.Current.CancellationToken);
        var assertion = await Assert.ThrowsAsync<AggregateException>(act);
        Assert.All(assertion.InnerExceptions, e => Assert.IsType<SiloUnavailableException>(e));

        var span = activities.Single(a => a.OperationName == Telemetry.Spans.FireBroadcast(typeof(TestSynapse)));
        Assert.Equal(2, span.GetTagItem(Telemetry.Tags.BroadcastTargetCount));
        Assert.Equal(2, span.GetTagItem(Telemetry.Tags.BroadcastFailedCount));
        Assert.Equal(2, span.GetTagItem(Telemetry.Tags.BroadcastTransportFailures));
    }

    /// <summary>
    /// Two-hop fan-out — a canonical handler for <c>TestSynapse</c> re-fires <c>ChildSynapse</c>
    /// through the NeuronContext's FirePort, which routes back through the same SystemFirePort.
    /// Verifies the port supports recursive dispatch (the ItineraryComposerNeuron shape the plan
    /// introduces in slice 8) and that the child's NeuronContext carries the correct correlation
    /// and target bundle for the second hop.
    /// </summary>
    [Fact]
    public async Task Fire_supports_two_hop_chains_when_handler_re_fires_through_ctx()
    {
        var parentTarget = new CanonicalTarget(
            typeof(TestSynapse), typeof(ITestNeuron), TargetDomain, []);
        var childTarget = new CanonicalTarget(
            typeof(ChildSynapse), typeof(IChildNeuron), AlphaDomain, []);

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupCanonicalAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(parentTarget));
        discovery.LookupCanonicalAsync(typeof(ChildSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(childTarget));

        NeuronContext? childCtx = null;
        var child = Substitute.For<IChildNeuron>();
        child.HandleAsync(
                Arg.Any<ChildSynapse>(),
                Arg.Do<NeuronContext>(c => childCtx = c),
                Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok("child-ok"));

        var parent = Substitute.For<ITestNeuron>();
        parent.HandleAsync(Arg.Any<TestSynapse>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                var parentCtx = call.Arg<NeuronContext>()!;
                var childResult = await parentCtx.Fire(new ChildSynapse(), call.Arg<CancellationToken>());
                return childResult.Success ? NeuronResult.Ok("parent-ok") : NeuronResult.Fail(SynapseErrorCode.GrainActivationFailed, "child failed");
            });

        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<INeuron<TestSynapse>>(Arg.Any<string>(), Arg.Any<string?>()).Returns(parent);
        grains.GetGrain<INeuron<ChildSynapse>>(Arg.Any<string>(), Arg.Any<string?>()).Returns(child);

        var port = new SystemFirePort(grains, discovery, new ActivitySource("test"), new InMemoryInoEventBus(), new InMemorySynapseJournal(), new InMemoryReasoningProbe());
        var rootCtx = AmbientCtx(port);

        var result = await port.Fire(new TestSynapse("x"), rootCtx, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal("parent-ok", result.Message);

        await child.Received(1).HandleAsync(
            Arg.Any<ChildSynapse>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>());

        Assert.NotNull(childCtx);
        Assert.Equal(rootCtx.CorrelationId, childCtx!.CorrelationId);
        var childCallerDomain = Assert.IsType<Caller.FromDomain>(childCtx.Source);
        Assert.Equal(AlphaDomain, childCallerDomain.Domain);
    }

    /// <summary>
    /// Wire contract with the Flutter Trace view: SystemFirePort must publish an
    /// <see cref="InoEvent"/> per Fire whose <c>Type</c> is one of the client's
    /// known kinds (currently <c>SynapseFired</c>) and whose <c>Payload</c> is a
    /// JSON document carrying <c>SequenceNumber</c>, <c>SynapseVerb</c>,
    /// <c>TargetId</c>, <c>CorrelationId</c>, and <c>Decay</c> — the five fields
    /// state/timeline_bloc.dart::_fromInoEvent decodes. Keep both sides in sync.
    /// </summary>
    [Fact]
    public async Task Fire_publishes_json_event_matching_flutter_trace_contract()
    {
        var target = new CanonicalTarget(
            typeof(TestSynapse), typeof(ITestNeuron), TargetDomain, []);
        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupCanonicalAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(target));

        var grain = Substitute.For<ITestNeuron>();
        grain.HandleAsync(Arg.Any<TestSynapse>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok("ok"));

        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<INeuron<TestSynapse>>(Arg.Any<string>(), Arg.Any<string?>()).Returns(grain);

        var bus = new InMemoryInoEventBus();
        var port = new SystemFirePort(grains, discovery, new ActivitySource("test"), bus, new InMemorySynapseJournal(), new InMemoryReasoningProbe());

        var userId = "default";
        var ctx = AmbientCtx(port) with { UserId = userId };

        using var sub = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var received = new List<InoEvent>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var evt in bus.SubscribeAsync(userId, sub.Token))
                received.Add(evt);
        }, sub.Token);

        // give the subscription loop a beat to register before publishing
        await Task.Delay(50, TestContext.Current.CancellationToken);

        await port.Fire(new TestSynapse("x"), ctx, TestContext.Current.CancellationToken);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        await sub.CancelAsync();
        try { await consumer; } catch (OperationCanceledException) { /* expected */ }

        Assert.Single(received, e => e.Type == "SynapseFired");
        var evt = received.Single(e => e.Type == "SynapseFired");

        using var doc = JsonDocument.Parse(evt.Payload.ToArray());
        var root = doc.RootElement;
        Assert.True(root.GetProperty("SequenceNumber").GetInt64() > 0);
        Assert.Equal(nameof(TestSynapse), root.GetProperty("SynapseVerb").GetString());
        Assert.Equal(typeof(ITestNeuron).FullName, root.GetProperty("TargetId").GetString());
        Assert.Equal(ctx.CorrelationId.Value, root.GetProperty("CorrelationId").GetString());
        Assert.Equal(100, root.GetProperty("Decay").GetInt32());
    }

    /// <summary>
    /// Slice 15 — when IReasoningProbe has a scenario recorded for the target grain
    /// type (Cortex does this via BddMockChatClient before firing), the SynapseFired
    /// envelope must carry Scenario/Feature/ReasoningSource so the Flutter
    /// Reasoning panel can render "mocked via BDD · {scenario}" from TimelineBloc
    /// state without a separate gateway round-trip.
    /// </summary>
    [Fact]
    public async Task Fire_attaches_reasoning_to_payload_when_probe_has_entry_for_target()
    {
        var target = new CanonicalTarget(
            typeof(TestSynapse), typeof(ITestNeuron), TargetDomain, []);
        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupCanonicalAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(target));

        var grain = Substitute.For<ITestNeuron>();
        grain.HandleAsync(Arg.Any<TestSynapse>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok("ok"));

        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<INeuron<TestSynapse>>(Arg.Any<string>(), Arg.Any<string?>()).Returns(grain);

        var probe = new InMemoryReasoningProbe();
        probe.Record(typeof(ITestNeuron).FullName!, new ReasoningRecord(
            Source: "bdd-mock",
            ScenarioName: "Find flights",
            FeatureTitle: "Travel — intent routing",
            Prompt: "find flights to Bali",
            Reply: "Searching flights via the FlightSearch neuron.",
            Timestamp: DateTimeOffset.UtcNow));

        var bus = new InMemoryInoEventBus();
        var port = new SystemFirePort(grains, discovery, new ActivitySource("test"), bus, new InMemorySynapseJournal(), probe);

        var userId = "default";
        var ctx = AmbientCtx(port) with { UserId = userId };

        using var sub = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var received = new List<InoEvent>();
        var consumer = Task.Run(async () =>
        {
            await foreach (var evt in bus.SubscribeAsync(userId, sub.Token))
                received.Add(evt);
        }, sub.Token);

        await Task.Delay(50, TestContext.Current.CancellationToken);
        await port.Fire(new TestSynapse("x"), ctx, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await sub.CancelAsync();
        try { await consumer; } catch (OperationCanceledException) { /* expected */ }

        var evt = received.Single(e => e.Type == "SynapseFired");
        using var doc = JsonDocument.Parse(evt.Payload.ToArray());
        var root = doc.RootElement;
        Assert.Equal("Find flights", root.GetProperty("Scenario").GetString());
        Assert.Equal("Travel — intent routing", root.GetProperty("Feature").GetString());
        Assert.Equal("bdd-mock", root.GetProperty("ReasoningSource").GetString());
    }
}
