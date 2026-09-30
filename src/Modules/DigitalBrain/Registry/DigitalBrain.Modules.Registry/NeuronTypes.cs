using System.ComponentModel;
using System.Reflection;
using DigitalBrain.Contracts;
using DigitalBrain.Core;

namespace DigitalBrain.Registry;

internal sealed class NeuronTypes(ModuleInventory modules)
{
    private readonly Lock _gate = new();
    private IReadOnlyList<NeuronType>? _types;

    public IReadOnlyList<NeuronType> Read()
    {
        lock (_gate)
        {
            if (_types is not null) { return _types; }
            var types = new Dictionary<string, (Type Contract, NeuronType Metadata)>(StringComparer.Ordinal);
            foreach (var module in modules.Types)
            {
                foreach (var contracts in ModuleInventory.ContractAssembliesOf(module.Assembly))
                {
                    var signals = contracts.GetExportedTypes()
                        .Where(type => typeof(Signal).IsAssignableFrom(type) && type != typeof(Signal) && !type.IsAbstract)
                        .ToArray();
                    foreach (var contract in contracts.GetExportedTypes()
                        .Where(type => type.IsInterface && type != typeof(INeuron) && typeof(INeuron).IsAssignableFrom(type)))
                    {
                        var id = contract.GetCustomAttribute<AliasAttribute>()?.Alias;
                        if (string.IsNullOrWhiteSpace(id)) { throw new InvalidOperationException($"Public neuron contract {contract.FullName} requires an Orleans alias."); }
                        if (types.TryGetValue(id, out var previous))
                        {
                            if (previous.Contract != contract) { throw new InvalidOperationException($"Duplicate neuron alias '{id}'."); }
                            continue;
                        }
                        var name = contract.Name.StartsWith('I') ? contract.Name[1..] : contract.Name;
                        var methods = new[] { contract }.Concat(contract.GetInterfaces())
                            .Where(type => !type.IsAssignableFrom(typeof(INeuron)))
                            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                            .Where(method => !method.IsSpecialName)
                            .Select(method => method + (method.GetCustomAttribute<DescriptionAttribute>() is { } description ? " — " + description.Description : ""))
                            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
                        types.Add(id, (contract, new(id, NeuronType.ModuleIdOf(module), name,
                            contract.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name, Array.AsReadOnly(methods),
                            contract.FullName!, Array.AsReadOnly(SignalsNear(contract, signals)))));
                    }
                }
            }
            _types = Array.AsReadOnly(types.Values.Select(type => type.Metadata).OrderBy(type => type.Id, StringComparer.Ordinal).ToArray());
            return _types;
        }
    }

    // Signals live beside their neuron contract ("...Timers.ITimer" with "...Timers.Signals.TimerTick"),
    // so a contract carries the signals of its own namespace and its ".Signals" sub-namespace.
    private static string[] SignalsNear(Type contract, Type[] signals) => [.. signals
        .Where(signal => signal.Namespace == contract.Namespace || signal.Namespace == contract.Namespace + ".Signals")
        .Select(Describe)
        .Order(StringComparer.Ordinal)];

    private static string Describe(Type signal)
        => signal.FullName + " { " + string.Join("; ", signal
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.DeclaringType != typeof(Signal))
            .Select(property => TypeName(property.PropertyType) + " " + property.Name)) + " }";

    private static string TypeName(Type type) => type.IsGenericType
        ? type.Name.Split('`')[0] + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">"
        : type.Name;
}
