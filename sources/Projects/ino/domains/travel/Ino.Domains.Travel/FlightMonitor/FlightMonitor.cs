using System.Diagnostics;
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Llm;
using Ino.Domains.Travel.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Orleans.Timers;

namespace Ino.Domains.Travel.FlightMonitor;

/// <summary>
/// Watches a single flight and fires <see cref="FlightDelayed"/> as a reactive
/// broadcast on each tick. For the v0.1 demo the "delay" is synthetic — every
/// <c>ArmFlightMonitor.TickInterval</c> the neuron pushes out a mutated schedule so
/// the Flutter flight card can animate (slice 10 wires the UI reactivity via
/// StreamEventsAsync).
///
/// Implementation note on the "IRemindable" phrasing in the plan slice 9: Orleans
/// reminders enforce a 1-minute minimum period by default (overridable via
/// <c>ReminderOptions.MinimumReminderPeriod</c>) and are designed for durable,
/// re-activation-triggering ticks. For a short demo pulse (3 seconds) with no
/// requirement to survive grain deactivation, a grain-scoped timer is both lighter
/// and idiomatic — the grain stays activated for its default idle timeout, which
/// is already longer than any demo session. Slice 15 or beyond can swap this for
/// a reminder if the Monitor needs to resume monitoring after a silo bounce.
/// </summary>
public sealed class FlightMonitor(
    IFirePort firePort,
    ITimerRegistry timers,
    IChatClientFactory chatFactory,
    ILogger<FlightMonitor> log) : Grain, INeuron<ArmFlightMonitor>
{
    static readonly ActivitySource ActivitySource = new("ino");
    static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(1);

    IGrainTimer? _timer;
    ArmFlightMonitor? _armed;
    int _tickCount;

    public Task<NeuronResult> HandleAsync(
        ArmFlightMonitor synapse,
        NeuronContext ctx,
        CancellationToken ct)
    {
        using var span = ActivitySource.StartActivity("ino.neuron.handle", ActivityKind.Internal);
        span?.SetTag("ino.neuron.type", nameof(FlightMonitor));
        span?.SetTag("ino.synapse.type", nameof(ArmFlightMonitor));
        span?.SetTag("ino.correlation_id", ctx.CorrelationId.Value);
        span?.SetTag("ino.flight.id", synapse.FlightId);

        // Re-arming replaces any previous timer — the monitor is single-tenant per
        // activation (keyed by FlightId via GetGrain<INeuron<ArmFlightMonitor>>(flightId)).
        _timer?.Dispose();
        _armed = synapse;
        _tickCount = 0;

        var interval = synapse.TickInterval < MinimumInterval ? MinimumInterval : synapse.TickInterval;
        _timer = timers.RegisterGrainTimer(
            grainContext: GrainContext,
            callback: static async (state, token) => await state.FireTickAsync(token),
            state: this,
            options: new GrainTimerCreationOptions
            {
                DueTime = interval,
                Period = interval,
            });

        log.LogInformation(
            "FlightMonitor armed for {Flight} on {Route}, tick {Interval}",
            synapse.FlightId, synapse.Route, interval);

        return Task.FromResult(NeuronResult.Ok($"Monitoring {synapse.FlightId} every {interval}"));
    }

    async Task FireTickAsync(CancellationToken ct)
    {
        if (_armed is null) return;

        _tickCount++;
        var broadcastContext = new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("domains")),
            SourceStream: new StreamKey($"<flight-monitor/{_armed.FlightId}>"))
        {
            FirePort = firePort,
            Logger = log,
        };

        var newDepartTime = ShiftTime(_tickCount);
        var reason = await NarrateDelayAsync(_armed, _tickCount, newDepartTime, ct);
        var delayed = new FlightDelayed(
            FlightId: _armed.FlightId,
            NewDepartTime: newDepartTime,
            Reason: reason);

        try
        {
            await firePort.FireBroadcast(delayed, broadcastContext, ct);
            log.LogInformation("FlightMonitor tick {Tick} fired FlightDelayed for {Flight}",
                _tickCount, _armed.FlightId);
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "FlightMonitor tick {Tick} failed to broadcast FlightDelayed for {Flight}",
                _tickCount, _armed?.FlightId);
        }
    }

    async Task<string> NarrateDelayAsync(
        ArmFlightMonitor armed, int tick, string newDepartTime, CancellationToken ct)
    {
        var fallback = $"Synthetic demo delay #{tick} on {armed.Route}";
        var system =
            "You are a flight-status narrator. Write ONE short, calm notification " +
            "explaining that the flight is delayed. Mention the new departure time. " +
            "Do not speculate about causes beyond what you're told.";
        var user =
            $"Flight {armed.FlightId} on route {armed.Route} is now departing at {newDepartTime}. " +
            $"This is the {tick}th status update from the monitor.";

        try
        {
            var chat = chatFactory.ForTier(LlmTier.Balanced);
            var response = await chat.GetResponseAsync(
                new[]
                {
                    new ChatMessage(ChatRole.System, system),
                    new ChatMessage(ChatRole.User, user),
                },
                options: null,
                ct);
            return string.IsNullOrWhiteSpace(response.Text) ? fallback : response.Text.Trim();
        }
        catch (Exception ex) when (ex is BddMockMissException or NotSupportedException)
        {
            log.LogDebug("FlightMonitor narrative skipped: {Reason}", ex.GetType().Name);
            return fallback;
        }
    }

    static string ShiftTime(int tickCount)
    {
        // 10:30 base + 15 minutes per tick — enough movement to see in the demo UI.
        var minutes = 30 + (tickCount * 15);
        var hours = 10 + (minutes / 60);
        minutes %= 60;
        return $"{hours:00}:{minutes:00}";
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
    {
        _timer?.Dispose();
        _timer = null;
        return base.OnDeactivateAsync(reason, ct);
    }
}
