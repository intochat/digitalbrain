using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Invocation;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>
/// The in-silo <see cref="IInvocationSink"/> behind <c>Get&lt;T&gt;(NeuronId)</c>: picks the host grain by the class's policy,
/// keys it <c>brain/type/key</c> and stamps the caller into RequestContext, where the host grain reads provenance.
/// </summary>
internal sealed class OrleansInvocationSink(IGrainFactory grains, NeuronTypeRegistry? types, NeuronId? caller, Func<NeuronId, RuntimeInvocation, Task>? beforeStream = null) : IInvocationSink
{
    // Async methods: the RequestContext change stays inside the call and never leaks back into the caller's flow.
    public async Task<JsonElement?> Invoke(NeuronId target, RuntimeInvocation call, CancellationToken cancellationToken)
    {
        var (host, neuronClass) = await Host(target, call, cancellationToken).ConfigureAwait(false);
        Stamp();
        var invocation = Stamped(call);
        var interleaved = neuronClass is null
            ? await host.IsInterleaved(invocation).WaitAsync(cancellationToken).ConfigureAwait(false)
            : NeutralCallMapping.MayInterleave(neuronClass, NeuronHostGrainBase.Bind(neuronClass, invocation), invocation);
        return await (interleaved ? host.InvokeInterleaved(invocation, cancellationToken) : host.Invoke(invocation, cancellationToken)).ConfigureAwait(false);
    }

    public async Task Post(NeuronId target, RuntimeInvocation call)
    {
        var (host, neuronClass) = await Host(target, call, CancellationToken.None).ConfigureAwait(false);
        Stamp();
        var invocation = Stamped(call);
        var interleaved = neuronClass is null
            ? await host.IsInterleaved(invocation).ConfigureAwait(false)
            : NeutralCallMapping.MayInterleave(neuronClass, NeuronHostGrainBase.Bind(neuronClass, invocation), invocation);
        await (interleaved ? host.PostInterleaved(invocation) : host.Post(invocation)).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<JsonElement> Stream(NeuronId target, RuntimeInvocation call, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (host, _) = await Host(target, call, cancellationToken).ConfigureAwait(false);
        Stamp();
        if (beforeStream is not null) { await beforeStream(target, call).ConfigureAwait(false); }
        await foreach (var item in host.Stream(Stamped(call), cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false)) { yield return item; }
    }

    /// <summary>The host grain for <paramref name="target"/>, and the neuron class when this process loaded it.</summary>
    /// <exception cref="BindingException">The type is not hosted, or the call names another neuron.</exception>
    internal async Task<(INeuronHost Host, Type? NeuronClass)> Host(NeuronId target, RuntimeInvocation call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        var key = target.ToString();
        if (!string.Equals(call.Key, key, StringComparison.Ordinal)) { throw new BindingException($"The call names {call.Key}, not {key}."); }
        Type? neuronClass = null;
        string? grainType;
        if (types is not null)
        {
            if (!types.TryGet(target.Type, out neuronClass))
            { throw new BindingException($"No neuron class is hosted for type '{target.Type}'."); }
            grainType = NeuronTypeRegistry.GrainTypeOf(NeuronTypeRegistry.PolicyOf(neuronClass));
        }
        else
        {
            grainType = await grains.GetGrain<INeuronHostCatalog>("/").HostType(target.Type).WaitAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new BindingException($"No neuron class is hosted for type '{target.Type}'.");
        }
        return (grains.GetGrain<INeuronHost>(GrainId.Create(grainType, key)), neuronClass);
    }

    private readonly string _behavior = caller?.ToString() ?? string.Empty;

    // The caller's invocation is reused when it already carries this sink's provenance: no copy on the common path.
    private RuntimeInvocation Stamped(RuntimeInvocation call)
        => string.Equals(call.Behavior, _behavior, StringComparison.Ordinal) ? call : call with { Behavior = _behavior };

    private void Stamp()
    {
        if (caller is not null) { RequestContext.Set(HostNeuronContext.CallerKey, _behavior); }
        else { RequestContext.Remove(HostNeuronContext.CallerKey); }
    }
}
