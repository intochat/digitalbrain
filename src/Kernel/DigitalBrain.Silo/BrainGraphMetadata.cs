using System.Reflection;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;

namespace DigitalBrain.Kernel;

internal sealed record BrainGraphNeuronMetadata(string Label, string Module, IReadOnlyList<string> HandledSignals);

internal static class BrainGraphMetadata
{
    // Resolve metadata only for already-observed grain types. This is neither an
    // instance catalog nor permission to create a relationship from a capability.
    internal static BrainGraphNeuronMetadata For(string grainType)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic || assembly.GetName().Name?.StartsWith("DigitalBrain", StringComparison.Ordinal) != true)
            {
                continue;
            }

            foreach (var type in assembly.GetTypes())
            {
                if (type.IsAbstract || !typeof(Neuron).IsAssignableFrom(type))
                {
                    continue;
                }

                var declared = type.GetCustomAttributesData().FirstOrDefault(attribute =>
                    attribute.AttributeType == typeof(GrainTypeAttribute));
                var declaredType = declared?.ConstructorArguments.FirstOrDefault().Value as string ?? type.Name;
                if (!string.Equals(declaredType, grainType, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var assemblyName = assembly.GetName().Name!;
                const string modulePrefix = "DigitalBrain.Modules.";
                var module = assemblyName.StartsWith(modulePrefix, StringComparison.Ordinal)
                    ? assemblyName[modulePrefix.Length..].Split('.')[0] : "Kernel";
                var label = grainType switch
                {
                    "chat" => "Conversation",
                    "chat-turn-worker" => "Turn worker",
                    "assistant" => "Ino",
                    "sessionneuron" => "Owner session",
                    _ => type.Name.EndsWith("Neuron", StringComparison.Ordinal) ? type.Name[..^6] : type.Name,
                };
                return new(label, module, [.. type.GetInterfaces()
                    .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IHandle<>))
                    .Select(contract => contract.GetGenericArguments()[0].Name)
                    .Where(IsSubscriptionSignal).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
            }
        }

        return new(grainType, "Kernel", []);
    }

    internal static bool IsSubscriptionSignal(string signalType)
        => !string.IsNullOrWhiteSpace(signalType)
            && signalType is not (nameof(Subscribe) or nameof(Unsubscribe));
}
