using System.Reflection;
using DigitalBrain.Abstractions;
using Orleans;

namespace DigitalBrain.Kernel;

public sealed class ActiveModuleContractTypeMap
{
    private readonly IReadOnlyDictionary<(string ContractId, int SchemaVersion), Type> _synapses;
    private readonly IReadOnlyDictionary<string, string> _neuronGrainTypes;

    private ActiveModuleContractTypeMap(
        IReadOnlyDictionary<(string ContractId, int SchemaVersion), Type> synapses,
        IReadOnlyDictionary<string, string> neuronGrainTypes)
    {
        _synapses = synapses;
        _neuronGrainTypes = neuronGrainTypes;
    }

    public static ActiveModuleContractTypeMap Create(
        IEnumerable<ICompiledModule> selectedModules,
        ActiveCapabilityCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(selectedModules);
        ArgumentNullException.ThrowIfNull(catalog);

        var assemblies = new HashSet<Assembly>();
        foreach (var module in selectedModules)
        {
            CollectAssemblies(module.GetType().Assembly, assemblies);
        }

        var catalogSynapses = IndexCatalogSynapses(catalog);
        var synapses = new Dictionary<(string, int), Type>();
        var neuronGrainTypes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var assembly in assemblies.OrderBy(static item => item.FullName, StringComparer.Ordinal))
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(static type => type is not null).Cast<Type>().ToArray();
            }

            foreach (var type in types.OrderBy(static item => item.FullName, StringComparer.Ordinal))
            {
                if (type is null)
                {
                    continue;
                }

                var alias = ReadSingleAlias(type);
                if (alias is null)
                {
                    continue;
                }

                if (typeof(Synapse).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
                {
                    IndexSynapse(catalogSynapses, synapses, type, alias);
                }

                if (typeof(INeuron).IsAssignableFrom(type) && type.IsInterface)
                {
                    IndexNeuron(catalog, neuronGrainTypes, type, alias);
                }
            }
        }

        return new ActiveModuleContractTypeMap(synapses, neuronGrainTypes);
    }

    public bool TryGetSynapseType(string contractId, int schemaVersion, out Type? type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        return _synapses.TryGetValue((contractId, schemaVersion), out type);
    }

    public bool TryGetNeuronGrainType(string contractId, out string? grainType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contractId);
        return _neuronGrainTypes.TryGetValue(contractId, out grainType);
    }

    private static Dictionary<(string ContractId, int SchemaVersion), SynapseCapabilityDescriptor> IndexCatalogSynapses(
        ActiveCapabilityCatalog catalog)
    {
        var index = new Dictionary<(string, int), SynapseCapabilityDescriptor>();
        foreach (var module in catalog.Modules)
        {
            foreach (var neuron in module.Neurons)
            {
                foreach (var synapse in neuron.Accepted.Concat(neuron.Emitted))
                {
                    index[(synapse.ContractId, synapse.SchemaVersion)] = synapse;
                }
            }
        }

        return index;
    }

    private static void IndexSynapse(
        IReadOnlyDictionary<(string ContractId, int SchemaVersion), SynapseCapabilityDescriptor> catalogSynapses,
        Dictionary<(string, int), Type> synapses,
        Type type,
        string alias)
    {
        string schema;
        try
        {
            schema = CapabilitySchema.For(type);
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (NotSupportedException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        foreach (var entry in catalogSynapses)
        {
            if (!string.Equals(entry.Key.ContractId, alias, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(schema, entry.Value.JsonSchema, StringComparison.Ordinal))
            {
                continue;
            }

            if (synapses.TryGetValue(entry.Key, out var existing) && existing != type)
            {
                throw new InvalidOperationException(
                    $"Collision mapping synapse '{entry.Key.ContractId}' v{entry.Key.SchemaVersion} to both '{existing}' and '{type}'.");
            }

            synapses[entry.Key] = type;
        }
    }

    private static void IndexNeuron(
        ActiveCapabilityCatalog catalog,
        Dictionary<string, string> neuronGrainTypes,
        Type type,
        string alias)
    {
        if (!catalog.TryGetNeuron(alias, out _))
        {
            return;
        }

        var grainType = NeuronId.GrainTypeNameOf(type);
        if (neuronGrainTypes.TryGetValue(alias, out var existing)
            && !string.Equals(existing, grainType, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Collision mapping neuron '{alias}' to grain types '{existing}' and '{grainType}'.");
        }

        neuronGrainTypes[alias] = grainType;
    }

    private static string? ReadSingleAlias(Type type)
    {
        string? selected = null;
        foreach (var attribute in type.GetCustomAttributes<AliasAttribute>(inherit: false))
        {
            if (string.IsNullOrWhiteSpace(attribute.Alias))
            {
                continue;
            }

            if (selected is not null)
            {
                return null;
            }

            selected = attribute.Alias;
        }

        return selected;
    }

    private static void CollectAssemblies(Assembly root, HashSet<Assembly> assemblies)
    {
        if (!assemblies.Add(root))
        {
            return;
        }

        foreach (var name in root.GetReferencedAssemblies().OrderBy(static item => item.FullName, StringComparer.Ordinal))
        {
            if (name.Name is null
                || !name.Name.StartsWith("DigitalBrain", StringComparison.Ordinal))
            {
                continue;
            }

            Assembly? referenced = null;
            foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(loaded.GetName().Name, name.Name, StringComparison.Ordinal))
                {
                    referenced = loaded;
                    break;
                }
            }

            if (referenced is null || referenced.IsDynamic)
            {
                continue;
            }

            CollectAssemblies(referenced, assemblies);
        }
    }
}
