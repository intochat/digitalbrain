using DigitalBrain.Invocation;
using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Runtime.Orleans.Synapses;
using DigitalBrain.Runtime.Orleans.Testing;
using Microsoft.Extensions.DependencyInjection;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Tests.Integration.Hosting;

public sealed class RoutingLifecycleFacts
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task An_admitted_ordinary_call_finishes_when_deactivation_starts(bool external, bool queuedCatchUp)
    {
        var ct = TestContext.Current.CancellationToken;
        var entered = new TaskCompletionSource<IGrainContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = await new LocalSiloHostFactory(new HostedSilo.Hosted(), services =>
        {
            services.Configure<OrleansSdk.Configuration.SiloMessagingOptions>(options => options.ResponseTimeout = TimeSpan.FromSeconds(3));
            services.AddSingleton<IIncomingGrainCallFilter>(new Admitted(entered));
        }).Start(new InProcessHostOptions(), ct);
        var id = NeuronId.Parse("b1/probe.lifecycle/" + Guid.NewGuid().ToString("N"));
        var probe = NeuronHosting.Get<ILifecycleProbe>(host.Services, id);
        var gate = id + "#held";
        var held = probe.Hold(gate, retire: !external);
        Task<int> queued = Task.FromResult(0);
        try
        {
            await ProbeLog.Until(() => TurnLog.Held.ContainsKey(gate), ct);
            if (queuedCatchUp)
            {
                await host.Services.GetRequiredService<IGrainFactory>()
                    .GetGrain<ISynapseHost>(GrainId.Create("neuron.host", id.ToString())).CatchUp();
            }
            queued = probe.Touch();
            var dispatch = host.Services.GetRequiredService<IGrainFactory>().GetGrain<INeuronHost>(GrainId.Create("neuron.host", id.ToString()));
            Assert.False(await dispatch.IsInterleaved(new RuntimeInvocation("", NeuronProxy.ContractName(typeof(ILifecycleProbe)), id.ToString(), nameof(ILifecycleProbe.Touch), [])));
            var context = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            if (external) { context.Deactivate(new DeactivationReason(DeactivationReasonCode.ShuttingDown, "Lifecycle regression"), ct); }
            ProbeLog.Gate(gate).TrySetResult();
            await held.WaitAsync(TimeSpan.FromSeconds(5), ct);
            Assert.Equal(1, await queued.WaitAsync(TimeSpan.FromSeconds(5), ct));
        }
        finally
        {
            ProbeLog.Gate(gate).TrySetResult();
            try { await Task.WhenAll(held, queued).WaitAsync(TimeSpan.FromSeconds(5), ct); }
            finally { await host.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct); }
        }
    }

    private sealed class Admitted(TaskCompletionSource<IGrainContext> entered) : IIncomingGrainCallFilter
    {
        public Task Invoke(IIncomingGrainCallContext context)
        {
            var pending = context.Invoke();
            if (context.InterfaceMethod.DeclaringType == typeof(INeuronHost)
                && context.InterfaceMethod.Name == nameof(INeuronHost.IsInterleaved))
            { entered.TrySetResult(context.TargetContext); }
            return pending;
        }
    }
}

public interface ILifecycleProbe
{
    Task Hold(string gate, bool retire);
    Task<int> Touch();
}

[Neuron("probe.lifecycle")]
public sealed class LifecycleProbe : Neuron, ILifecycleProbe
{
    private int _touches;

    public async Task Hold(string gate, bool retire)
    {
        TurnLog.Held[gate] = true;
        await ProbeLog.Gate(gate).Task;
        if (retire) { ((HostNeuronContext)Context).DeactivateOnIdle(); }
    }

    public Task<int> Touch() => Task.FromResult(++_touches);
}
