using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OrleansSdk = Orleans;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Probe;

// 04-14 (RT-01, 06 D-11..D-13): every call shape of one plain neuron through Get<T>(NeuronId) on a LocalProfile silo.
public sealed class ProbeCallShapeFacts(StatefulSilo silo) : IClassFixture<StatefulSilo>
{
    private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(700);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IProbe Probe(string id) => silo.Get<IProbe>(id);

    private async Task<string> HostType(string id, string method)
    {
        var neuron = NeuronId.Parse(id);
        var call = new RuntimeInvocation("", NeuronProxy.ContractName(typeof(IProbe)), id, method, []);
        var host = await new OrleansInvocationSink(silo.Services.GetRequiredService<OrleansSdk.IGrainFactory>(), silo.Services.GetRequiredService<NeuronTypeRegistry>(), null)
            .Host(neuron, call, CancellationToken.None);
        return host.Host.GetGrainId().Type.ToString();
    }

    /// <summary>Holds a turn on <paramref name="id"/>, runs <paramref name="other"/> and reports whether it finished while the turn was held.</summary>
    private async Task<bool> RunsDuringHold(string id, Func<IProbe, Task> other)
    {
        var probe = Probe(id);
        var gate = id + "#" + Guid.NewGuid().ToString("N");
        var hold = probe.Hold(gate);
        await ShapeLog.Until(() => ShapeLog.Seen.ContainsKey("held:" + gate), Ct);
        var call = other(probe);
        var during = await Task.WhenAny(call, Task.Delay(Window, Ct)) == call;
        ShapeLog.Gate(gate).SetResult();
        await hold;
        await call;
        return during;
    }

    [Fact]
    public async Task Task_returns_after_the_method_ran()
    {
        await Probe("b1/probe.shapes/task").Touch("task");
        Assert.Equal(1, ShapeLog.Seen["task"]);
    }

    [Fact]
    public async Task TaskOfT_returns_the_value() => Assert.Equal(5, await Probe("b1/probe.shapes/taskt").Add(2, 3));

    [Fact]
    public async Task ValueTask_returns_after_the_method_ran()
    {
        await Probe("b1/probe.shapes/vt").Nothing("vt");
        Assert.True(ShapeLog.Seen.ContainsKey("vt"));
    }

    [Fact]
    public async Task ValueTaskOfT_returns_the_value_for_empty_and_large_payloads()
    {
        var probe = Probe("b1/probe.shapes/vtt");
        Assert.Equal("", await probe.Echo(""));
        var large = new string('x', 32 * 1024);
        Assert.Equal(large, await probe.Echo(large));
    }

    [Fact]
    public async Task A_caller_cancelled_CancellationToken_reaches_the_method()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var waiting = Probe("b1/probe.shapes/cancel").WaitCancelled("cancel", cancel.Token);
        await Task.Delay(300, Ct);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await ShapeLog.Until(() => ShapeLog.Seen.ContainsKey("cancel"), Ct);
    }

    [Fact]
    public async Task IAsyncEnumerable_streams_every_item_in_order()
        => Assert.Equal(Enumerable.Range(0, 50), await Probe("b1/probe.shapes/stream").Count(50, Ct).ToListAsync(Ct));

    [Fact]
    public async Task OneWayCall_returns_before_the_method_runs()
    {
        var gate = "oneway#" + Guid.NewGuid().ToString("N");
        await Probe("b1/probe.shapes/oneway").Fire(gate).WaitAsync(TimeSpan.FromSeconds(5), Ct);
        Assert.False(ShapeLog.Seen.ContainsKey(gate));
        ShapeLog.Gate(gate).SetResult();
        await ShapeLog.Until(() => ShapeLog.Seen.ContainsKey(gate), Ct);
    }

    [Fact]
    public async Task CallTimeout_throws_TimeoutException() => await Assert.ThrowsAsync<TimeoutException>(() => Probe("b1/probe.shapes/slow").Slow());

    [Fact]
    public async Task A_plain_method_does_not_run_during_a_suspended_turn()
        => Assert.False(await RunsDuringHold("b1/probe.shapes/plain", probe => probe.Plain()));

    [Fact]
    public async Task Interleaves_runs_during_a_suspended_turn()
        => Assert.True(await RunsDuringHold("b1/probe.shapes/interleaves", probe => probe.Peek()));

    [Fact]
    public async Task ReadOnlyCall_runs_during_a_suspended_turn()
        => Assert.True(await RunsDuringHold("b1/probe.shapes/readonly", probe => probe.Look()));

    [Fact]
    public async Task InterleavesWhen_interleaves_exactly_when_its_predicate_says_so()
    {
        Assert.True(await RunsDuringHold("b1/probe.shapes/when", probe => probe.Flagged(true)));
        Assert.False(await RunsDuringHold("b1/probe.shapes/when", probe => probe.Flagged(false)));
    }

    [Fact]
    public async Task Reentrant_is_hosted_by_neuron_host_reentrant_and_interleaves_plain_methods()
    {
        Assert.Equal("neuron.host.reentrant", await HostType("b1/probe.shapes.reentrant/r", "Plain"));
        Assert.True(await RunsDuringHold("b1/probe.shapes.reentrant/r", probe => probe.Plain()));
    }

    [Fact]
    public async Task StatelessNeuron_is_hosted_by_neuron_host_stateless_and_answers()
    {
        Assert.Equal("neuron.host.stateless", await HostType("b1/probe.shapes.stateless/s", "Add"));
        var probe = Probe("b1/probe.shapes.stateless/s");
        Assert.Equal(7, await probe.Add(3, 4));
        Assert.NotEqual(0, await probe.Instance());
    }

    [Fact]
    public async Task StatelessNeuron_Save_is_refused()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Probe("b1/probe.shapes.stateless/save").Save(1, "no"));
        Assert.Contains("StatelessNeuron", error.Message);
    }

    [Fact]
    public async Task Exclusive_neuron_is_hosted_by_neuron_host_keyed_brain_type_key()
        => Assert.Equal("neuron.host", await HostType("b1/probe.shapes/keyed", "Add"));

    [Fact]
    public async Task Save_persists_state_and_signals_and_both_survive_deactivation_and_reactivation()
    {
        var probe = Probe("b1/probe.shapes/saved");
        Assert.Equal(3, await probe.Save(3, "first"));
        Assert.Equal(8, await probe.Save(8, "second"));

        await silo.Restart(Ct);

        Assert.Equal(8, await Probe("b1/probe.shapes/saved").Value());
        var page = await silo.History("b1").Read(NeuronId.Parse("b1/probe.shapes/saved"), 0, 10, Ct);
        Assert.Equal([new ProbeSaved(3, "first"), new ProbeSaved(8, "second")], page.Signals.Select(signal => (ProbeSaved)signal.Signal));
    }

    [Fact]
    public async Task Caller_provenance_stamped_by_the_sink_reaches_Context_Caller()
    {
        var a = Probe("b1/probe.shapes/caller-a");
        Assert.Equal("", await a.Caller());
        Assert.Equal("b1/probe.shapes/caller-a", await a.AskCaller("caller-b"));
    }
}
