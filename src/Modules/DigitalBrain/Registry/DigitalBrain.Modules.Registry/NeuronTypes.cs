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
                var assembly = module.Assembly;
                var contracts = assembly.GetReferencedAssemblies()
                    .Where(reference => reference.Name == assembly.GetName().Name + ".Contracts").Select(Assembly.Load).ToArray();
                if (contracts.Length == 0) { contracts = [assembly]; }
                foreach (var contract in contracts.SelectMany(source => source.GetExportedTypes())
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
                    types.Add(id, (contract, new(id, module.FullName!, name,
                        contract.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name, Array.AsReadOnly(methods))));
                }
            }
            _types = Array.AsReadOnly(types.Values.Select(type => type.Metadata).OrderBy(type => type.Id, StringComparer.Ordinal).ToArray());
            return _types;
        }
    }
}