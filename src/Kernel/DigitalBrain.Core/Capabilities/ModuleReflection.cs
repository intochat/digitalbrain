using System.Reflection;
using System.Text;
using DigitalBrain.Abstractions;

namespace DigitalBrain.Core;

public static class ModuleReflection
{
    private const string ContractsSuffix = ".Contracts";
    private const string DefaultInstanceMemberName = "DefaultInstanceName";

    public static CapabilityManifest ManifestOf(Assembly contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        var assemblyName = contracts.GetName().Name
            ?? throw new InvalidOperationException("A contracts assembly must carry a name.");
        var moduleName = assemblyName.EndsWith(ContractsSuffix, StringComparison.Ordinal)
            ? assemblyName[..^ContractsSuffix.Length]
            : assemblyName;

        return new CapabilityManifest(
            new ModuleId(moduleName),
            "1.0.0",
            Humanized(moduleName[(moduleName.LastIndexOf('.') + 1)..]),
            [.. NeuronContracts(contracts).Select(Described)],
            [.. FactVocabulary(contracts)]);
    }

    private static IEnumerable<Type> NeuronContracts(Assembly contracts)
        => contracts.GetTypes()
            .Where(static type => type is { IsInterface: true, IsPublic: true }
                && type != typeof(INeuron)
                && typeof(INeuron).IsAssignableFrom(type))
            .OrderBy(static type => type.Name, StringComparer.Ordinal);

    private static NeuronCapabilityDescriptor Described(Type neuronContract)
    {
        var accepted = HandledSynapses(neuronContract)
            .Select(DescriptorFor)
            .ToArray();
        var emitted = HandledSynapses(neuronContract)
            .Select(ReplyTypeOf)
            .OfType<Type>()
            .Distinct()
            .Select(DescriptorFor)
            .ToArray();

        return new NeuronCapabilityDescriptor(
            ContractIdOf(neuronContract),
            Humanized(TrimmedInterfaceName(neuronContract)),
            DefaultInstanceNameOf(neuronContract),
            accepted,
            emitted);
    }

    private static IEnumerable<Type> HandledSynapses(Type neuronContract)
        => neuronContract.GetInterfaces()
            .Where(static contract => contract.IsGenericType
                && contract.GetGenericTypeDefinition() == typeof(IHandle<>))
            .Select(static contract => contract.GenericTypeArguments[0])
            .OrderBy(static synapse => synapse.Name, StringComparer.Ordinal);

    private static IEnumerable<SynapseCapabilityDescriptor> FactVocabulary(Assembly contracts)
        => contracts.GetTypes()
            .Where(static type => type is { IsClass: true, IsAbstract: false }
                && typeof(Synapse).IsAssignableFrom(type))
            .OrderBy(static type => type.Name, StringComparer.Ordinal)
            .Select(DescriptorFor);

    private static SynapseCapabilityDescriptor DescriptorFor(Type synapse)
        => new(
            SynapseAlias.Of(synapse) ?? synapse.Name,
            1,
            Humanized(synapse.Name),
            CapabilitySchema.For(synapse));

    private static Type? ReplyTypeOf(Type synapse)
    {
        for (var probed = synapse.BaseType; probed is not null; probed = probed.BaseType)
        {
            if (probed.IsGenericType && probed.GetGenericTypeDefinition() == typeof(RequestSynapse<>))
            {
                return probed.GenericTypeArguments[0];
            }
        }

        return null;
    }

    private static string ContractIdOf(Type neuronContract)
        => neuronContract.GetCustomAttributesData()
                .FirstOrDefault(static attribute => attribute.AttributeType == typeof(AliasAttribute))?
                .ConstructorArguments[0].Value as string
            ?? TrimmedInterfaceName(neuronContract).ToLowerInvariant();

    private static string DefaultInstanceNameOf(Type neuronContract)
        => neuronContract.GetField(DefaultInstanceMemberName)?.GetRawConstantValue() as string
            ?? neuronContract.GetField("InstanceName")?.GetRawConstantValue() as string
            ?? "default";

    private static string TrimmedInterfaceName(Type contract)
        => contract.Name.Length > 1 && contract.Name[0] == 'I' && char.IsUpper(contract.Name[1])
            ? contract.Name[1..]
            : contract.Name;

    // "StartTimer" → "Start timer"; "time.start-timer" callers pass type names, not aliases.
    private static string Humanized(string identifier)
    {
        var words = new StringBuilder(identifier.Length + 8);

        for (var index = 0; index < identifier.Length; index++)
        {
            var letter = identifier[index];
            if (index > 0 && char.IsUpper(letter) && !char.IsUpper(identifier[index - 1]))
            {
                words.Append(' ').Append(char.ToLowerInvariant(letter));
                continue;
            }

            words.Append(letter);
        }

        return words.ToString();
    }
}
