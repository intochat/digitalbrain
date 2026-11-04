using System.Text.Json;
using DigitalBrain.Invocation;
using Orleans;
using Orleans.Concurrency;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>
/// The Orleans endpoint of a plain neuron, keyed <c>brain/type/key</c>. Three precompiled host grains implement it:
/// <c>neuron.host</c> (brain-affine), <c>neuron.host.reentrant</c> and <c>neuron.host.stateless</c>.
/// </summary>
[Alias("neuron.host")]
public interface INeuronHost : IGrainWithStringKey
{
    /// <summary>Whether the bound call permits interleaving on the serving host's neuron class. Runs no neuron method.</summary>
    [AlwaysInterleave, Alias("is-interleaved")]
    Task<bool> IsInterleaved(RuntimeInvocation call);

    /// <summary>Runs the call and returns its JSON result, or null when the method returns nothing.</summary>
    [Alias("invoke")]
    Task<JsonElement?> Invoke(RuntimeInvocation call, CancellationToken cancellationToken = default);

    /// <summary>Runs a call whose bound method permits interleaving; refuses an ordinary method before execution.</summary>
    [AlwaysInterleave, Alias("invoke-interleaved")]
    Task<JsonElement?> InvokeInterleaved(RuntimeInvocation call, CancellationToken cancellationToken = default);

    /// <summary>Runs a one-way call; the caller does not wait for it.</summary>
    [OneWay, Alias("post")]
    Task Post(RuntimeInvocation call);

    /// <summary>Runs a one-way call whose bound method permits interleaving; refuses ordinary methods.</summary>
    [AlwaysInterleave, OneWay, Alias("post-interleaved")]
    Task PostInterleaved(RuntimeInvocation call);

    /// <summary>Runs a streaming call and yields its items as JSON.</summary>
    [Alias("stream")]
    IAsyncEnumerable<JsonElement> Stream(RuntimeInvocation call, CancellationToken cancellationToken = default);

    /// <summary>Runs the gate synapses of the hosted state neuron for a signal, as a turn; saves nothing on allow.</summary>
    /// <exception cref="InvalidOperationException">The hosted neuron is not a state neuron.</exception>
    [Alias("gate")]
    Task<Synapses.GateVerdict> Gate(string signalAlias, JsonElement signal, CancellationToken cancellationToken = default);

    /// <summary>After the publisher's append, saves <paramref name="next"/> as the hosted state neuron's state with the signal, as a turn.</summary>
    /// <exception cref="InvalidOperationException">The hosted neuron is not a state neuron.</exception>
    [Alias("committed")]
    Task Committed(string signalAlias, JsonElement signal, JsonElement next, CancellationToken cancellationToken = default);

    /// <summary>The publish the signal was gated for did not happen: the hosted state neuron drops its provisional state, as a turn.</summary>
    /// <exception cref="InvalidOperationException">The hosted neuron is not a state neuron.</exception>
    [Alias("aborted")]
    Task Aborted(string signalAlias, JsonElement signal, CancellationToken cancellationToken = default);

    /// <summary>Inspects the live state of the hosted neuron.</summary>
    [AlwaysInterleave, Alias("inspect-state")]
    Task<JsonElement?> InspectState(CancellationToken cancellationToken = default);
}
