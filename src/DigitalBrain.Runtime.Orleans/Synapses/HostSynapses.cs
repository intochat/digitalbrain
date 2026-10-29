using DigitalBrain.Runtime.Orleans.Hosting;
using DigitalBrain.Spi;
using Orleans;

using OrleansSdk = Orleans;
namespace DigitalBrain.Runtime.Orleans.Synapses;

/// <summary>
/// The runtime-internal grain surface of a host grain for synapses: subscribe-time admission on the source and a reconcile nudge
/// on the subscriber. Every host grain implements it beside <see cref="INeuronHost"/>.
/// </summary>
[Alias("neuron.synapse-host")]
internal interface ISynapseHost : IGrainWithStringKey
{
    /// <summary>Runs the source's <c>AdmitSynapse</c> for <paramref name="subscriber"/>; the caller identity must be the subscriber.</summary>
    /// <exception cref="UnauthorizedAccessException">The source refuses, or the caller is not the subscriber.</exception>
    [OrleansSdk.Concurrency.AlwaysInterleave, Alias("admit")]
    Task Admit(NeuronId subscriber, string[] signalAliases);

    /// <summary>Catches up every source of this subscriber from its cursor.</summary>
    [OrleansSdk.Concurrency.OneWay, Alias("catch-up")]
    Task CatchUp();
}

/// <summary><see cref="ISynapses"/> of a hosted neuron, over its activation's <see cref="SynapseEngine"/>.</summary>
internal sealed class HostSynapses(SynapseEngine engine) : ISynapses
{
    public Task Ensure<TSignal>(NeuronId from) where TSignal : Signal => engine.Ensure(from, [typeof(TSignal)]);

    public Task Ensure(NeuronId from, params Type[] signals) => engine.Ensure(from, signals);

    public Task Remove(NeuronId from) => engine.Remove(from);
}
