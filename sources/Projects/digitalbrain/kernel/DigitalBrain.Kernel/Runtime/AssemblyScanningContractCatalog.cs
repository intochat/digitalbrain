using System.Reflection;
using System.Runtime.CompilerServices;
using DigitalBrain.Runtime.Runtime;
using DigitalBrain.Kernel.Creator.InoAuthoring;
using DigitalBrain.InoLang.Linking;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel.Runtime;

public sealed class AssemblyScanningContractCatalog : IContractCatalog
{
    static readonly ContractSchema[] SystemSignalSchemas =
    [
        new("DigitalBrain.Kernel.Loaded", ContractKind.Synapse, []),
        new("DigitalBrain.Brain.Started", ContractKind.Synapse, []),
        new(InoCreatorNeuron.AuthoredSignalFqn, ContractKind.Synapse,
            InoCreatorNeuron.AuthoredSignalFields),
        new("DigitalBrain.Developer.Specs.ReviewReplied", ContractKind.Synapse, ["approved"]),
        new("DigitalBrain.Developer.Specs.FileReplied", ContractKind.Synapse, ["success"]),
        new("DigitalBrain.Developer.Specs.GitReplied", ContractKind.Synapse, ["success"]),
        new("DigitalBrain.Custom.EmailSummaryCompleted", ContractKind.Synapse, ["success"]),
    ];

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ContractSchema> _schemas;

    public AssemblyScanningContractCatalog(IEnumerable<Assembly> contractAssemblies)
    {
        ArgumentNullException.ThrowIfNull(contractAssemblies);
        _schemas = BuildSchemas(contractAssemblies.Distinct().ToArray());
    }

    private static readonly string DynamicCatalogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DigitalBrain",
        "dynamic-catalog.json");

    private static List<ContractSchema> LoadDynamicSchemas()
    {
        try
        {
            if (File.Exists(DynamicCatalogPath))
            {
                var json = File.ReadAllText(DynamicCatalogPath);
                var list = System.Text.Json.JsonSerializer.Deserialize<List<ContractSchema>>(json);
                return list ?? new List<ContractSchema>();
            }
        }
        catch
        {
        }
        return new List<ContractSchema>();
    }

    private static void SaveDynamicSchema(ContractSchema schema)
    {
        try
        {
            var schemas = LoadDynamicSchemas();
            schemas.RemoveAll(s => s.Fqn.Equals(schema.Fqn, StringComparison.OrdinalIgnoreCase));
            schemas.Add(schema);

            var dir = Path.GetDirectoryName(DynamicCatalogPath);
            if (dir != null) Directory.CreateDirectory(dir);

            var json = System.Text.Json.JsonSerializer.Serialize(schemas, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(DynamicCatalogPath, json);
        }
        catch
        {
        }
    }

    public ContractSchema? Resolve(string fqn) =>
        _schemas.TryGetValue(fqn, out var schema) ? schema : null;

    public IReadOnlyCollection<ContractSchema> GetAllSchemas() => _schemas.Values.ToArray();

    public void Register(ContractSchema schema)
    {
        _schemas[schema.Fqn] = schema;
        SaveDynamicSchema(schema);
    }

    public static IEnumerable<Assembly> DiscoverContractAssemblies() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(IsContractAssembly);

    static bool IsContractAssembly(Assembly assembly) =>
        assembly.IsDefined(typeof(ContractAssemblyAttribute), inherit: false);

    static System.Collections.Concurrent.ConcurrentDictionary<string, ContractSchema> BuildSchemas(IReadOnlyList<Assembly> assemblies)
    {
        var byFqn = new Dictionary<string, ContractSchema>(StringComparer.Ordinal);
        foreach (var schema in SystemSignalSchemas)
            byFqn[schema.Fqn] = schema;

        foreach (var schema in LoadDynamicSchemas())
            byFqn[schema.Fqn] = schema;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            foreach (var type in SafeGetTypes(assembly))
            {
                if (!IsConcreteNeuronTarget(type)) continue;
                if (GetGrainTypeFqn(type) is not { } fqn) continue;
                byFqn[fqn] = new ContractSchema(fqn, ContractKind.Neuron, []);
            }

        foreach (var assembly in assemblies)
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                if (!IsConcreteSynapseRecord(type)) continue;
                if (type.FullName is not { } fqn) continue;
                byFqn[fqn] = new ContractSchema(fqn, ContractKind.Synapse, CollectDomainFields(type));
            }
        }

        foreach (var assembly in assemblies)
        {
            foreach (var type in SafeGetTypes(assembly))
            {
                if (GetSignalIdentity(type) is not { } signalIdentity) continue;
                if (byFqn.TryGetValue(signalIdentity, out var existing) &&
                    existing.Kind == ContractKind.Synapse) continue;
                byFqn[signalIdentity] = new ContractSchema(
                    signalIdentity, ContractKind.Synapse, CollectDomainFields(type));
            }
        }
        return new System.Collections.Concurrent.ConcurrentDictionary<string, ContractSchema>(byFqn, StringComparer.Ordinal);
    }

    static bool IsConcreteNeuronTarget(Type type) =>
        !type.IsAbstract
        && type.IsClass
        && (typeof(ICallNeuronTarget).IsAssignableFrom(type)
            || typeof(IStreamNeuronTarget).IsAssignableFrom(type)
            || typeof(IResourceNeuronTarget).IsAssignableFrom(type)
            || typeof(IPredicateNeuronTarget).IsAssignableFrom(type));

    static string? GetGrainTypeFqn(Type type) =>
        type.GetCustomAttributesData()
            .FirstOrDefault(a => a.AttributeType.FullName == "Orleans.GrainTypeAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value as string;

    static string? GetSignalIdentity(Type type)
    {
        if (type.IsAbstract || !type.IsClass) return null;
        return type.GetCustomAttributesData()
            .FirstOrDefault(a =>
                a.AttributeType.FullName == "DigitalBrain.Runtime.Runtime.SignalAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value as string;
    }

    static bool IsConcreteSynapseRecord(Type type) =>
        !type.IsAbstract
        && type.IsClass
        && typeof(Synapse).IsAssignableFrom(type)
        && type != typeof(Synapse);

    static IReadOnlyList<string> CollectDomainFields(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => !property.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
            .Select(property => property.Name)
            .ToList();

        if (type.Name == "RfwCard" && !properties.Contains("ReceiverNeuronType"))
        {
            properties.Add("ReceiverNeuronType");
        }

        return properties.ToArray();
    }

    static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t is not null)!; }
    }
}
