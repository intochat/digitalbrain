using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace DigitalBrain.Runtime.Orleans.Hosting;

/// <summary>Which host grain runs a neuron class.</summary>
internal enum HostPolicy
{
    /// <summary><c>neuron.host</c>: one turn at a time, brain-affine, neutral attributes decide interleaving.</summary>
    Affine,

    /// <summary><c>neuron.host.reentrant</c>: a <see cref="ReentrantAttribute"/> class; every call interleaves.</summary>
    Reentrant,

    /// <summary><c>neuron.host.stateless</c>: a <see cref="StatelessNeuronAttribute"/> class; many activations.</summary>
    Stateless,
}

/// <summary>One hosted plain neuron class as the registry sees it.</summary>
/// <param name="Type">The frozen <see cref="NeuronAttribute"/> type name.</param>
/// <param name="Class">The class.</param>
/// <param name="StateName">The persisted state name: <see cref="NeuronAttribute.State"/> or <c>state</c>. Storage keys never move.</param>
/// <param name="Policy">The host grain policy.</param>
/// <param name="Contracts">The contracts the class implements, framework interfaces excluded.</param>
internal sealed record NeuronRegistration(string Type, Type Class, string StateName, HostPolicy Policy, IReadOnlyList<Type> Contracts);

/// <summary>
/// The module-wide registry of hosted plain neuron classes (06 D-01, D-13): frozen <see cref="NeuronAttribute"/> type name to
/// class, state name, policy and contracts, built from the composed module assemblies. Duplicate type names and
/// <see cref="App"/> classes fail startup.
/// </summary>
internal sealed class NeuronTypeRegistry
{
    internal const string AffineGrainType = "neuron.host";
    internal const string ReentrantGrainType = "neuron.host.reentrant";
    internal const string StatelessGrainType = "neuron.host.stateless";

    /// <summary>The state name when the class sets none.</summary>
    internal const string DefaultStateName = "state";

    private readonly Dictionary<string, NeuronRegistration> _types = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, NeuronRegistration> _byContract = [];

    /// <exception cref="InvalidOperationException">Two classes claim one type name, or a <c>[Neuron]</c> class is an app.</exception>
    public NeuronTypeRegistry(IEnumerable<Assembly> assemblies)
        : this((assemblies ?? throw new ArgumentNullException(nameof(assemblies))).Distinct().SelectMany(Loadable))
    {
    }

    /// <exception cref="InvalidOperationException">Two classes claim one type name, or a <c>[Neuron]</c> class is an app.</exception>
    internal NeuronTypeRegistry(IEnumerable<Type> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        foreach (var type in candidates.Distinct())
        {
            if (!type.IsClass || type.GetCustomAttribute<NeuronAttribute>(inherit: false) is not { } neuron) { continue; }
            if (typeof(App).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"{type.FullName} is marked [Neuron(\"{neuron.Type}\")] but derives App: script apps run in the sandbox, not in the silo.");
            }
            if (type.IsAbstract || type.IsGenericTypeDefinition || !typeof(Neuron).IsAssignableFrom(type)) { continue; }
            if (_types.TryGetValue(neuron.Type, out var other) && other.Class != type)
            {
                throw new InvalidOperationException($"Neuron type '{neuron.Type}' is claimed by {other.Class.FullName} and {type.FullName}.");
            }
            _ = NeutralCallMapping.PredicateOf(type); // a misnamed predicate fails at registration, not on a call
            var registration = new NeuronRegistration(neuron.Type, type, neuron.State ?? DefaultStateName, PolicyOf(type), ContractsOf(type));
            _types[neuron.Type] = registration;
            foreach (var contract in registration.Contracts) { _byContract.TryAdd(contract, registration); }
        }
    }

    /// <summary>The hosted type names.</summary>
    public IReadOnlyCollection<string> Names => _types.Keys;

    /// <summary>The registrations.</summary>
    public IReadOnlyCollection<NeuronRegistration> Registrations => _types.Values;

    /// <summary>The class registered for <paramref name="type"/>.</summary>
    public bool TryGet(string type, [NotNullWhen(true)] out Type? neuronClass)
    {
        neuronClass = _types.TryGetValue(type, out var registration) ? registration.Class : null;
        return neuronClass is not null;
    }

    /// <summary>The registration for <paramref name="type"/>.</summary>
    public bool TryGetRegistration(string type, [NotNullWhen(true)] out NeuronRegistration? registration) => _types.TryGetValue(type, out registration);

    /// <summary>The first registered class implementing <paramref name="contract"/>.</summary>
    public bool TryGetByContract(Type contract, [NotNullWhen(true)] out NeuronRegistration? registration) => _byContract.TryGetValue(contract, out registration);

    /// <summary>The registration for <paramref name="type"/>; the error names the type.</summary>
    /// <exception cref="Invocation.BindingException">No class is hosted for the type.</exception>
    public NeuronRegistration Require(string type)
        => _types.TryGetValue(type, out var registration) ? registration
            : throw new Invocation.BindingException($"No neuron class is hosted for type '{type}'.");

    /// <summary>The host policy a class declares.</summary>
    public static HostPolicy PolicyOf(Type neuronClass)
    {
        ArgumentNullException.ThrowIfNull(neuronClass);
        if (neuronClass.IsDefined(typeof(StatelessNeuronAttribute), inherit: false)) { return HostPolicy.Stateless; }
        return neuronClass.IsDefined(typeof(ReentrantAttribute), inherit: true) ? HostPolicy.Reentrant : HostPolicy.Affine;
    }

    /// <summary>The host grain type for a policy.</summary>
    public static string GrainTypeOf(HostPolicy policy) => policy switch
    {
        HostPolicy.Reentrant => ReentrantGrainType,
        HostPolicy.Stateless => StatelessGrainType,
        _ => AffineGrainType,
    };

    private static Type[] ContractsOf(Type neuronClass)
        => [.. neuronClass.GetInterfaces().Where(face => face.Assembly != typeof(Neuron).Assembly && face.Assembly != typeof(object).Assembly)];

    private static IEnumerable<Type> Loadable(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error) { return error.Types.OfType<Type>(); }
    }
}
