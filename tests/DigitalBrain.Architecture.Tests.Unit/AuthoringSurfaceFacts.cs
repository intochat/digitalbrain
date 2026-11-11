using System.Reflection;
using DigitalBrain.Runtime.Synapses;
using Xunit;

namespace DigitalBrain.Architecture.Tests.Unit;

/// <summary>
/// A public type in an authoring namespace mentions no closed DigitalBrain type in its base, interfaces or visible signatures.
/// Method bodies and attributes are not read: neither hands a unit a type.
/// </summary>
public sealed class AuthoringSurfaceFacts
{
    private static readonly string[] Shipped =
    [
        "DigitalBrain",
        "DigitalBrain.UI",
        "DigitalBrain.Simulation",
        "DigitalBrain.Testing",
        "DigitalBrain.Identity.Local",
        "DigitalBrain.Runtime.Http",
        "DigitalBrain.Runtime.Synapses",
        "DigitalBrain.Capabilities.Compute",
        "DigitalBrain.Capabilities.Connections",
        "DigitalBrain.Capabilities.Http",
        "DigitalBrain.Capabilities.Identity",
        "DigitalBrain.Capabilities.Secrets",
        "DigitalBrain.Capabilities.Storage",
        "DigitalBrain.Capabilities.Tools",
    ];

    // Closed types the published language still mentions, as "Type -> Type". Each entry is a debt; the set may only shrink.
    private static readonly HashSet<string> Bridges = new(StringComparer.Ordinal)
    {
        "DigitalBrain.Capabilities.Connections.ConnectionNeuron`1 -> DigitalBrain.Neurons.IStateInspectable",
        "DigitalBrain.Capabilities.Connections.ConnectionNeuron`1 -> DigitalBrain.Neurons.Neuron`1",
        "DigitalBrain.Capabilities.Http.HttpAsks -> DigitalBrain.Units.Ask",
        "DigitalBrain.Capabilities.Http.HttpAsks -> DigitalBrain.Units.Asks`1",
        "DigitalBrain.Capabilities.Storage.IStorage -> DigitalBrain.Operations.OperationId",
        "DigitalBrain.Capabilities.Storage.IStorageVfs -> DigitalBrain.Operations.OperationId",
        "DigitalBrain.Capabilities.Storage.StorageConfirmation -> DigitalBrain.Confirmation.ConfirmationPlan",
        "DigitalBrain.Capabilities.Storage.StorageVfs -> DigitalBrain.Operations.OperationId",
        "DigitalBrain.Capabilities.Storage.StorageWriteHandler -> DigitalBrain.Operations.IOperationHandler`1",
        "DigitalBrain.Capabilities.Storage.StorageWriteHandler -> DigitalBrain.Operations.IOperationNeuron",
        "DigitalBrain.Capabilities.Storage.StorageWriteHandler -> DigitalBrain.Operations.OperationApproval",
        "DigitalBrain.Capabilities.Storage.StorageWriteHandler -> DigitalBrain.Operations.OperationResult",
        "DigitalBrain.Capabilities.Storage.StorageWriteHandler -> DigitalBrain.Operations.OperationScope",
        "DigitalBrain.Capabilities.Storage.StorageWriteResult -> DigitalBrain.Operations.OperationId",
        "DigitalBrain.Capabilities.Storage.StorageWriteResult -> DigitalBrain.Operations.OperationResult",
        "DigitalBrain.Capabilities.Storage.StorageWriteRunner -> DigitalBrain.Confirmation.ConfirmationPlan",
        "DigitalBrain.Capabilities.Storage.StorageWriteRunner -> DigitalBrain.Operations.IOperation",
        "DigitalBrain.Capabilities.Storage.StorageWriteRunner -> DigitalBrain.Operations.OperationResult",
        "DigitalBrain.Capabilities.Storage.WritePlan -> DigitalBrain.Operations.OperationPlan",
        "DigitalBrain.Capabilities.Tools.ToolAsks -> DigitalBrain.Units.Ask",
        "DigitalBrain.Capabilities.Tools.ToolAsks -> DigitalBrain.Units.Asks`1",
        "DigitalBrain.IScreen -> DigitalBrain.Units.PublishedView",
        "DigitalBrain.NeuronId -> DigitalBrain.Identity.CompoundGrainKey",
        "DigitalBrain.Reaction`1 -> DigitalBrain.Units.Ask",
        "DigitalBrain.Screen`1 -> DigitalBrain.Neurons.IStateInspectable",
        "DigitalBrain.Screen`1 -> DigitalBrain.Neurons.Neuron`1",
        "DigitalBrain.Screen`1 -> DigitalBrain.Units.PublishedView",
    };

    [Fact]
    public void APublicTypeInAnAuthoringNamespaceMentionsNoClosedDigitalBrainType()
    {
        var leaks = Shipped
            .Select(name => Assembly.Load(name))
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => Open(type.Namespace))
            .SelectMany(type => Signatures.Of(type, visibleOnly: true)
                .Where(mentioned => Closed(mentioned.Namespace))
                .Select(mentioned => $"{type.FullName} -> {mentioned.FullName}"))
            .Distinct()
            .Where(edge => !Bridges.Contains(edge))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(leaks.Count == 0, "Published-language types that mention a closed DigitalBrain type: " + string.Join(", ", leaks));
    }

    private static bool Open(string? ns) => ns is not null && AllowedApis.AuthoringNamespaces.Contains(ns, StringComparer.Ordinal);

    private static bool Closed(string? ns) => ns is not null && ns.StartsWith("DigitalBrain", StringComparison.Ordinal) && !Open(ns);
}
