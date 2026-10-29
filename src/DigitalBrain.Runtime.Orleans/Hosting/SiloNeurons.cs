using System.Reflection;
using DigitalBrain.Contracts;
using DigitalBrain.Invocation;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>
/// Reaches a hosted plain neuron from outside any neuron (a host edge or a test harness on an in-process silo): the call goes
/// to the neuron's host grain by the runtime's hosting policy and carries no neuron caller.
/// </summary>
public static class SiloNeurons
{
    private static readonly MethodInfo TypedGet = typeof(SiloNeurons).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method.IsGenericMethodDefinition && method.GetParameters() is [_, { ParameterType: var key }] && key == typeof(NeuronId));

    /// <summary>A typed proxy to the neuron <paramref name="id"/> hosted in the silo of <paramref name="services"/>.</summary>
    /// <exception cref="InvalidOperationException">The services are not a silo's that hosts plain neurons.</exception>
    public static T Get<T>(IServiceProvider services, NeuronId id) where T : class
    {
        ArgumentNullException.ThrowIfNull(services);
        return NeuronHosting.Get<T>(services, id);
    }

    /// <summary>A typed proxy of <typeparamref name="T"/> for the os key <c>{brain}/{key}</c>. <typeparamref name="T"/> names its neuron type with <see cref="DefaultNeuronTypeAttribute"/>.</summary>
    /// <exception cref="ArgumentException"><typeparamref name="T"/> names no neuron type, or <paramref name="fullKey"/> names no brain.</exception>
    public static T Get<T>(IServiceProvider services, string fullKey) where T : class
        => Get<T>(services, IdOf(typeof(T), fullKey));

    /// <summary><see cref="Get{T}(IServiceProvider, NeuronId)"/> for a contract known only at runtime.</summary>
    public static object Get(IServiceProvider services, Type contract, NeuronId id)
    {
        ArgumentNullException.ThrowIfNull(contract);
        return TypedGet.MakeGenericMethod(contract).Invoke(null, [services, id])!;
    }

    /// <summary><see cref="Get{T}(IServiceProvider, string)"/> for a contract known only at runtime.</summary>
    public static object Get(IServiceProvider services, Type contract, string fullKey)
        => Get(services, contract, IdOf(contract, fullKey));

    /// <summary>The runtime neuron id the os full key names for <paramref name="contract"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="contract"/> names no neuron type, or <paramref name="fullKey"/> names no brain.</exception>
    public static NeuronId IdOf(Type contract, string fullKey)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullKey);
        var type = contract.GetCustomAttribute<DefaultNeuronTypeAttribute>(inherit: false)?.Type
            ?? throw new ArgumentException($"{contract.Name} is not a plain neuron contract.", nameof(contract));
        var separator = fullKey.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0 || !BrainId.IsValid(fullKey[..separator]))
        { throw new ArgumentException($"'{fullKey}' names no brain: a plain neuron key is {{brain}}/{{key}}.", nameof(fullKey)); }
        var brain = fullKey[..separator];
        return new NeuronId(new BrainId(brain), type, fullKey[(brain.Length + 1)..]);
    }

    /// <summary>
    /// Asks <paramref name="authorize"/> before a stream starts on a proxy from <see cref="Get{T}(IServiceProvider, NeuronId)"/>.
    /// A host grain's stream passes no incoming call filter, so an edge policy registers here.
    /// </summary>
    public static void AuthorizeStreams(IServiceCollection services, Func<IServiceProvider, NeuronId, RuntimeInvocation, Task> authorize)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(authorize);
        services.AddSingleton(new NeuronStreamAuthorizer(authorize));
    }
}

internal sealed class NeuronStreamAuthorizer(Func<IServiceProvider, NeuronId, RuntimeInvocation, Task> authorize)
{
    public Task Authorize(IServiceProvider services, NeuronId id, RuntimeInvocation call) => authorize(services, id, call);
}
