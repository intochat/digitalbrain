using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.SourceGeneration;

public sealed partial class DispatchManifestGenerator
{
    private static readonly DiagnosticDescriptor SynapseAliasRequired = new(
        "DBGEN007",
        "Capability synapses require a stable Alias",
        "Synapse '{0}' accepted or emitted by '{1}' must declare an Orleans Alias for the capability catalog",
        "DigitalBrain.SourceGeneration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor CapabilityDescriptionRequired = new(
        "DBGEN008",
        "Capability descriptors require a description",
        "Capability member '{0}' is missing a DescriptionAttribute required by the exact catalog",
        "DigitalBrain.SourceGeneration",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static ImmutableArray<NeuronCapabilityModel> CapabilitiesOf(
        Compilation compilation,
        INamedTypeSymbol module,
        CancellationToken cancellationToken)
    {
        var handle = compilation.GetTypeByMetadataName("DigitalBrain.Abstractions.IHandle`1");
        var emit = compilation.GetTypeByMetadataName("DigitalBrain.Abstractions.IEmit`1");
        if (handle is null || emit is null)
        {
            return ImmutableArray<NeuronCapabilityModel>.Empty;
        }

        var byContract = new Dictionary<string, NeuronCapabilityAccumulator>(StringComparer.Ordinal);

        foreach (var type in TypesIn(module.ContainingAssembly.GlobalNamespace))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (type.TypeKind != TypeKind.Class || type.IsAbstract)
            {
                continue;
            }

            foreach (var contract in type.AllInterfaces)
            {
                var definition = contract.OriginalDefinition;
                var isHandler = SymbolEqualityComparer.Default.Equals(definition, handle);
                var isEmitter = SymbolEqualityComparer.Default.Equals(definition, emit);
                if (!isHandler && !isEmitter)
                {
                    continue;
                }

                var neuronContract = DomainNeuronContract(type);
                if (neuronContract is null)
                {
                    continue;
                }

                var contractId = neuronContract.ToDisplayString(FullName);
                if (!byContract.TryGetValue(contractId, out var accumulator))
                {
                    var description = DescriptionOf(neuronContract) ?? neuronContract.Name;
                    accumulator = new NeuronCapabilityAccumulator(
                        contractId,
                        description,
                        DescriptionOf(neuronContract) is not null,
                        neuronContract.Locations.FirstOrDefault() ?? Location.None);
                    byContract[contractId] = accumulator;
                }

                var synapse = (INamedTypeSymbol)contract.TypeArguments[0];
                var synapseModel = SynapseOf(synapse, type.ToDisplayString(FullName));
                if (isHandler)
                {
                    accumulator.Accepted.Add(synapseModel);
                }
                else
                {
                    accumulator.Emitted.Add(synapseModel);
                }
            }
        }

        return byContract.Values
            .OrderBy(entry => entry.ContractId, StringComparer.Ordinal)
            .Select(entry => new NeuronCapabilityModel(
                entry.ContractId,
                entry.Description,
                "default",
                entry.HasDescription,
                entry.Location,
                DistinctSynapses(entry.Accepted),
                DistinctSynapses(entry.Emitted)))
            .ToImmutableArray();
    }

    private static ImmutableArray<SynapseCapabilityModel> DistinctSynapses(
        IEnumerable<SynapseCapabilityModel> synapses)
        => synapses
            .GroupBy(synapse => synapse.ContractId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(synapse => synapse.ContractId, StringComparer.Ordinal)
            .ToImmutableArray();

    private static INamedTypeSymbol? DomainNeuronContract(INamedTypeSymbol implementation)
    {
        foreach (var contract in implementation.AllInterfaces)
        {
            if (contract.IsGenericType)
            {
                continue;
            }

            if (contract.ToDisplayString(FullName) == NeuronInterface)
            {
                continue;
            }

            if (contract.AllInterfaces.Any(parent => parent.ToDisplayString(FullName) == NeuronInterface))
            {
                return contract;
            }
        }

        return null;
    }

    private static SynapseCapabilityModel SynapseOf(INamedTypeSymbol synapse, string ownerNeuron)
    {
        var alias = AliasOf(synapse);
        var description = DescriptionOf(synapse) ?? synapse.Name;
        return new SynapseCapabilityModel(
            alias ?? synapse.ToDisplayString(FullName),
            synapse.ToDisplayString(FullName),
            schemaVersion: 1,
            description,
            alias is not null,
            DescriptionOf(synapse) is not null,
            synapse.Locations.FirstOrDefault() ?? Location.None,
            ownerNeuron);
    }

    private static string? AliasOf(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString(FullName) != AliasAttribute)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string alias
                && alias.Length > 0)
            {
                return alias;
            }
        }

        return null;
    }

    private static string? DescriptionOf(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString(FullName)
                != "System.ComponentModel.DescriptionAttribute")
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string description
                && description.Length > 0)
            {
                return description;
            }
        }

        return null;
    }

    private static void ReportCapabilityDiagnostics(
        SourceProductionContext production,
        ImmutableArray<NeuronCapabilityModel> neurons,
        bool requireDescriptions)
    {
        foreach (var neuron in neurons)
        {
            if (requireDescriptions && !neuron.HasDescription)
            {
                production.ReportDiagnostic(Diagnostic.Create(
                    CapabilityDescriptionRequired,
                    neuron.Location,
                    neuron.ContractId));
            }

            foreach (var synapse in neuron.Accepted.Concat(neuron.Emitted))
            {
                if (!synapse.HasAlias)
                {
                    production.ReportDiagnostic(Diagnostic.Create(
                        SynapseAliasRequired,
                        synapse.Location,
                        synapse.TypeName,
                        synapse.OwnerNeuron));
                }

                if (requireDescriptions && !synapse.HasDescription)
                {
                    production.ReportDiagnostic(Diagnostic.Create(
                        CapabilityDescriptionRequired,
                        synapse.Location,
                        synapse.TypeName));
                }
            }
        }
    }

    private static void AppendCapabilities(StringBuilder source, ModuleCapsuleModel module)
    {
        source.AppendLine();
        source.AppendLine("    public static global::DigitalBrain.Abstractions.CapabilityManifest Capabilities { get; } =");
        source.AppendLine("        new(");
        source.AppendLine("            Id,");
        source.AppendLine("            \"1.0.0\",");
        source.AppendLine($"            \"{Escape(module.Name)} module\",");
        source.AppendLine("            global::System.Array.Empty<string>(),");
        source.AppendLine("            [");

        foreach (var neuron in module.Neurons)
        {
            source.AppendLine("                new global::DigitalBrain.Abstractions.NeuronCapabilityDescriptor(");
            source.AppendLine($"                    \"{Escape(neuron.ContractId)}\",");
            source.AppendLine($"                    \"{Escape(neuron.Description)}\",");
            source.AppendLine($"                    \"{Escape(neuron.DefaultInstanceName)}\",");
            AppendSynapseArray(source, neuron.Accepted, "                    ");
            source.AppendLine(",");
            AppendSynapseArray(source, neuron.Emitted, "                    ");
            source.AppendLine("),");
        }

        source.AppendLine("            ]);");
        source.AppendLine();
        source.AppendLine("    global::DigitalBrain.Abstractions.CapabilityManifest");
        source.AppendLine("        global::DigitalBrain.Kernel.ICompiledModule.Capabilities => Capabilities;");
    }

    private static void AppendSynapseArray(
        StringBuilder source,
        ImmutableArray<SynapseCapabilityModel> synapses,
        string indent)
    {
        if (synapses.Length == 0)
        {
            source.Append($"{indent}global::System.Array.Empty<global::DigitalBrain.Abstractions.SynapseCapabilityDescriptor>()");
            return;
        }

        source.AppendLine($"{indent}[");
        foreach (var synapse in synapses)
        {
            source.AppendLine($"{indent}    new global::DigitalBrain.Abstractions.SynapseCapabilityDescriptor(");
            source.AppendLine($"{indent}        \"{Escape(synapse.ContractId)}\",");
            source.AppendLine($"{indent}        {synapse.SchemaVersion},");
            source.AppendLine($"{indent}        \"{Escape(synapse.Description)}\",");
            source.AppendLine($"{indent}        global::DigitalBrain.Abstractions.CapabilitySchema.For(typeof(global::{synapse.TypeName})),");
            source.AppendLine($"{indent}        global::System.Array.Empty<string>()),");
        }

        source.Append($"{indent}]");
    }

    private static string Escape(string value)
        => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private sealed class NeuronCapabilityAccumulator(
        string contractId,
        string description,
        bool hasDescription,
        Location location)
    {
        public string ContractId { get; } = contractId;

        public string Description { get; } = description;

        public bool HasDescription { get; } = hasDescription;

        public Location Location { get; } = location;

        public List<SynapseCapabilityModel> Accepted { get; } = [];

        public List<SynapseCapabilityModel> Emitted { get; } = [];
    }

    private sealed class NeuronCapabilityModel(
        string contractId,
        string description,
        string defaultInstanceName,
        bool hasDescription,
        Location location,
        ImmutableArray<SynapseCapabilityModel> accepted,
        ImmutableArray<SynapseCapabilityModel> emitted)
    {
        public string ContractId { get; } = contractId;

        public string Description { get; } = description;

        public string DefaultInstanceName { get; } = defaultInstanceName;

        public bool HasDescription { get; } = hasDescription;

        public Location Location { get; } = location;

        public ImmutableArray<SynapseCapabilityModel> Accepted { get; } = accepted;

        public ImmutableArray<SynapseCapabilityModel> Emitted { get; } = emitted;
    }

    private sealed class SynapseCapabilityModel(
        string contractId,
        string typeName,
        int schemaVersion,
        string description,
        bool hasAlias,
        bool hasDescription,
        Location location,
        string ownerNeuron)
    {
        public string ContractId { get; } = contractId;

        public string TypeName { get; } = typeName;

        public int SchemaVersion { get; } = schemaVersion;

        public string Description { get; } = description;

        public bool HasAlias { get; } = hasAlias;

        public bool HasDescription { get; } = hasDescription;

        public Location Location { get; } = location;

        public string OwnerNeuron { get; } = ownerNeuron;
    }
}
