using DigitalBrain.Runtime.Orleans;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace DigitalBrain.Framework.Tests.E2E;

/// <summary>The silo's clock for facts: the system clock plus an offset <see cref="RunningTarget.Advance"/> moves. Timers stay real.</summary>
internal sealed class OffsetClock : TimeProvider
{
    private long _offsetTicks;

    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    public void Advance(TimeSpan by) => Interlocked.Add(ref _offsetTicks, by.Ticks);
}

/// <summary>The shared mapping of the controls onto an Orleans in-process host: <c>RuntimeTestHooks</c> over <see cref="IInProcessHost.Services"/>.</summary>
internal sealed class OrleansRun(IInProcessHost host, FrameworkHostOptions options) : RunningTarget
{
    private IServiceProvider Services => host.Services;

    /// <summary>Retention for the options path <c>IntoChatOS:Signalling:Retention</c>.</summary>
    public static Dictionary<string, string?> Configuration(FrameworkHostOptions options)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        const string Retention = IntoChatOSNames.Signalling.Retention;
        if (options.HistoryWindowSignals is { } signals) { values[Retention + ":HistoryWindowSignals"] = signals.ToString(System.Globalization.CultureInfo.InvariantCulture); }
        if (options.HistoryWindowAge is { } age) { values[Retention + ":HistoryWindowAge"] = age.ToString("c"); }
        if (options.StaleCursorAfter is { } stale) { values[Retention + ":StaleCursorAfter"] = stale.ToString("c"); }
        return values;
    }

    /// <summary>The services callback: the silo reads time from <paramref name="clock"/>.</summary>
    public static Action<IServiceCollection> ClockServices(OffsetClock clock) => services =>
    {
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(clock);
    };

    public required OffsetClock Clock { get; init; }

    public static RunningTarget Started(IInProcessHost host, FrameworkHostOptions options, OffsetClock clock)
    {
        var run = new OrleansRun(host, options) { Clock = clock };
        run.ApplyFaults();
        return run;
    }

    public override T Get<T>(NeuronId id) => RuntimeTestHooks.Get<T>(Services, id);

    public override Task Deactivate(NeuronId id) => RuntimeTestHooks.Deactivate(Services, id);

    public override async Task Restart(CancellationToken ct)
    {
        await host.Restart(ct);
        ApplyFaults();
    }

    public override void FailNextPublishAfterAppend(NeuronId publisher) => RuntimeTestHooks.FailNextPublishAfterAppend(Services, publisher);

    public override void Advance(TimeSpan by) => Clock.Advance(by);

    public override Task ForceCheckpoint(BrainId brain, CancellationToken ct) => RuntimeTestHooks.ForceCheckpoint(Services, brain.Value, ct);

    public override Task Idle() => Task.CompletedTask;

    public override async Task<int> SubscriptionCount(NeuronId subscriber, NeuronId source, Type signal)
    {
        var manager = Services.GetRequiredService<IStreamSubscriptionManagerAdmin>().GetStreamSubscriptionManager(StreamSubscriptionManagerType.ExplicitSubscribeOnly);
        var all = await manager.GetSubscriptions(IntoChatOSNames.SignalStreams, StreamNames.For(source.Brain.Value, signal));
        return all.Count(s => s.GrainId.Key.ToString() == subscriber.ToString());
    }

    public override ValueTask DisposeAsync() => host.DisposeAsync();

    private void ApplyFaults() => RuntimeTestHooks.DuplicateEach(Services, options.DuplicateDelivery);
}

