using System.ComponentModel;
using System.Reflection;
using System.Text;

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

    /// <summary>What the app with state alias <paramref name="alias"/> asks for at install time.</summary>
    /// <param name="alias">The state alias.</param>
    /// <returns>The needs, derived from every synapse of the alias.</returns>
    /// <exception cref="SynapseShapeException">The alias is not a state of this catalog.</exception>
    public SynapseNeeds NeedsOf(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (!States.ContainsKey(alias))
        {
            throw new SynapseShapeException($"No state '{alias}' in this catalog.");
        }

        var own = For(alias);
        var slots = own
            .SelectMany(d => d.Slots)
            .DistinctBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new SlotNeed(s.Name, s.Capability, DisplayName(s.Contract)))
            .ToList();
        var schedules = own.Where(d => d.Every is not null).Select(d => d.Every!).DistinctBy(s => s.Text, StringComparer.Ordinal).ToList();
        var gates = own
            .Where(d => d.Kind == SynapseKind.Gate)
            .Select(d => d.Signal!.GetCustomAttribute<SignalAttribute>()?.Alias ?? d.Signal!.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var reactions = own.Where(d => d.Kind == SynapseKind.Reaction).Select(d => d.Function.Name).ToList();
        return new SynapseNeeds(alias, slots, schedules, gates, reactions, own.Any(d => d.Kind == SynapseKind.View));
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

    /// <summary>A new catalog without the state <paramref name="alias"/> and every synapse addressing it.</summary>
    /// <param name="alias">The state alias to drop; an alias not in the catalog drops nothing.</param>
    /// <returns>The catalog without the alias.</returns>
    public SynapseCatalog Without(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        var states = new Dictionary<string, Type>(States, StringComparer.Ordinal);
        states.Remove(alias);
        return new SynapseCatalog(states, [.. Synapses.Where(d => !string.Equals(d.StateAlias, alias, StringComparison.Ordinal))]);
    }

    /// <summary>A new catalog where every state of <paramref name="other"/> replaces the state of the same alias: <see cref="Without"/> each, then <see cref="Merge"/>.</summary>
    /// <param name="other">The catalog whose states and synapses replace those of the same aliases.</param>
    /// <returns>The catalog.</returns>
    public SynapseCatalog Replace(SynapseCatalog other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var kept = this;
        foreach (var alias in other.States.Keys)
        {
            kept = kept.Without(alias);
        }

        return kept.Merge(other);
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

        var synapses = methods.Select(m => SynapseShape.Of(m, states)).ToList();
        foreach (var group in synapses.GroupBy(d => d.StateAlias, StringComparer.Ordinal))
        {
            var bound = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var slot in group.SelectMany(d => d.Slots))
            {
                if (!bound.TryAdd(slot.Name, slot.Contract) && bound[slot.Name] != slot.Contract)
                {
                    throw new SynapseShapeException($"{group.Key}: {SynapseReasons.SlotBoundTwice} '{slot.Name}' is bound to two contracts ({bound[slot.Name].FullName} and {slot.Contract.FullName}).");
                }
            }
        }

        return new SynapseCatalog(states, synapses);
    }

    private static string DisplayName(Type contract)
    {
        if (contract.GetCustomAttribute<DescriptionAttribute>() is { } description)
        {
            return description.Description;
        }

        var name = contract.Name;
        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
        {
            name = name[1..];
        }

        var words = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsUpper(c) && words.Length > 0)
            {
                words.Append(' ');
            }

            words.Append(char.ToLowerInvariant(c));
        }

        return words.ToString();
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
