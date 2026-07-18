using DigitalBrain.V2.Core.Brain;
using DigitalBrain.V2.Core.Runtime;
using DigitalBrain.V2.Core.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans.Streams;
using System.Reflection;
using Xunit;

namespace DigitalBrain.V2.Testing;

// A test IS a simulation. This is the author's surface: boot the live silo, observe the
// timeline, fire synapses, assert on what comes back. mock = Fire, assert = Expect. There is
// no FakeDigitalBrain and no backend selection — the real substrate routes everything.
//
// Conceptually a simulation is a neuron; in the seed it runs as xUnit's client-side face of
// one. The in-silo SimulationNeuron form is deferred (docs/04 roadmap).
public abstract class Simulation : IAsyncLifetime
{
    private IHost _host = default!;
    private IClusterClient _client = default!;
    private StreamSubscriptionHandle<Synapse>? _subscription;
    private readonly List<Synapse> _seen = new();

    protected IGrainFactory Grains => _client;

    public async ValueTask InitializeAsync()
    {
        _host = await Substrate.StartAsync(ApplicationParts);
        _client = _host.Services.GetRequiredService<IClusterClient>();

        var timeline = _client.GetStreamProvider(SynapseStream.ProviderName).Timeline();
        _subscription = await timeline.SubscribeAsync((synapse, _) =>
        {
            lock (_seen) _seen.Add(synapse);
            return Task.CompletedTask;
        });

        await PrimeAsync();
    }

    // Override to activate the neurons under test so they subscribe to the timeline before
    // the first broadcast is fired at them.
    protected virtual Task PrimeAsync() => Task.CompletedTask;

    protected virtual IEnumerable<Assembly> ApplicationParts => [];

    protected Task Activate<TNeuron>(string key = "default") where TNeuron : INeuron =>
        Grains.GetGrain<TNeuron>(key).EnsureActiveAsync();

    protected Task Fire(Synapse synapse) =>
        Grains.GetGrain<IDigitalBrain>(Brain.WellKnownKey).Fire(synapse);

    protected async Task<T> Expect<T>(Func<T, bool>? where = null, int ms = 3000) where T : Synapse
    {
        var deadline = DateTimeOffset.UtcNow.AddMilliseconds(ms);
        while (true)
        {
            if (Match(where) is { } hit) return hit;
            if (DateTimeOffset.UtcNow >= deadline) break;
            await Task.Delay(25);
        }

        Assert.Fail($"Expected {typeof(T).Name} on the timeline within {ms}ms, but it never arrived.");
        throw new InvalidOperationException("unreachable");
    }

    protected async Task ExpectNone<T>(Func<T, bool>? where = null, int ms = 500) where T : Synapse
    {
        await Task.Delay(ms);
        if (Match(where) is { } stray)
            Assert.Fail($"Did not expect {typeof(T).Name} on the timeline, but observed {stray.GetType().Name}.");
    }

    private T? Match<T>(Func<T, bool>? where) where T : Synapse
    {
        lock (_seen) return _seen.OfType<T>().FirstOrDefault(s => where?.Invoke(s) ?? true);
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscription is not null) await _subscription.UnsubscribeAsync();
        await _host.StopAsync();
        _host.Dispose();
    }
}
