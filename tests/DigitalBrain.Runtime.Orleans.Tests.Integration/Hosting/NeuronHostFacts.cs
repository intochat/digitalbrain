using System.Net;
using System.Net.Sockets;
using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Placement;
using DigitalBrain.Runtime.Orleans.Testing;
using DigitalBrain.Spi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Hosting;
using Orleans.Metadata;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

/// <summary>One in-process silo hosting the probe classes of this assembly, shared by the hosting facts.</summary>
public sealed class HostedSilo : IAsyncLifetime
{
    private IInProcessHost? _host;

    public IServiceProvider Services => (_host ?? throw new InvalidOperationException("Not started.")).Services;

    public IGrainFactory Grains => Services.GetRequiredService<IGrainFactory>();

    public T Get<T>(string id) where T : class => NeuronHosting.Get<T>(Services, NeuronId.Parse(id));

    public async ValueTask InitializeAsync()
        => _host = await new LocalSiloHostFactory(new Hosted()).Start(new InProcessHostOptions(), CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        if (_host is not null) { await _host.DisposeAsync(); }
    }

    internal sealed class Hosted : IClusterStoreRegistration
    {
        public void Register(ISiloBuilder silo) => silo.AddNeuronHosting(typeof(ProbeNeuron).Assembly);
    }
}

public sealed class NeuronHostFacts(HostedSilo silo) : IClassFixture<HostedSilo>
{
    private const string Brain = "b1";

    [Fact]
    public async Task A_plain_neuron_answers_through_Get_and_its_activation_is_neuron_host_keyed_brain_type_key()
    {
        var probe = silo.Get<IProbe>("b1/probe/a");
        Assert.Equal("b1/probe/a", await probe.Key());
        var id = NeuronId.Parse("b1/probe/a");
        var host = (await new OrleansInvocationSink(silo.Grains, silo.Services.GetRequiredService<NeuronTypeRegistry>(), null).Host(id, Calls.Of("", "b1/probe/a", "Key"), CancellationToken.None)).Host;
        var grain = host.GetGrainId();
        Assert.Equal("neuron.host", grain.Type.ToString());
        Assert.Equal("b1/probe/a", grain.Key.ToString());
        Assert.Contains("probe", silo.Services.GetRequiredService<NeuronTypeRegistry>().Names);
    }

    [Fact]
    public async Task Task_TaskOfT_ValueTask_answer_and_a_caller_cancelled_token_reaches_the_method()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = silo.Get<IProbe>("b1/probe/shapes");
        await probe.Touch();
        Assert.Equal(5, await probe.Add(2, 3));
        Assert.Equal("hi@b1/probe/shapes", await probe.Echo("hi"));
        await probe.Nothing();

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var waiting = probe.WaitCancelled(cancel.Token);
        await Task.Delay(300, ct);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await ProbeLog.Until(() => ProbeLog.Cancelled.ContainsKey("b1/probe/shapes"), ct);
    }

    [Fact]
    public async Task A_stream_flows_through_Stream_and_disposing_after_two_items_stops_the_producer()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = silo.Get<IProbe>("b1/probe/stream");
        List<int> items = [];
        await foreach (var item in probe.Count(ct))
        {
            items.Add(item);
            if (items.Count == 2) { break; }
        }
        Assert.Equal([0, 1], items);
        await ProbeLog.Until(() => ProbeLog.StreamStopped.ContainsKey("b1/probe/stream"), ct);
    }

    [Fact]
    public async Task A_one_way_call_returns_before_the_neuron_runs_and_CallTimeout_throws_TimeoutException()
    {
        var ct = TestContext.Current.CancellationToken;
        var probe = silo.Get<IProbe>("b1/probe/oneway");
        await probe.Fire().WaitAsync(TimeSpan.FromSeconds(5), ct);
        Assert.False(ProbeLog.Fired.ContainsKey("b1/probe/oneway"));
        ProbeLog.Gate("b1/probe/oneway").SetResult();
        await ProbeLog.Until(() => ProbeLog.Fired.ContainsKey("b1/probe/oneway"), ct);

        var slow = silo.Get<IProbe>("b1/probe/slow");
        await Assert.ThrowsAsync<TimeoutException>(() => slow.Slow());
    }

    [Fact]
    public async Task A_StatelessNeuron_is_hosted_by_neuron_host_stateless_and_answers_from_several_activations_at_once()
    {
        var id = NeuronId.Parse("b1/probe.stateless/s");
        var host = (await new OrleansInvocationSink(silo.Grains, silo.Services.GetRequiredService<NeuronTypeRegistry>(), null)
            .Host(id, new RuntimeInvocation("", NeuronProxy.ContractName(typeof(IStatelessProbe)), id.ToString(), "Busy", []), CancellationToken.None)).Host;
        Assert.Equal("neuron.host.stateless", host.GetGrainId().Type.ToString());

        var probe = silo.Get<IStatelessProbe>("b1/probe.stateless/s");
        var instances = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => probe.Busy()));
        Assert.True(StatelessProbe.MostConcurrent > 1, $"at most {StatelessProbe.MostConcurrent} ran at once");
        Assert.True(instances.Distinct().Count() > 1);
    }

    [Fact]
    public async Task Host_grains_are_brain_affine_and_two_neurons_of_one_brain_share_a_silo_in_a_two_silo_cluster()
    {
        var ct = TestContext.Current.CancellationToken;
        var properties = silo.Services.GetRequiredService<GrainPropertiesResolver>();
        foreach (var type in new[] { "neuron.host", "neuron.host.reentrant" })
        {
            Assert.True(properties.GetGrainProperties(GrainType.Create(type)).Properties.TryGetValue(WellKnownGrainTypeProperties.PlacementStrategy, out var strategy));
            Assert.Equal(nameof(BrainAffinePlacement), strategy);
        }

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
            await Task.Delay(1000, ct); // both membership views settle

            HashSet<string> silos = [];
            foreach (var brain in Enumerable.Range(0, 6).Select(i => "brain" + i))
            {
                var one = await NeuronHosting.Get<IProbe>(first.Services, NeuronId.Parse($"{brain}/probe/one")).Silo();
                var two = await NeuronHosting.Get<IProbe>(second.Services, NeuronId.Parse($"{brain}/probe/two")).Silo();
                Assert.Equal(one, two);
                silos.Add(one);
            }
            Assert.Equal(2, silos.Count);
        }
        finally
        {
            await second.StopAsync(CancellationToken.None);
            await first.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_key_naming_another_brain_or_an_unknown_type_is_refused_before_neuron_code_runs()
    {
        await Assert.ThrowsAsync<BindingException>(() => silo.Get<IProbe>("b1/nope/a").Touch());
        await Assert.ThrowsAnyAsync<Exception>(() => silo.Grains.GetGrain<INeuronHost>(GrainId.Create("neuron.host", "b1/nope/a")).Invoke(Calls.Of("", "b1/nope/a", "Touch"), TestContext.Current.CancellationToken));

        var host = silo.Grains.GetGrain<INeuronHost>(GrainId.Create("neuron.host", "b1/probe/guarded"));
        var error = await Assert.ThrowsAnyAsync<Exception>(() => host.Invoke(Calls.Of("", "b2/probe/guarded", "Touch"), TestContext.Current.CancellationToken));
        Assert.Contains("b2/probe/guarded", error.Message, StringComparison.Ordinal);
        Assert.False(ProbeLog.Touched.ContainsKey("b1/probe/guarded"));
        Assert.False(ProbeLog.Touched.ContainsKey("b2/probe/guarded"));
    }

    [Fact]
    public async Task A_Behavior_naming_another_caller_changes_nothing_provenance_comes_from_RequestContext()
    {
        var host = silo.Grains.GetGrain<INeuronHost>(GrainId.Create("neuron.host", $"{Brain}/probe/target"));
        var forged = await host.Invoke(Calls.Of("b1/admin/root", $"{Brain}/probe/target", "Caller"), TestContext.Current.CancellationToken);
        Assert.Equal("", forged!.Value.GetString());

        Assert.Equal("", await silo.Get<IProbe>($"{Brain}/probe/target").Caller());
        Assert.Equal($"{Brain}/probe/asker", await silo.Get<IProbe>($"{Brain}/probe/asker").AskCaller("target"));
        Assert.Equal("", await silo.Get<IProbe>($"{Brain}/probe/target").Caller());
    }

    private static IHost TwoSilo(int siloPort, IPEndPoint primary)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.UseOrleans(orleans =>
        {
            orleans.UseLocalhostClustering(siloPort, FreePort(), primary, "db-host-facts", "db-host-facts");
            orleans.AddPlacementDirector<BrainAffinePlacement, BrainAffinePlacementDirector>();
            orleans.AddNeuronHosting(typeof(ProbeNeuron).Assembly);
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
