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

    /// <summary>Inspects the live state of the hosted neuron.</summary>
    [AlwaysInterleave, Alias("inspect-state")]
    Task<JsonElement?> InspectState(CancellationToken cancellationToken = default);
}
