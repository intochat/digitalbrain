using DigitalBrain.Modules;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DigitalBrain.Access;
using DigitalBrain.Invocation;
using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Orleans.Access;
using Orleans;
using Orleans.Runtime;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>
/// The in-silo <see cref="IInvocationSink"/> behind <c>Get&lt;T&gt;(NeuronId)</c>: picks the host grain by the class's policy,
/// keys it <c>brain/type/key</c> and stamps the caller into RequestContext, where the host grain reads provenance.
/// </summary>
internal sealed class OrleansInvocationSink(
    IGrainFactory grains,
    NeuronTypeRegistry? types,
    NeuronId? caller,
    ICallerCarrier? callers = null,
    bool asKernel = false,
    Func<NeuronId, RuntimeInvocation, Task>? beforeStream = null) : IInvocationSink
{
    private readonly ICallerCarrier _callers = callers ?? OrleansCallerAmbient.Instance;

    // Async methods: the RequestContext change stays inside the call and never leaks back into the caller's flow.
    public async Task<JsonElement?> Invoke(NeuronId target, RuntimeInvocation call, CancellationToken cancellationToken)
    {
        using var _ = InProcessCallerScope.Enter();
        var (host, neuronClass) = await Host(target, call, cancellationToken).ConfigureAwait(false);
        Hop(target);
        var invocation = Stamped(call);
        var interleaved = neuronClass is null
            ? await host.IsInterleaved(invocation).WaitAsync(cancellationToken).ConfigureAwait(false)
            : CallPolicy.MayInterleave(neuronClass, NeuronHostGrainBase.Bind(neuronClass, invocation), invocation);
        return await (interleaved ? host.InvokeInterleaved(invocation, cancellationToken) : host.Invoke(invocation, cancellationToken)).ConfigureAwait(false);
    }

    public async Task Post(NeuronId target, RuntimeInvocation call)
    {
        using var _ = InProcessCallerScope.Enter();
        var (host, neuronClass) = await Host(target, call, CancellationToken.None).ConfigureAwait(false);
        Hop(target);
        var invocation = Stamped(call);
        var interleaved = neuronClass is null
            ? await host.IsInterleaved(invocation).ConfigureAwait(false)
            : CallPolicy.MayInterleave(neuronClass, NeuronHostGrainBase.Bind(neuronClass, invocation), invocation);
        await (interleaved ? host.PostInterleaved(invocation) : host.Post(invocation)).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<JsonElement> Stream(NeuronId target, RuntimeInvocation call, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var _ = InProcessCallerScope.Enter();
        var (host, _) = await Host(target, call, cancellationToken).ConfigureAwait(false);
        Hop(target);
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
            if (types.TryGet(target.Type, out neuronClass))
            {
                grainType = NeuronTypeRegistry.GrainTypeOf(NeuronTypeRegistry.PolicyOf(neuronClass));
            }
            else
            {
                grainType = "neuron.host";
            }
        }
        else
        {
            grainType = await grains.GetGrain<INeuronHostCatalog>("/").HostType(target.Type).WaitAsync(cancellationToken).ConfigureAwait(false)
                ?? "neuron.host";
        }
        return (grains.GetGrain<INeuronHost>(GrainId.Create(grainType, key)), neuronClass);
    }

    private readonly string _behavior = caller?.ToString() ?? string.Empty;

    // The caller's invocation is reused when it already carries this sink's provenance: no copy on the common path.
    private RuntimeInvocation Stamped(RuntimeInvocation call)
        => string.Equals(call.Behavior, _behavior, StringComparison.Ordinal) ? call : call with { Behavior = _behavior };

    private void Hop(NeuronId target)
    {
        var current = asKernel ? Caller.OfKernel(target.Brain) : _callers.Current;
        var inside = caller is not null || (current?.Inside ?? false) || (current?.Actor is Actor.Kernel or Actor.Scheduler or Actor.Module);
        _ = _callers.Enter(current?.Hop(via: caller, inside: inside));
    }
}
