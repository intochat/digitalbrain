using System.Text.Json;
using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

public sealed class InterleavedAccessFacts(HostedSilo silo) : IClassFixture<HostedSilo>
{
    [Fact]
    public async Task A_policy_query_is_pure_and_forcing_an_ordinary_mutation_to_interleave_has_no_effect()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = "b1/probe/forced-turn-" + Guid.NewGuid().ToString("N");
        var host = Host(key);
        var call = Calls.Of("forged/admin", key, nameof(IProbe.Touch));

        Assert.False(await host.IsInterleaved(call));
        Assert.False(ProbeLog.Touched.ContainsKey(key));
        await Assert.ThrowsAsync<BindingException>(() => host.InvokeInterleaved(call, ct));
        Assert.False(ProbeLog.Touched.ContainsKey(key));

        await host.Invoke(call, ct);
        Assert.Equal(1, ProbeLog.Touched[key]);
    }

    [Fact]
    public async Task Neither_the_policy_query_nor_the_interleaved_endpoint_can_retarget_the_host()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = "b1/turn/bound-" + Guid.NewGuid().ToString("N");
        var host = Host(key);
        var forged = TurnCall("b2/turn/other", nameof(ITurnProbe.Peek));

        await Assert.ThrowsAsync<BindingException>(() => host.IsInterleaved(forged));
        await Assert.ThrowsAsync<BindingException>(() => host.InvokeInterleaved(forged, ct));
        Assert.Equal(1, (await host.InvokeInterleaved(TurnCall(key, nameof(ITurnProbe.Peek)), ct))!.Value.GetInt32());
    }

    [Theory]
    [InlineData("turn", nameof(ITurnProbe.Peek), true, 1)]
    [InlineData("turn", nameof(ITurnProbe.Read), true, 2)]
    [InlineData("turn", nameof(ITurnProbe.Flagged), true, 4)]
    [InlineData("turn", nameof(ITurnProbe.Flagged), false, 0)]
    [InlineData("turn", nameof(ITurnProbe.Plain), false, 0)]
    [InlineData("turn.own", nameof(ITurnProbe.Plain), true, 3)]
    [InlineData("turn.own", nameof(ITurnProbe.Flagged), true, 0)]
    public async Task The_endpoint_rechecks_the_actual_class_and_exact_call_policy(string type, string method, bool flag, int expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var key = "b1/" + type + "/policy-" + Guid.NewGuid().ToString("N");
        var host = Host(key);
        var call = TurnCall(key, method, method == nameof(ITurnProbe.Flagged) ? [JsonSerializer.SerializeToElement(flag)] : []);

        Assert.Equal(expected != 0, await host.IsInterleaved(call));
        if (expected == 0) { await Assert.ThrowsAsync<BindingException>(() => host.InvokeInterleaved(call, ct)); }
        else { Assert.Equal(expected, (await host.InvokeInterleaved(call, ct))!.Value.GetInt32()); }
    }

    [Fact]
    public async Task A_one_way_call_cannot_force_an_ordinary_effect_to_interleave()
    {
        var ct = TestContext.Current.CancellationToken;
        var key = "b1/turn/forced-post-" + Guid.NewGuid().ToString("N");
        var observed = new ObservedPost(key);
        await using var runtime = await new LocalSiloHostFactory(new HostedSilo.Hosted(), services =>
            services.AddSingleton<IIncomingGrainCallFilter>(observed)).Start(new InProcessHostOptions(), ct);
        var host = runtime.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create("neuron.host", key));
        var deniedTag = key + "/denied";
        await host.PostInterleaved(TurnCall(key, nameof(ITurnProbe.Note), [JsonSerializer.SerializeToElement(deniedTag)]));

        Assert.IsType<BindingException>(await observed.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.False(TurnLog.Noted.ContainsKey(deniedTag));

        observed.Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowedTag = key + "/allowed";
        await host.PostInterleaved(TurnCall(key, nameof(ITurnProbe.NoteNow), [JsonSerializer.SerializeToElement(allowedTag)]));
        Assert.Null(await observed.Completion.Task.WaitAsync(TimeSpan.FromSeconds(5), ct));
        Assert.True(TurnLog.Noted.ContainsKey(allowedTag));
    }

    private INeuronHost Host(string key) => silo.Grains.GetGrain<INeuronHost>(GrainId.Create("neuron.host", key));

    private static RuntimeInvocation TurnCall(string key, string method, JsonElement[]? arguments = null)
        => new("", NeuronProxy.ContractName(typeof(ITurnProbe)), key, method, arguments ?? []);

    private sealed class ObservedPost(string key) : IIncomingGrainCallFilter
    {
        public TaskCompletionSource<Exception?> Completion { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task Invoke(IIncomingGrainCallContext context)
        {
            if (context.InterfaceMethod.Name != nameof(INeuronHost.PostInterleaved)
                || context.Request.GetArgument(0) is not RuntimeInvocation call || call.Key != key)
            { await context.Invoke(); return; }
            Exception? failure = null;
            try { await context.Invoke(); }
            catch (Exception error) { failure = error; throw; }
            finally { Completion.TrySetResult(failure); }
        }
    }
}
