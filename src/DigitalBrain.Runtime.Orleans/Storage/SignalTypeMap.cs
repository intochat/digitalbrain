using System.Reflection;

namespace DigitalBrain.Runtime.Orleans;

/// <summary>
/// The closed set of signal types the journal decodes: <c>[Signal(alias)]</c> records from the composed assemblies plus the framework signals
/// <see cref="Gap"/>, <see cref="DigitalBrain.Rules.Rejected"/> and <see cref="StateChanged"/>. A type name read from disk never reaches <see cref="Type.GetType(string)"/> (T-04-12).
/// </summary>
internal sealed class SignalTypeMap
{
    private readonly Dictionary<string, Type> byAlias = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> byType = [];

    /// <exception cref="InvalidOperationException">Two types share one alias.</exception>
    public SignalTypeMap(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);
        Add(typeof(Gap));
        Add(typeof(DigitalBrain.Rules.Rejected));
        Add(typeof(StateChanged));
        // The installs and time neurons every silo hosts (NeuronTypeRegistry) publish these.
        Add(typeof(Installed));
        Add(typeof(Retired));
        Add(typeof(InstallRequested));
        Add(typeof(InstallDenied));
        // The installs neuron completes an install on the resolution of its plan.
        Add(typeof(DigitalBrain.Confirmation.ConfirmationRequested));
        Add(typeof(DigitalBrain.Confirmation.ConfirmationResolved));
        Add(typeof(GateChanged));
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

    public IReadOnlyCollection<string> Aliases => byAlias.Keys;

    public bool TryResolve(string alias, out Type type) => byAlias.TryGetValue(alias, out type!);

    public bool TryAliasOf(Type type, out string alias) => byType.TryGetValue(type, out alias!);

    private void Add(Type type)
    {
        var alias = type.GetCustomAttribute<SignalAttribute>()!.Alias;
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
