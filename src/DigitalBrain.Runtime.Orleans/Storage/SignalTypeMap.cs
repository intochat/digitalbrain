using DigitalBrain.Limits;
using DigitalBrain.Units;
using System.Collections.Concurrent;
using System.Reflection;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// The closed set of signal types the journal decodes: <c>[Signal(alias)]</c> records from the composed assemblies plus the framework signals
/// <see cref="Gap"/>, <see cref="DigitalBrain.Rules.Rejected"/> and <see cref="StateChanged"/>. A type name read from disk never reaches <see cref="Type.GetType(string)"/> (T-04-12).
/// Thread-safe: <see cref="Register"/>, <see cref="Replace"/> and <see cref="Unregister"/> change the set while the silo runs.
/// </summary>
internal sealed class SignalTypeMap
{
    private readonly Lock gate = new();
    private readonly ConcurrentDictionary<string, Type> byAlias = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, string> byType = new();

    /// <exception cref="InvalidOperationException">Two types share one alias.</exception>
    public SignalTypeMap(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        Add(typeof(Gap));
        Add(typeof(DigitalBrain.Rules.Rejected));
        Add(typeof(StateChanged));
        Add(typeof(SynapseFailed));
        // The installs and time neurons every silo hosts (NeuronTypeRegistry) publish these.
        Add(typeof(Installed));
        Add(typeof(Retired));
        Add(typeof(InstallRequested));
        Add(typeof(InstallDenied));
        // The sources neuron every silo hosts journals the deploys of generated sources.
        Add(typeof(Deployed));
        Add(typeof(Staged));
        Add(typeof(Undeployed));
        Add(typeof(StageChanged));
        Add(typeof(StageDropped));
        Add(typeof(AliasesClaimed));
        Add(typeof(AliasesReleased));
        // The installs neuron completes an install on the resolution of its plan.
        Add(typeof(DigitalBrain.Confirmation.ConfirmationRequested));
        Add(typeof(DigitalBrain.Confirmation.ConfirmationResolved));
        Add(typeof(PolicyProposed));
        Add(typeof(PolicyAdopted));
        Add(typeof(PolicyRefused));
        Add(typeof(PolicyRetired));
        Add(typeof(DigitalBrain.Time.Tick));
        Add(typeof(DigitalBrain.Time.Registered));
        Add(typeof(DigitalBrain.Time.Unregistered));
        foreach (var assembly in assemblies.Distinct())
        {
            foreach (var type in Loadable(assembly))
            {
                if (type.IsAbstract || !typeof(Signal).IsAssignableFrom(type) || type.GetCustomAttribute<SignalAttribute>() is null) { continue; }
                Add(type);
            }
        }
    }

    public IReadOnlyCollection<string> Aliases => [.. byAlias.Keys];

    public bool TryResolve(string alias, out Type type) => byAlias.TryGetValue(alias, out type!);

    public bool TryAliasOf(Type type, out string alias) => byType.TryGetValue(type, out alias!);

    /// <summary>Adds the <c>[Signal]</c> type <paramref name="type"/>; adding it again is a no-op.</summary>
    /// <exception cref="InvalidOperationException">Another type holds the alias (only <see cref="Replace"/> swaps it), or the type has no <c>[Signal]</c>.</exception>
    public void Register(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (gate) { Add(type); }
    }

    /// <summary>Puts <paramref name="type"/> behind its alias, dropping the type that held it; adds it when the alias is new.</summary>
    /// <exception cref="InvalidOperationException">The type has no <c>[Signal]</c>.</exception>
    public void Replace(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (gate)
        {
            var alias = AliasOf(type);
            if (byAlias.TryGetValue(alias, out var existing)) { byType.TryRemove(existing, out _); }
            byAlias[alias] = type;
            byType[type] = alias;
        }
    }

    /// <summary>Drops <paramref name="alias"/> and its type; an unknown alias is ignored.</summary>
    public void Unregister(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        lock (gate)
        {
            if (byAlias.TryRemove(alias, out var type)) { byType.TryRemove(type, out _); }
        }
    }

    private static string AliasOf(Type type)
        => type.GetCustomAttribute<SignalAttribute>()?.Alias ?? throw new InvalidOperationException($"{type.FullName} has no SignalAttribute.");

    private void Add(Type type)
    {
        var alias = AliasOf(type);
        if (byAlias.TryGetValue(alias, out var existing))
        {
            if (existing == type) { return; }
            throw new InvalidOperationException($"Signal alias '{alias}' is declared by both {existing.FullName} and {type.FullName}.");
        }
        byAlias[alias] = type;
        byType[type] = alias;
    }

    private static IEnumerable<Type> Loadable(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error) { return error.Types.OfType<Type>(); }
    }
}

/// <summary>The assemblies whose <c>[Signal]</c> records the journal decodes; one instance per <c>AddJournalPayloads</c> call.</summary>
internal sealed record SignalAssemblies(Assembly[] Assemblies);
