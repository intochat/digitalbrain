using System.Collections.Concurrent;
using System.Reflection;
using DigitalBrain.Capabilities;

namespace DigitalBrain.Testing;

/// <summary>
/// The capability fakes a <see cref="TestOS"/> hands to neurons and apps, keyed by the <c>[Capability]</c> contract and a binding name.
/// Only explicitly bound fakes resolve: an unbound capability fails with <see cref="CapabilityNotBoundException"/>, never a silent default.
/// </summary>
public sealed class CapabilityFakes
{
    private readonly ConcurrentDictionary<(Type Contract, string Name), object> _bound = new();
    private readonly ConcurrentQueue<(Type Contract, string Name)> _order = new();

    /// <summary>The capability ids with at least one bound fake.</summary>
    public IReadOnlyList<CapabilityId> Bound => [.. _order.Select(key => IdOf(key.Contract)).Distinct()];

    /// <summary>The <c>name@version</c> of a <c>[Capability]</c> contract.</summary>
    /// <exception cref="ArgumentException"><paramref name="contract"/> is not an interface marked <c>[Capability]</c>.</exception>
    public static CapabilityId IdOf(Type contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var attribute = contract.IsInterface ? contract.GetCustomAttribute<CapabilityAttribute>(inherit: false) : null;
        return attribute is null
            ? throw new ArgumentException($"{contract.FullName} is not a capability contract: mark the interface [Capability(name, version)].", nameof(contract))
            : attribute.Id;
    }

    /// <summary>Binds <paramref name="fake"/> as the <typeparamref name="TCapability"/> named <paramref name="name"/>.</summary>
    /// <exception cref="ArgumentException">The contract is not a <c>[Capability]</c> interface, the name is empty, or the (contract, name) pair is already bound.</exception>
    public CapabilityFakes Bind<TCapability>(string name, TCapability fake) where TCapability : class
    {
        ArgumentNullException.ThrowIfNull(fake);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var id = IdOf(typeof(TCapability));
        if (!_bound.TryAdd((typeof(TCapability), name), fake))
        {
            throw new ArgumentException($"Capability {id} is already bound under the name '{name}'.", nameof(name));
        }
        _order.Enqueue((typeof(TCapability), name));
        return this;
    }

    /// <summary>True when a fake implementing <paramref name="capability"/> is bound.</summary>
    public bool IsBound(CapabilityId capability) => _order.Any(key => IdOf(key.Contract) == capability);

    /// <summary>The fake bound as the <typeparamref name="TCapability"/> named <paramref name="name"/>.</summary>
    /// <exception cref="CapabilityNotBoundException">Nothing is bound under that contract and name.</exception>
    public TCapability Resolve<TCapability>(string name) where TCapability : class
        => _bound.TryGetValue((typeof(TCapability), name), out var fake)
            ? (TCapability)fake
            : throw new CapabilityNotBoundException(IdOf(typeof(TCapability)), name);

    /// <summary>The only fake bound as <typeparamref name="TCapability"/>.</summary>
    /// <exception cref="CapabilityNotBoundException">Nothing is bound under that contract.</exception>
    /// <exception cref="InvalidOperationException">Several fakes are bound; resolve one by name.</exception>
    public TCapability Resolve<TCapability>() where TCapability : class => (TCapability)Resolve(typeof(TCapability));

    /// <summary>Every fake bound as <typeparamref name="TCapability"/>, in binding order.</summary>
    public IReadOnlyList<TCapability> All<TCapability>() where TCapability : class => (TCapability[])All(typeof(TCapability));

    /// <summary>The binding names of <typeparamref name="TCapability"/>, in binding order.</summary>
    public IReadOnlyList<string> Names<TCapability>() where TCapability : class
        => [.. _order.Where(key => key.Contract == typeof(TCapability)).Select(key => key.Name)];

    internal object Resolve(Type contract)
    {
        var names = _order.Where(key => key.Contract == contract).ToList();
        return names.Count switch
        {
            0 => throw new CapabilityNotBoundException(IdOf(contract), null),
            1 => _bound[names[0]],
            _ => throw new InvalidOperationException(
                $"Capability {IdOf(contract)} is bound {names.Count} times ({string.Join(", ", names.Select(key => key.Name))}); take IReadOnlyList<{contract.Name}> or resolve by name."),
        };
    }

    internal Array All(Type contract)
    {
        _ = IdOf(contract);
        var fakes = _order.Where(key => key.Contract == contract).Select(key => _bound[key]).ToArray();
        var typed = Array.CreateInstance(contract, fakes.Length);
        Array.Copy(fakes, typed, fakes.Length);
        return typed;
    }

    internal static bool IsContract(Type type) => type.IsInterface && type.IsDefined(typeof(CapabilityAttribute), inherit: false);

    // IEnumerable<T>, IReadOnlyList<T>, IReadOnlyCollection<T> or T[] of a capability contract: every bound fake.
    internal static Type? ListedContract(Type type)
    {
        if (type.IsArray) { return IsContract(type.GetElementType()!) ? type.GetElementType() : null; }
        if (!type.IsGenericType) { return null; }
        var definition = type.GetGenericTypeDefinition();
        var element = type.GetGenericArguments()[0];
        return (definition == typeof(IEnumerable<>) || definition == typeof(IReadOnlyList<>) || definition == typeof(IReadOnlyCollection<>)) && IsContract(element)
            ? element
            : null;
    }
}

/// <summary>A neuron or app asked for a capability no fake is bound for.</summary>
public sealed class CapabilityNotBoundException : InvalidOperationException
{
    /// <summary>The capability is not bound (under <paramref name="name"/> when given); <paramref name="requiredBy"/> names who needs it.</summary>
    public CapabilityNotBoundException(CapabilityId capability, string? name, string? requiredBy = null)
        : base(Describe(capability, name, requiredBy))
    {
        Capability = capability;
        Name = name;
    }

    /// <summary>The capability that is not bound.</summary>
    public CapabilityId Capability { get; }

    /// <summary>The binding name asked for, or null for any.</summary>
    public string? Name { get; }

    private static string Describe(CapabilityId capability, string? name, string? requiredBy)
        => (requiredBy is null ? string.Empty : $"{requiredBy} requires capability {capability}, but ")
            + (name is null ? $"capability {capability} is not bound" : $"capability {capability} is not bound under the name '{name}'")
            + " in this TestOS; bind a fake with TestOS.With<TCapability>(...).";
}

// Neurons and apps resolve from this: capability contracts from the fakes, ICapabilityBroker over the fakes, the rest from the options' services.
internal sealed class TestOSServices(CapabilityFakes fakes, IServiceProvider inner) : IServiceProvider, ICapabilityBroker
{
    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceType == typeof(ICapabilityBroker)) { return this; }
        if (serviceType == typeof(CapabilityFakes)) { return fakes; }
        if (CapabilityFakes.IsContract(serviceType)) { return fakes.Resolve(serviceType); }
        return CapabilityFakes.ListedContract(serviceType) is { } contract ? fakes.All(contract) : inner.GetService(serviceType);
    }

    public Task<TCapability> Resolve<TCapability>(BrainId brain, CancellationToken ct) where TCapability : class
        => Task.FromResult(fakes.Resolve<TCapability>());

    // One connection per bound fake: the binding name is the connection key and the label.
    public Task<IReadOnlyList<CapabilityConnection<TCapability>>> ResolveAll<TCapability>(BrainId brain, CancellationToken ct) where TCapability : class
        => Task.FromResult<IReadOnlyList<CapabilityConnection<TCapability>>>(
            [.. fakes.Names<TCapability>().Select(name => new CapabilityConnection<TCapability>(name, name, fakes.Resolve<TCapability>(name)))]);
}
