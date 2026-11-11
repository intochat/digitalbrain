using System.Collections.Concurrent;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

/// <summary>
/// End-to-end silo smoke facts verifying that the Microsoft Orleans virtual actor runtime
/// adapter is correctly wired and integrated on this hosting topology.
/// Covers: 1) Hosting, 2) Activation, 3) Turn Dispatch, 4) Signal Delivery, 5) Persistence, 6) Shutdown.
/// Pure kernel domain rules are tested deterministically in fast unit tests (DigitalBrain.Testing.Tests.Unit / DigitalBrain.Simulation).
/// </summary>
public sealed class SiloSmokeFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // 1. HOSTING: Proves that the silo boots, registers runtime modules/types in DI, and provides valid grain factory & typed proxy resolution.
    [Fact]
    public async Task Silo_HostsAndResolvesCoreServices()
    {
        Assert.NotNull(silo.Services);
        Assert.NotNull(silo.Services.GetService<IGrainFactory>());
        Assert.NotNull(silo.Services.GetService<BrainSignalHistories>());
        Assert.NotNull(silo.Services.GetService<IClusterListeners>());

        var registry = silo.Services.GetRequiredService<NeuronTypeRegistry>();
        Assert.True(registry.TryGet("probe.counter", out var neuronType));
        Assert.Equal(typeof(CounterNeuron), neuronType);

        var counter = silo.Get<ICounter>("smoke/probe.counter/hosting");
        Assert.NotNull(counter);
        Assert.Equal(0, await counter.Value());
    }

    // 2. ACTIVATION: Proves that addressing an un-activated grain transparently triggers OnActivateAsync, binds HostNeuronContext, and loads initial state.
    [Fact]
    public async Task Grain_ActivatesOnFirstAddressAndInitializesContext()
    {
        const string brain = "smoke-act";
        var id = NeuronId.Parse($"{brain}/probe.counter/first-address");
        var counter = silo.Get<ICounter>(id.ToString());

        var initialValue = await counter.Value();
        Assert.Equal(0, initialValue);

        var caller = await counter.Caller();
        Assert.NotNull(caller);
    }

    // 3. TURN DISPATCH: Proves that incoming method calls route through CallCatalog, execute sequentially within turn boundaries, and return outcomes.
    [Fact]
    public async Task TurnDispatch_ExecutesInvocationThroughCatalogAndReturnsOutcome()
    {
        const string brain = "smoke-turn";
        var counter = silo.Get<ICounter>($"{brain}/probe.counter/dispatch");

        var first = await counter.Add(7);
        Assert.Equal(7, first);

        var second = await counter.Add(13);
        Assert.Equal(20, second);
        Assert.Equal(20, await counter.Value());
    }

    // 4. SIGNAL DELIVERY: Proves that a turn-emitted signal is journaled and delivered via [OneWay] nudge to cluster listeners without polling.
    [Fact]
    public async Task SignalDelivery_DeliversCommittedSignalToClusterListener()
    {
        const string brain = "smoke-signal";
        var id = NeuronId.Parse($"{brain}/probe.counter/signal-delivery");
        var counter = silo.Get<ICounter>(id.ToString());

        var received = new TaskCompletionSource<(NeuronId Publisher, Noted Signal)>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clusterListeners = silo.Services.GetRequiredService<IClusterListeners>();

        await using var subscription = await clusterListeners.Listen<Noted>("smoke-watcher", (pub, sig, token) =>
        {
            if (pub == id)
            {
                received.TrySetResult((pub, sig));
            }
            return Task.CompletedTask;
        });

        await counter.Note("silo-signal-received");

        var (publisher, signal) = await received.Task.WaitAsync(DefaultTimeout, Ct);
        Assert.Equal(id, publisher);
        Assert.Equal("silo-signal-received", signal.Text);
    }

    // 5. PERSISTENCE: Proves atomic journal commits survive grain collection and full silo restart.
    [Fact]
    public async Task Persistence_StateAndCommittedSignalsSurviveSiloRestart()
    {
        const string brain = "smoke-persist";
        var id = NeuronId.Parse($"{brain}/probe.counter/restart-survival");
        var counter = silo.Get<ICounter>(id.ToString());

        Assert.Equal(42, await counter.Add(42));
        Assert.Equal(42, await counter.Value());

        var historyBefore = await silo.History(brain).Read(id, 0, 10, Ct);
        Assert.Contains(historyBefore.Signals, s => s.Signal is Counted counted && counted.Value == 42);

        await silo.Restart(Ct);

        var counterAfter = silo.Get<ICounter>(id.ToString());
        Assert.Equal(42, await counterAfter.Value());

        Assert.Equal(50, await counterAfter.Add(8));
        Assert.Equal(50, await counterAfter.Value());
    }

    // 6. SHUTDOWN: Proves clean silo shutdown, grain deactivation, and release of all file locks and temporary directories.
    [Fact]
    public async Task Shutdown_SiloTerminatesCleanlyAndReleasesResources()
    {
        string homePath;
        using (var home = new TempHome())
        {
            homePath = home.Path;
            var factory = new LocalSiloHostFactory(new SiloHostRegistration());
            await using (var host = await factory.Start(new InProcessHostOptions { Home = home.Path, Modules = [typeof(HostingModule)] }, Ct))
            {
                var counter = NeuronHosting.Get<ICounter>(host.Services, NeuronId.Parse("smoke-shut/probe.counter/c1"));
                Assert.Equal(10, await counter.Add(10));
            }
        }
        Assert.False(Directory.Exists(homePath));
    }

    private sealed class SiloHostRegistration : IClusterStoreRegistration
    {
        public void Register(ISiloBuilder builder)
        {
            builder.AddBrainStorage(typeof(HostingModule).Assembly);
        }
    }
}
