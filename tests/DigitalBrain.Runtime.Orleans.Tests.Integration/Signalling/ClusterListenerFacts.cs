using System.Collections.Concurrent;
using DigitalBrain.Runtime.Orleans.Signalling;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Signalling;

/// <summary>A silo whose host registers a cluster listener at every start, as os <c>FeatureBoot</c> does.</summary>
public sealed class ClusterListenerSilo : SessionSilo
{
    /// <summary>Every <see cref="Counted"/> the start-up listener heard, as <c>brain:value</c>.</summary>
    public ConcurrentQueue<string> Heard { get; } = new();

    /// <summary>How many times the host started and registered.</summary>
    private int _starts;

    public int Starts => Volatile.Read(ref _starts);

    private void Started() => Interlocked.Increment(ref _starts);

    protected override Action<IServiceCollection>? ConfigureServices => services => services.AddSingleton<IHostedService>(sp => new StartupListener(this, sp));

    private sealed class StartupListener(ClusterListenerSilo owner, IServiceProvider services) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            owner.Started();
            var listeners = services.GetRequiredService<IClusterListeners>();
            // Registered twice on purpose: idempotent per listener, so each signal is still heard once.
            for (var i = 0; i < 2; i++)
            {
                await listeners.Listen<Counted>("boot", (publisher, signal, _) =>
                {
                    owner.Heard.Enqueue($"{publisher.Brain}:{signal.Value}");
                    return Task.CompletedTask;
                });
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}

// 04-19, R4-19: a host-level listener hears a signal published by any brain, re-registered at host start.
public sealed class ClusterListenerFacts(ClusterListenerSilo silo) : IClassFixture<ClusterListenerSilo>
{
    private static async Task Until(Func<bool> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!condition() && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }
    }

    [Fact]
    public async Task Signals_of_every_brain_reach_the_start_up_listener_once_each_and_again_after_a_silo_restart()
    {
        var ct = TestContext.Current.CancellationToken;
        bool Has(string item) => silo.Heard.Contains(item);

        await silo.Get<ICounter>("cl-one/probe.counter/c").Add(1);
        await silo.Get<ICounter>("cl-fresh-" + Guid.NewGuid().ToString("N")[..6] + "/probe.counter/c").Add(5);
        await silo.Get<ICounter>("cl-two/probe.counter/c").Add(2);
        await Until(() => Has("cl-one:1") && Has("cl-two:2") && silo.Heard.Any(item => item.EndsWith(":5", StringComparison.Ordinal)), ct);
        await Task.Delay(300, ct);
        Assert.Single(silo.Heard, item => item == "cl-one:1");
        Assert.Single(silo.Heard, item => item == "cl-two:2");
        Assert.Single(silo.Heard, item => item.StartsWith("cl-fresh-", StringComparison.Ordinal));
        Assert.Equal(1, silo.Starts);

        await silo.Restart(ct);
        Assert.Equal(2, silo.Starts);
        Assert.Equal(1, silo.Services.GetRequiredService<ClusterListeners>().Count);
        await silo.Get<ICounter>("cl-one/probe.counter/c").Add(1);
        await Until(() => Has("cl-one:2"), ct);
        await Task.Delay(300, ct);
        Assert.Single(silo.Heard, item => item == "cl-one:2");
    }

    [Fact]
    public async Task A_disposed_or_throwing_handler_does_not_stop_the_others()
    {
        var ct = TestContext.Current.CancellationToken;
        var listeners = silo.Services.GetRequiredService<ClusterListeners>();
        var failures = listeners.Failures;
        await using var boom = await listeners.Listen<Counted>("boom", (_, _, _) => throw new InvalidOperationException("boom"));
        var gone = new ConcurrentQueue<string>();
        var disposed = await listeners.Listen<Counted>("gone", (publisher, signal, _) =>
        {
            gone.Enqueue(publisher.Brain.Value);
            return Task.CompletedTask;
        });
        await disposed.DisposeAsync();

        await silo.Get<ICounter>("cl-throw/probe.counter/c").Add(3);
        await Until(() => silo.Heard.Contains("cl-throw:3"), ct);
        await Task.Delay(300, ct);
        Assert.Empty(gone);
        Assert.True(listeners.Failures > failures);
    }
}
