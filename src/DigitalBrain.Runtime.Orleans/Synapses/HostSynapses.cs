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

    /// <summary>Nudges the subscriber to deliver signals up to <paramref name="upTo"/>.</summary>
    [OrleansSdk.Concurrency.OneWay, Alias("nudge")]
    Task Nudge(long upTo);

    /// <summary>Catches up every source of this subscriber from its cursor.</summary>
    [OrleansSdk.Concurrency.OneWay, Alias("catch-up")]
    Task CatchUp();

    /// <summary>Ensures signals of <paramref name="signalAliases"/> from every other neuron of the brain reach this subscriber (its brain-wide synapse).</summary>
    /// <exception cref="UnauthorizedAccessException">The caller is not the installs neuron of this brain.</exception>
    [Alias("ensure-brain-wide")]
    Task EnsureBrainWide(string[] signalAliases);

    /// <summary>Removes the brain-wide synapse of this subscriber.</summary>
    /// <exception cref="UnauthorizedAccessException">The caller is not the installs neuron of this brain.</exception>
    [Alias("remove-brain-wide")]
    Task RemoveBrainWide();
}
