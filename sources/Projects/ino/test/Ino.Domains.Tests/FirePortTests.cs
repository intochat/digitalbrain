using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Orleans.Runtime;
using Xunit;

namespace Ino.Domains.Tests;

public class FirePortTests
{
    public sealed record TestSynapse : ISynapse;

    public interface ITestNeuronGrain : INeuron<TestSynapse> { }
    public interface ITestReactorGrain : IReactsTo<TestSynapse> { }

    private static readonly DomainId CallerDomain = DomainId.From("caller");
    private static readonly DomainId TargetDomain = DomainId.From("target");
    private static readonly DomainId AlphaDomain = DomainId.From("alpha");
    private static readonly DomainId BetaDomain = DomainId.From("beta");

    private static NeuronContext MakeCtx(IFirePort firePort) =>
        new(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.FromDomain(CallerDomain),
            SourceStream: new StreamKey("<test>"))
        {
            FirePort = firePort,
            Logger = NullLogger.Instance,
        };

    private static ActivityListener AttachListener(string sourceName, List<Activity> sink)
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
        var enforcer = Substitute.For<ICapabilityEnforcer>();
        var grains = Substitute.For<IGrainFactory>();
        var port = new FirePort(grains, discovery, enforcer, new ActivitySource("test"));

        var result = await port.Fire(new TestSynapse(), MakeCtx(port), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(SynapseErrorCode.NoCanonicalHandler, result.Error!.Code);
    }

    [Fact]
    public async Task Fire_propagates_CapabilityDenied_from_enforcer()
    {
        var target = new CanonicalTarget(
            typeof(TestSynapse), typeof(ITestNeuronGrain), TargetDomain,
            [new Capability.Llm(LlmTier.Reasoning)]);

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupCanonicalAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(target));
        var enforcer = Substitute.For<ICapabilityEnforcer>();
        enforcer.When(e => e.AssertCanFire(Arg.Any<Caller>(), Arg.Any<CanonicalTarget>()))
            .Do(_ => throw new CapabilityDeniedException("denied"));
        var grains = Substitute.For<IGrainFactory>();
        var port = new FirePort(grains, discovery, enforcer, new ActivitySource("test"));

        var result = await port.Fire(new TestSynapse(), MakeCtx(port), TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Equal(SynapseErrorCode.CapabilityDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Fire_happy_path_resolves_grain_by_interface_only_and_rewrites_caller()
    {
        var sourceName = $"test-{Guid.NewGuid():N}";
        var activities = new List<Activity>();
        using var listener = AttachListener(sourceName, activities);

        var target = new CanonicalTarget(
            typeof(TestSynapse), typeof(ITestNeuronGrain), TargetDomain, []);

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupCanonicalAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(target));

        var enforcer = Substitute.For<ICapabilityEnforcer>();

        NeuronContext? capturedChildContext = null;
        var grain = Substitute.For<ITestNeuronGrain>();
        grain.HandleAsync(Arg.Any<TestSynapse>(), Arg.Do<NeuronContext>(c => capturedChildContext = c), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Ok("ok"));

        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<INeuron<TestSynapse>>(
                Arg.Any<string>(), Arg.Any<string?>())
            .Returns(grain);

        var ctx = MakeCtx(new NoOpFirePort());
        var port = new FirePort(grains, discovery, enforcer, new ActivitySource(sourceName));

        var result = await port.Fire(new TestSynapse(), ctx, TestContext.Current.CancellationToken);

        Assert.True(result.Success);

        // Interface-only resolution — passing GrainType.FullName as a prefix silently
        // mismatches Orleans' lowercased GrainType.Name (see CLAUDE.md known-traps) and
        // can hit a post-join gossip race; Discovery already guarantees one canonical
        // handler per synapse type, so null prefix is the correct call.
        grains.Received(1).GetGrain<INeuron<TestSynapse>>(ctx.CorrelationId.Value, null);

        Assert.NotNull(capturedChildContext);
        var childCaller = Assert.IsType<Caller.FromDomain>(capturedChildContext!.Source);
        Assert.Equal(TargetDomain, childCaller.Domain);
        Assert.Equal(ctx.CorrelationId, capturedChildContext.CorrelationId);
        Assert.NotEqual(ctx.SynapseId, capturedChildContext.SynapseId);

        Assert.Single(activities, a => a.OperationName == Telemetry.Spans.Fire(typeof(TestSynapse)));
        var span = activities.Single(a => a.OperationName == Telemetry.Spans.Fire(typeof(TestSynapse)));
        Assert.Equal(typeof(TestSynapse).FullName, span.GetTagItem(Telemetry.Tags.SynapseType));
        Assert.Equal(CallerDomain.Value, span.GetTagItem(Telemetry.Tags.SourceDomain));
        Assert.Equal(TargetDomain.Value, span.GetTagItem(Telemetry.Tags.TargetDomain));
        Assert.Equal(ctx.CorrelationId.Value, span.GetTagItem(Telemetry.Tags.CorrelationId));
        Assert.Equal(true, span.GetTagItem(Telemetry.Tags.ResultSuccess));
    }

    [Fact]
    public async Task Fire_tags_ErrorCode_when_result_fails()
    {
        var sourceName = $"test-{Guid.NewGuid():N}";
        var activities = new List<Activity>();
        using var listener = AttachListener(sourceName, activities);

        var target = new CanonicalTarget(
            typeof(TestSynapse), typeof(ITestNeuronGrain), TargetDomain, []);

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupCanonicalAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<CanonicalTarget?>(target));

        var enforcer = Substitute.For<ICapabilityEnforcer>();

        var grain = Substitute.For<ITestNeuronGrain>();
        grain.HandleAsync(Arg.Any<TestSynapse>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(NeuronResult.Fail(SynapseErrorCode.GrainActivationFailed, "bad"));

        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<INeuron<TestSynapse>>(Arg.Any<string>(), Arg.Any<string?>()).Returns(grain);

        var port = new FirePort(grains, discovery, enforcer, new ActivitySource(sourceName));

        var result = await port.Fire(new TestSynapse(), MakeCtx(new NoOpFirePort()), TestContext.Current.CancellationToken);

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
        var enforcer = Substitute.For<ICapabilityEnforcer>();
        var grains = Substitute.For<IGrainFactory>();
        var port = new FirePort(grains, discovery, enforcer, new ActivitySource("test"));

        await port.FireBroadcast(new TestSynapse(), MakeCtx(port), TestContext.Current.CancellationToken);

        grains.DidNotReceiveWithAnyArgs().GetGrain<IReactsTo<TestSynapse>>(default!, default);
    }

    [Fact]
    public async Task FireBroadcast_dispatches_to_all_targets_on_happy_path()
    {
        var targets = new IReadOnlyList<ReactiveTarget>[] { new[]
        {
            new ReactiveTarget(typeof(TestSynapse), typeof(ITestReactorGrain), AlphaDomain),
            new ReactiveTarget(typeof(TestSynapse), typeof(ITestReactorGrain), BetaDomain),
        } };

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupReactiveAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(targets[0]));

        var enforcer = Substitute.For<ICapabilityEnforcer>();

        var grain = Substitute.For<ITestReactorGrain>();
        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<IReactsTo<TestSynapse>>(Arg.Any<string>(), Arg.Any<string?>()).Returns(grain);

        var port = new FirePort(grains, discovery, enforcer, new ActivitySource("test"));

        await port.FireBroadcast(new TestSynapse(), MakeCtx(port), TestContext.Current.CancellationToken);

        await grain.Received(2).ReactAsync(Arg.Any<TestSynapse>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FireBroadcast_one_throws_others_still_succeed_and_span_records_failure_count()
    {
        var sourceName = $"test-{Guid.NewGuid():N}";
        var activities = new List<Activity>();
        using var listener = AttachListener(sourceName, activities);

        var alpha = new ReactiveTarget(typeof(TestSynapse), typeof(ITestReactorGrain), AlphaDomain);
        var beta = new ReactiveTarget(typeof(TestSynapse), typeof(ITestReactorGrain), BetaDomain);
        IReadOnlyList<ReactiveTarget> targets = new[] { alpha, beta };

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupReactiveAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(targets));

        var enforcer = Substitute.For<ICapabilityEnforcer>();
        enforcer
            .When(e => e.AssertCanFireBroadcast(Arg.Any<Caller>(), Arg.Is<ReactiveTarget>(t => t!.Domain == AlphaDomain)))
            .Do(_ => throw new CapabilityDeniedException("alpha denied"));

        var healthyGrain = Substitute.For<ITestReactorGrain>();
        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<IReactsTo<TestSynapse>>(Arg.Any<string>(), Arg.Any<string?>())
            .Returns(healthyGrain);

        var port = new FirePort(grains, discovery, enforcer, new ActivitySource(sourceName));

        await port.FireBroadcast(new TestSynapse(), MakeCtx(port), TestContext.Current.CancellationToken);

        await healthyGrain.Received(1).ReactAsync(Arg.Any<TestSynapse>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>());

        var span = activities.Single(a => a.OperationName == Telemetry.Spans.FireBroadcast(typeof(TestSynapse)));
        Assert.Equal(2, span.GetTagItem(Telemetry.Tags.BroadcastTargetCount));
        Assert.Equal(1, span.GetTagItem(Telemetry.Tags.BroadcastFailedCount));
        Assert.Equal(1, span.GetTagItem(Telemetry.Tags.BroadcastCapabilityDenied));
    }

    [Fact]
    public async Task FireBroadcast_aggregates_Orleans_transport_failures_and_rethrows()
    {
        var sourceName = $"test-{Guid.NewGuid():N}";
        var activities = new List<Activity>();
        using var listener = AttachListener(sourceName, activities);

        var alpha = new ReactiveTarget(typeof(TestSynapse), typeof(ITestReactorGrain), AlphaDomain);
        var beta = new ReactiveTarget(typeof(TestSynapse), typeof(ITestReactorGrain), BetaDomain);
        IReadOnlyList<ReactiveTarget> targets = new[] { alpha, beta };

        var discovery = Substitute.For<IDiscoveryClient>();
        discovery.LookupReactiveAsync(typeof(TestSynapse), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(targets));

        var enforcer = Substitute.For<ICapabilityEnforcer>();

        var grain = Substitute.For<ITestReactorGrain>();
        grain.ReactAsync(Arg.Any<TestSynapse>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromException(new SiloUnavailableException("silo down")));

        var grains = Substitute.For<IGrainFactory>();
        grains.GetGrain<IReactsTo<TestSynapse>>(Arg.Any<string>(), Arg.Any<string?>()).Returns(grain);

        var port = new FirePort(grains, discovery, enforcer, new ActivitySource(sourceName));

        var act = async () => await port.FireBroadcast(new TestSynapse(), MakeCtx(port), TestContext.Current.CancellationToken);
        var assertion = await Assert.ThrowsAsync<AggregateException>(act);
        Assert.All(assertion.InnerExceptions, e => Assert.IsType<SiloUnavailableException>(e));

        var span = activities.Single(a => a.OperationName == Telemetry.Spans.FireBroadcast(typeof(TestSynapse)));
        Assert.Equal(2, span.GetTagItem(Telemetry.Tags.BroadcastTransportFailures));
        Assert.Equal(2, span.GetTagItem(Telemetry.Tags.BroadcastFailedCount));
    }
}
