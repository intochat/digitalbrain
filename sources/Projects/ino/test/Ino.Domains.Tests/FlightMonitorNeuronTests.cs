using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Travel.Contracts;
using FlightMonitorAgent = Ino.Domains.Travel.FlightMonitor.FlightMonitor;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans.Runtime;
using Orleans.Timers;
using Xunit;

namespace Ino.Domains.Tests;

public class FlightMonitorTests
{
    /// <summary>
    /// FlightMonitor's narrative LLM call wraps the synthetic delay reason —
    /// for unit tests we mock the factory so it returns an empty reply
    /// (NaN matched), which makes the neuron fall back to its fixed-string
    /// reason. That keeps the tests asserting on a deterministic value.
    /// </summary>
    static IChatClientFactory NoopFactory()
    {
        var client = Substitute.For<IChatClient>();
        client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, ""))));
        var factory = Substitute.For<IChatClientFactory>();
        factory.ForTier(Arg.Any<LlmTier>()).Returns(client);
        return factory;
    }

    static NeuronContext Ctx(IFirePort port) =>
        new(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("domains")),
            SourceStream: new StreamKey("<monitor-test>"))
        {
            FirePort = port,
            Logger = NullLogger.Instance,
        };

    sealed class CapturedTimer : IGrainTimer
    {
        public Func<CancellationToken, Task>? Callback { get; set; }
        public GrainTimerCreationOptions? Options { get; set; }
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        public void Change(TimeSpan dueTime, TimeSpan period) { }
    }

    static (ITimerRegistry registry, List<CapturedTimer> timers) BuildTimerRegistry()
    {
        var registry = Substitute.For<ITimerRegistry>();
        var captured = new List<CapturedTimer>();
        registry.RegisterGrainTimer(
                Arg.Any<IGrainContext>(),
                Arg.Any<Func<FlightMonitorAgent, CancellationToken, Task>>(),
                Arg.Any<FlightMonitorAgent>(),
                Arg.Any<GrainTimerCreationOptions>())
            .Returns(call =>
            {
                var cb = call.Arg<Func<FlightMonitorAgent, CancellationToken, Task>>()!;
                var state = call.Arg<FlightMonitorAgent>()!;
                var opts = call.Arg<GrainTimerCreationOptions>()!;
                var t = new CapturedTimer
                {
                    Callback = ct => cb(state, ct),
                    Options = opts,
                };
                captured.Add(t);
                return t;
            });
        return (registry, captured);
    }

    [Fact]
    public async Task HandleAsync_registers_exactly_one_timer_and_returns_Ok()
    {
        var (registry, captured) = BuildTimerRegistry();
        var port = Substitute.For<IFirePort>();
        port.FireBroadcast(Arg.Any<FlightDelayed>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var neuron = new FlightMonitorAgent(port, registry, NoopFactory(), NullLogger<FlightMonitorAgent>.Instance);

        var result = await neuron.HandleAsync(
            new ArmFlightMonitor("SQ-321", "JFK→DPS", TimeSpan.FromSeconds(3)),
            Ctx(port),
            TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Contains("SQ-321", result.Message);
        Assert.Single(captured);
        Assert.NotNull(captured[0].Callback);
    }

    [Fact]
    public async Task Timer_tick_fires_FlightDelayed_broadcast_with_correct_flight_id()
    {
        var (registry, captured) = BuildTimerRegistry();
        var port = Substitute.For<IFirePort>();
        FlightDelayed? published = null;
        port.FireBroadcast(Arg.Do<FlightDelayed>(d => published = d),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var neuron = new FlightMonitorAgent(port, registry, NoopFactory(), NullLogger<FlightMonitorAgent>.Instance);

        await neuron.HandleAsync(
            new ArmFlightMonitor("SQ-321", "JFK→DPS", TimeSpan.FromSeconds(3)),
            Ctx(port),
            TestContext.Current.CancellationToken);

        // Simulate the timer firing once.
        await captured[0].Callback!(TestContext.Current.CancellationToken);

        Assert.NotNull(published);
        Assert.Equal("SQ-321", published!.FlightId);
        Assert.Contains("#1", published.Reason);
        Assert.Matches(@"^\d{2}:\d{2}$", published.NewDepartTime);

        await port.Received(1).FireBroadcast(
            Arg.Any<FlightDelayed>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Second_tick_shifts_time_further_and_increments_reason_counter()
    {
        var (registry, captured) = BuildTimerRegistry();
        var port = Substitute.For<IFirePort>();
        var ticks = new List<FlightDelayed>();
        port.FireBroadcast(Arg.Do<FlightDelayed>(d => ticks.Add(d)),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var neuron = new FlightMonitorAgent(port, registry, NoopFactory(), NullLogger<FlightMonitorAgent>.Instance);

        await neuron.HandleAsync(
            new ArmFlightMonitor("SQ-321", "JFK→DPS", TimeSpan.FromSeconds(3)),
            Ctx(port),
            TestContext.Current.CancellationToken);

        await captured[0].Callback!(TestContext.Current.CancellationToken);
        await captured[0].Callback!(TestContext.Current.CancellationToken);
        await captured[0].Callback!(TestContext.Current.CancellationToken);

        Assert.Equal(3, ticks.Count);
        // each tick rolls the synthetic depart-time forward so the UI shows movement
        var times = ticks.Select(t => t.NewDepartTime).ToList();
        Assert.Equal(times.Count, times.Distinct().Count());
        Assert.Contains("#1", ticks[0].Reason);
        Assert.Contains("#2", ticks[1].Reason);
        Assert.Contains("#3", ticks[2].Reason);
    }

    [Fact]
    public async Task Re_arm_disposes_previous_timer_before_registering_new_one()
    {
        var (registry, captured) = BuildTimerRegistry();
        var port = Substitute.For<IFirePort>();
        FlightDelayed? published = null;
        port.FireBroadcast(Arg.Do<FlightDelayed>(d => published = d),
                Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var neuron = new FlightMonitorAgent(port, registry, NoopFactory(), NullLogger<FlightMonitorAgent>.Instance);

        await neuron.HandleAsync(
            new ArmFlightMonitor("SQ-321", "JFK→DPS", TimeSpan.FromSeconds(3)),
            Ctx(port),
            TestContext.Current.CancellationToken);
        await neuron.HandleAsync(
            new ArmFlightMonitor("EK-204", "JFK→DPS", TimeSpan.FromSeconds(5)),
            Ctx(port),
            TestContext.Current.CancellationToken);

        Assert.Equal(2, captured.Count);
        Assert.True(captured[0].Disposed);

        // The second arm's flight id wins — invoke the second timer and assert.
        await captured[1].Callback!(TestContext.Current.CancellationToken);
        Assert.Equal("EK-204", published!.FlightId);
    }
}
