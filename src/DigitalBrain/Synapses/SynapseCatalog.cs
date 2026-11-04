using System.Reflection;

namespace DigitalBrain;

/// <summary>The state records and synapse functions found in a set of assemblies.</summary>
public sealed class SynapseCatalog
{
    private const BindingFlags AllMethods = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    private SynapseCatalog(IReadOnlyDictionary<string, Type> states, IReadOnlyList<SynapseDescriptor> synapses)
    {
        States = states;
        Synapses = synapses;
    }

    /// <summary>State alias to state record type.</summary>
    public IReadOnlyDictionary<string, Type> States { get; }

    /// <summary>Every synapse, in registration order.</summary>
    public IReadOnlyList<SynapseDescriptor> Synapses { get; }

    /// <summary>Collects every <c>[State]</c> record and <c>[Synapse]</c> function of <paramref name="assemblies"/>.</summary>
    /// <param name="assemblies">The assemblies to read.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="SynapseShapeException">A duplicate alias, a function of unsupported shape, or a state a synapse uses that cannot be created empty.</exception>
    public static SynapseCatalog Scan(params Assembly[] assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        return Build(assemblies.SelectMany(a => a.GetTypes()), strictStates: false);
    }

    /// <summary>Reads an explicit list of types; every <c>[State]</c> among them must be creatable empty.</summary>
    /// <param name="candidates">The types to read.</param>
    /// <returns>The catalog.</returns>
    internal static SynapseCatalog Scan(IEnumerable<Type> candidates) => Build(candidates, strictStates: true);

    /// <summary>The synapses addressing the state <paramref name="stateAlias"/>, in registration order.</summary>
    /// <param name="stateAlias">The state alias.</param>
    /// <returns>The synapses.</returns>
    public IReadOnlyList<SynapseDescriptor> For(string stateAlias) =>
        [.. Synapses.Where(d => string.Equals(d.StateAlias, stateAlias, StringComparison.Ordinal))];

    /// <summary>The gates a signal of type <paramref name="signal"/> reaches: most derived parameter type first, then registration order.</summary>
    /// <param name="signal">The arriving signal type.</param>
    /// <returns>The gates.</returns>
    public IReadOnlyList<SynapseDescriptor> GatesOf(Type signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return [.. Synapses
            .Where(d => d.Kind == SynapseKind.Gate && d.Signal!.IsAssignableFrom(signal))
            .OrderByDescending(d => Depth(d.Signal!))];
    }

    /// <summary>A new catalog holding both sets.</summary>
    /// <param name="other">The catalog to add.</param>
    /// <returns>The merged catalog.</returns>
    /// <exception cref="SynapseShapeException">A state alias is in both catalogs.</exception>
    public SynapseCatalog Merge(SynapseCatalog other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var states = new Dictionary<string, Type>(States, StringComparer.Ordinal);
        foreach (var (alias, type) in other.States)
        {
            if (!states.TryAdd(alias, type))
            {
                throw Duplicate(alias, states[alias], type);
            }
        }

        return new SynapseCatalog(states, [.. Synapses, .. other.Synapses]);
    }

    private static SynapseCatalog Build(IEnumerable<Type> types, bool strictStates)
    {
        var all = types.ToList();
        var states = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var type in all)
        {
            if (type.GetCustomAttribute<StateAttribute>() is { } state && !states.TryAdd(state.Alias, type))
            {
                throw Duplicate(state.Alias, states[state.Alias], type);
            }
        }

        var methods = all
            .SelectMany(t => t.GetMethods(AllMethods).Where(m => m.IsDefined(typeof(SynapseAttribute), false)).OrderBy(m => m.MetadataToken))
            .ToList();

        var used = methods.Select(m => m.GetCustomAttribute<SynapseAttribute>()!.State).ToHashSet(StringComparer.Ordinal);
        foreach (var (alias, type) in states)
        {
            if ((strictStates || used.Contains(alias)) && !CreatableEmpty(type))
            {
                throw new SynapseShapeException($"{type.FullName}: state record '{alias}' needs a public parameterless constructor (optional constructor parameters do not count).");
            }
        }

        return new SynapseCatalog(states, [.. methods.Select(m => SynapseShape.Of(m, states))]);
    }

    private static bool CreatableEmpty(Type type) =>
        type.GetConstructor(Type.EmptyTypes) is not null;

    private static int Depth(Type type)
    {
        var depth = 0;
        for (var t = type.BaseType; t is not null; t = t.BaseType)
        {
            depth++;
        }

        return depth;
    }

    private static SynapseShapeException Duplicate(string alias, Type first, Type second) =>
        new($"State alias '{alias}' is declared by both {first.FullName} and {second.FullName}.");
}
