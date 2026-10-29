using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Placement;
using DigitalBrain.Runtime.Orleans.Signalling;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Signalling;

internal static class Listening
{
    public static IBrainListeners Listeners(this SynapseSilo silo) => silo.Services.GetRequiredService<IBrainListeners>();

    public static Task<IAsyncDisposable> Record(this IBrainListeners listeners, string brain, ConcurrentQueue<string> into)
        => listeners.Listen<Counted>(new BrainId(brain), (signal, _) =>
        {
            into.Enqueue("counted:" + signal.Value);
            return Task.CompletedTask;
        });

    public static async Task Until(ConcurrentQueue<string> queue, int count, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (queue.Count < count && DateTime.UtcNow < deadline) { await Task.Delay(50, ct); }
    }
}

// 04 D-02, R4-03, T-04-43, RESEARCH Pitfall 8: silo-local typed listeners over one brain-wide synapse.
public sealed class BrainListenerFacts(SessionSilo silo) : IClassFixture<SessionSilo>
{
    [Fact]
    public async Task Listen_ensures_the_listener_neuron_and_every_signal_of_the_brain_reaches_the_handler_once_in_brain_order()
    {
        var ct = TestContext.Current.CancellationToken;
        await silo.Get<ICounter>("bl-order/probe.counter/early").Add(100); // before the listener: never replayed
        var heard = new ConcurrentQueue<string>();
        await using var registration = await silo.Listeners().Record("bl-order", heard);
        Assert.Equal(1, await silo.Subscriptions("bl-order/runtime.listener/bl-order", "probe.counted"));

        var a = silo.Get<ICounter>("bl-order/probe.counter/a");
        var b = silo.Get<ICounter>("bl-order/probe.counter/b");
        await a.Add(1);
        await b.Add(10);
        await a.Add(1);
        await b.Add(10);
        await Listening.Until(heard, 4, ct);
        await Task.Delay(500, ct);
        Assert.Equal(["counted:1", "counted:10", "counted:2", "counted:20"], heard);
    }

    [Fact]
    public async Task Disposing_a_registration_stops_delivery_to_that_handler_only()
    {
        var ct = TestContext.Current.CancellationToken;
        var first = new ConcurrentQueue<string>();
        var second = new ConcurrentQueue<string>();
        var one = await silo.Listeners().Record("bl-dispose", first);
        await using var two = await silo.Listeners().Record("bl-dispose", second);
        var counter = silo.Get<ICounter>("bl-dispose/probe.counter/c");
        await counter.Add(1);
        await Listening.Until(second, 1, ct);
        await Listening.Until(first, 1, ct);

        await one.DisposeAsync();
        await counter.Add(1);
        await Listening.Until(second, 2, ct);
        await Task.Delay(300, ct);
        Assert.Equal(["counted:1"], first);
        Assert.Equal(["counted:1", "counted:2"], second);
    }

    [Fact]
    public async Task A_listener_of_brain_A_never_receives_brain_B_and_a_throwing_handler_does_not_stop_the_others()
    {
        var ct = TestContext.Current.CancellationToken;
        var listeners = silo.Listeners();
        var failures = ((BrainListeners)listeners).Failures;
        await using var boom = await listeners.Listen<Counted>(new BrainId("bl-a"), (_, _) => throw new InvalidOperationException("boom"));
        var heard = new ConcurrentQueue<string>();
        await using var good = await listeners.Record("bl-a", heard);

        await silo.Get<ICounter>("bl-b/probe.counter/other").Add(5);
        await silo.Get<ICounter>("bl-a/probe.counter/mine").Add(7);
        await Listening.Until(heard, 1, ct);
        await Task.Delay(500, ct);
        Assert.Equal(["counted:7"], heard);
        Assert.True(((BrainListeners)listeners).Failures > failures);
    }

    [Fact]
    public async Task The_listener_runs_on_the_silo_that_hosts_the_brain_and_delivers_to_handlers_registered_there()
    {
        var ct = TestContext.Current.CancellationToken;
        var heard = new ConcurrentQueue<string>();
        await using var registration = await silo.Listeners().Record("bl-local", heard);
        var local = silo.Services.GetRequiredService<ILocalSiloDetails>().SiloAddress.ToString();
        Assert.Equal(local, await NeuronHosting.Get<IBrainListenerHost>(silo.Services, NeuronId.Parse("bl-local/runtime.listener/bl-local")).Silo());
        await silo.Get<ICounter>("bl-local/probe.counter/c").Add(3);
        await Listening.Until(heard, 1, ct);
        Assert.Equal(["counted:3"], heard);
    }
}

// The listener's synapse survives a silo restart: it resumes from its brain cursor with no duplicates.
public sealed class BrainListenerRestartFacts(SessionSilo silo) : IClassFixture<SessionSilo>
{
    [Fact]
    public async Task After_a_silo_restart_the_listener_resumes_from_its_brain_cursor_without_duplicates()
    {
        var ct = TestContext.Current.CancellationToken;
        var before = new ConcurrentQueue<string>();
        await using (await silo.Listeners().Record("bl-restart", before))
        {
            await silo.Get<ICounter>("bl-restart/probe.counter/c").Add(1);
            await Listening.Until(before, 1, ct);
        }
        Assert.Equal(["counted:1"], before);

        await silo.Restart(ct);
        var after = new ConcurrentQueue<string>();
        await using var registration = await silo.Listeners().Record("bl-restart", after);
        Assert.Equal(1, await silo.Subscriptions("bl-restart/runtime.listener/bl-restart", "probe.counted"));
        await silo.Get<ICounter>("bl-restart/probe.counter/c").Add(1);
        await Listening.Until(after, 1, ct);
        await Task.Delay(500, ct);
        Assert.Equal(["counted:2"], after);
    }
}

// SessionTtl with no handler: the listener removes its synapse.
public sealed class BrainListenerTtlFacts(SessionTtlSilo silo) : IClassFixture<SessionTtlSilo>
{
    [Fact]
    public async Task With_no_handler_for_a_full_SessionTtl_the_listener_removes_its_synapse()
    {
        var ct = TestContext.Current.CancellationToken;
        const string Listener = "bl-sweep/runtime.listener/bl-sweep";
        var registration = await silo.Listeners().Record("bl-sweep", new ConcurrentQueue<string>());
        Assert.Equal(1, await silo.Subscriptions(Listener, "probe.counted"));

        RuntimeTestHooks.Advance(silo.Services, TimeSpan.FromMinutes(11)); // a handler is still registered: kept
        await Task.Delay(1000, ct);
        Assert.Equal(1, await silo.Subscriptions(Listener, "probe.counted"));

        await registration.DisposeAsync();
        await Task.Delay(1000, ct); // a sweep sees no handler and starts counting
        RuntimeTestHooks.Advance(silo.Services, TimeSpan.FromMinutes(11));
        await Sessions.Until(async () => await silo.Subscriptions(Listener, "probe.counted") == 0, ct);
        Assert.Equal(0, await silo.Subscriptions(Listener, "probe.counted"));
    }
}

// The placement rule: in a cluster the listener neuron of a brain runs on that brain's affine silo, so delivery goes to the
// handlers registered there. Two silos, six brains: the listener and a neuron of the same brain always share a silo.
public sealed class BrainListenerPlacementFacts
{
    [Fact]
    public async Task In_two_silos_the_listener_of_a_brain_runs_on_the_brain_affine_silo()
    {
        var ct = TestContext.Current.CancellationToken;
        var primaryPort = FreePort();
        var primary = new IPEndPoint(IPAddress.Loopback, primaryPort);
        using var first = TwoSilo(primaryPort, primary);
        await first.StartAsync(ct);
        using var second = TwoSilo(FreePort(), primary);
        await second.StartAsync(ct);
        try
        {
            var management = first.Services.GetRequiredService<IGrainFactory>().GetGrain<IManagementGrain>(0);
            await ProbeLog.Until(() => management.GetHosts(onlyActive: true).GetAwaiter().GetResult().Count == 2, ct);
            await Task.Delay(1000, ct);

            HashSet<string> silos = [];
            foreach (var brain in Enumerable.Range(0, 6).Select(i => "lbrain" + i))
            {
                var neuron = await NeuronHosting.Get<IProbe>(first.Services, NeuronId.Parse($"{brain}/probe/one")).Silo();
                var listener = await NeuronHosting.Get<IBrainListenerHost>(second.Services, NeuronId.Parse($"{brain}/runtime.listener/{brain}")).Silo();
                Assert.Equal(neuron, listener);
                silos.Add(listener);
            }
            Assert.Equal(2, silos.Count);
        }
        finally
        {
            await second.StopAsync(CancellationToken.None);
            await first.StopAsync(CancellationToken.None);
        }
    }

    private static IHost TwoSilo(int siloPort, IPEndPoint primary)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.UseOrleans(orleans =>
        {
            orleans.UseLocalhostClustering(siloPort, FreePort(), primary, "db-listener-facts", "db-listener-facts");
            orleans.AddPlacementDirector<BrainAffinePlacement, BrainAffinePlacementDirector>();
            orleans.AddNeuronHosting(typeof(ProbeNeuron).Assembly);
            orleans.Services.AddJournalPayloads(typeof(ProbeNeuron).Assembly);
            orleans.AddRuntimeServices();
        });
        return builder.Build();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
