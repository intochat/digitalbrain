using System.Collections.Concurrent;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Runtime;
using Orleans.Streams;
using Orleans.Streams.Core;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;

/// <summary>What listeners received, by listener id, in arrival order.</summary>
internal static class Heard
{
    public static readonly ConcurrentDictionary<string, ConcurrentQueue<string>> Items = new();
    public static readonly ConcurrentDictionary<string, int> Booms = new();

    public static void Add(string listener, string item) => Items.GetOrAdd(listener, static _ => new()).Enqueue(item);

    public static string[] Of(string listener) => Items.TryGetValue(listener, out var queue) ? [.. queue] : [];

    public static async Task Until(string listener, int count, CancellationToken ct, int seconds = 20)
    {
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        while (Of(listener).Length < count && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }
    }
}

public interface IListener
{
    Task Follow(string source);

    Task FollowNotes(string source);

    Task Unfollow(string source);

    Task<int> Ping();
}

/// <summary>A plain subscriber: records each [On] reaction as text.</summary>
[Neuron("probe.listener")]
public sealed class ListenerNeuron : Neuron, IListener
{
    public Task Follow(string source) => Context.Synapses.Ensure<Counted>(NeuronId.Parse(source));

    public Task FollowNotes(string source) => Context.Synapses.Ensure<Noted>(NeuronId.Parse(source));

    public Task Unfollow(string source) => Context.Synapses.Remove(NeuronId.Parse(source));

    public Task<int> Ping() => Task.FromResult(1);

    [On]
    public Task On(Counted signal)
    {
        Heard.Add(Id.ToString(), "counted:" + signal.Value);
        return Task.CompletedTask;
    }

    [On]
    public Task On(Noted signal, CancellationToken cancellationToken)
    {
        if (signal.Text.StartsWith("boom", StringComparison.Ordinal) && Heard.Booms.AddOrUpdate(Id.ToString(), 1, static (_, n) => n + 1) == 1)
        {
            throw new InvalidOperationException("boom");
        }
        Heard.Add(Id.ToString(), "noted:" + signal.Text);
        return Task.CompletedTask;
    }

    [On]
    public Task On(Gap gap)
    {
        Heard.Add(Id.ToString(), $"gap:{gap.From}-{gap.To}");
        return Task.CompletedTask;
    }
}

[Neuron("probe.stateless.listener"), StatelessNeuron]
public sealed class StatelessListener : Neuron, IListener
{
    public Task Follow(string source) => Context.Synapses.Ensure<Counted>(NeuronId.Parse(source));

    public Task FollowNotes(string source) => Context.Synapses.Ensure<Noted>(NeuronId.Parse(source));

    public Task Unfollow(string source) => Context.Synapses.Remove(NeuronId.Parse(source));

    public Task<int> Ping() => Task.FromResult(1);
}

public interface IGuarded
{
    Task<int> Add(int by);
}

/// <summary>A source that admits only subscribers whose key starts with "ok".</summary>
[Neuron("probe.guarded", State = "count")]
public sealed class GuardedNeuron : Neuron<CounterState>, IGuarded
{
    public async Task<int> Add(int by)
    {
        await Save(State with { Value = State.Value + by }, new Counted(State.Value + by));
        return State.Value;
    }

    protected override async Task AdmitSynapse(NeuronId subscriber, IReadOnlyList<Type> signals)
    {
        await base.AdmitSynapse(subscriber, signals);
        if (!subscriber.Key.StartsWith("ok", StringComparison.Ordinal)) { throw new UnauthorizedAccessException($"{subscriber} is not admitted."); }
    }
}

/// <summary>A local silo with BrainLog as the cluster store and optional configuration (retention options path).</summary>
public class SynapseSilo : IAsyncLifetime
{
    private readonly TempHome _home = new();
    private IInProcessHost? _host;

    protected virtual IReadOnlyDictionary<string, string?> Configuration { get; } = new Dictionary<string, string?>();

    /// <summary>Extra silo services, applied again on every <see cref="Restart"/>.</summary>
    protected virtual Action<IServiceCollection>? ConfigureServices => null;

    public IServiceProvider Services => (_host ?? throw new InvalidOperationException("Not started.")).Services;

    public string Home => _home.Path;

    public T Get<T>(string id) where T : class => NeuronHosting.Get<T>(Services, NeuronId.Parse(id));

    public Task Restart(CancellationToken ct) => _host!.Restart(ct);

    internal SynapseCounters Counters => Services.GetRequiredService<SynapseCounters>();

    internal PublishFaults Faults => Services.GetRequiredService<PublishFaults>();

    /// <summary>Subscriptions of <paramref name="listener"/> on the stream of <paramref name="alias"/>.</summary>
    public async Task<int> Subscriptions(string listener, string alias)
    {
        var manager = Services.GetRequiredService<IStreamSubscriptionManagerAdmin>().GetStreamSubscriptionManager(StreamSubscriptionManagerType.ExplicitSubscribeOnly);
        var id = NeuronId.Parse(listener);
        var all = await manager.GetSubscriptions(IntoChatOSNames.SignalStreams, StreamNames.For(id.Brain.Value, alias));
        return all.Count(s => s.GrainId.Key.ToString() == listener);
    }

    /// <summary>Sends a reference by hand, as a duplicate or forged nudge would arrive.</summary>
    internal Task Nudge(string publisher, long sequence, string alias)
    {
        var id = NeuronId.Parse(publisher);
        var provider = Services.GetRequiredKeyedService<IStreamProvider>(IntoChatOSNames.SignalStreams);
        return provider.GetStream<SignalReference>(StreamNames.For(id.Brain.Value, alias)).OnNextAsync(new SignalReference(id, sequence));
    }

    /// <summary>Deactivates every idle activation.</summary>
    public Task DeactivateAll() => Services.GetRequiredService<IGrainFactory>().GetGrain<IManagementGrain>(0).ForceActivationCollection(TimeSpan.Zero);

    public async ValueTask InitializeAsync()
        => _host = await new LocalSiloHostFactory(new Stores(), ConfigureServices).Start(
            new InProcessHostOptions { Home = _home.Path, Modules = [typeof(HostingModule)], Configuration = Configuration }, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) { await _host.DisposeAsync(); }
        _home.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class Stores : IClusterStoreRegistration
    {
        public void Register(ISiloBuilder silo) => silo.AddBrainStorage(typeof(HostingModule).Assembly);
    }
}

/// <summary>Retention through the options path: keep two signals, nothing by age, no cursor stays live.</summary>
public sealed class ShortRetentionSilo : SynapseSilo
{
    protected override IReadOnlyDictionary<string, string?> Configuration { get; } = new Dictionary<string, string?>
    {
        [IntoChatOSNames.Signalling.Retention + ":HistoryWindowSignals"] = "2",
        [IntoChatOSNames.Signalling.Retention + ":HistoryWindowAge"] = "00:00:00",
        [IntoChatOSNames.Signalling.Retention + ":StaleCursorAfter"] = "00:00:00",
    };
}
