using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Catalog;

[GenerateSerializer]
[Alias("db.catalog.entry-kind")]
public enum CatalogEntryKind
{
    Module = 0,
    Capability = 1,
    NeuronType = 2,
    NeuronInstance = 3,
    SignalContract = 4,
    Operation = 5,
    Script = 6,
    Automation = 7,
    AgentDefinition = 8,
    Entity = 9,
    Activity = 10,
}

[GenerateSerializer]
[Alias("db.catalog.lifecycle")]
public enum CatalogLifecycle
{
    Draft = 0,
    Active = 1,
    Suspended = 2,
    Retired = 3,
}

[GenerateSerializer]
[Alias("db.catalog.visibility")]
public enum CatalogVisibility
{
    Discoverable = 0,
    InspectOnly = 1,
}

[GenerateSerializer]
[Alias("db.catalog.configuration-state")]
public enum CatalogConfigurationState
{
    Declared = 0,
    Configured = 1,
    Disabled = 2,
}

[GenerateSerializer]
[Alias("db.catalog.discovery-text")]
public sealed record CatalogDiscoveryText
{
    [JsonConstructor]
    public CatalogDiscoveryText(
        IReadOnlyList<string>? aliases,
        IReadOnlyList<string>? keywords,
        IReadOnlyList<string>? tags,
        IReadOnlyList<string>? routingExamples,
        IReadOnlyList<string>? inputConcepts,
        IReadOnlyList<string>? outputConcepts,
        IReadOnlyList<string>? whenNotToUse)
    {
        Aliases = CatalogContractValidation.Set(aliases, nameof(aliases));
        Keywords = CatalogContractValidation.Set(keywords, nameof(keywords));
        Tags = CatalogContractValidation.Set(tags, nameof(tags));
        RoutingExamples = CatalogContractValidation.Ordered(routingExamples, nameof(routingExamples));
        InputConcepts = CatalogContractValidation.Set(inputConcepts, nameof(inputConcepts));
        OutputConcepts = CatalogContractValidation.Set(outputConcepts, nameof(outputConcepts));
        WhenNotToUse = CatalogContractValidation.Set(whenNotToUse, nameof(whenNotToUse));
    }

    [Id(0)] public IReadOnlyList<string> Aliases { get; }
    [Id(1)] public IReadOnlyList<string> Keywords { get; }
    [Id(2)] public IReadOnlyList<string> Tags { get; }
    [Id(3)] public IReadOnlyList<string> RoutingExamples { get; }
    [Id(4)] public IReadOnlyList<string> InputConcepts { get; }
    [Id(5)] public IReadOnlyList<string> OutputConcepts { get; }
    [Id(6)] public IReadOnlyList<string> WhenNotToUse { get; }

    public static CatalogDiscoveryText Empty { get; } = new(null, null, null, null, null, null, null);
}

[GenerateSerializer]
[Alias("db.catalog.target-kind")]
public enum CatalogTargetKind
{
    Module = 0,
    Capability = 1,
    NeuronType = 2,
    NeuronInstance = 3,
    SignalContract = 4,
    Operation = 5,
    Script = 6,
    Automation = 7,
    AgentDefinition = 8,
    Entity = 9,
    Activity = 10,
}

[GenerateSerializer]
[Alias("db.catalog.typed-reference")]
public sealed record CatalogTypedReference
{
    [JsonConstructor]
    public CatalogTypedReference(
        CatalogTargetKind kind,
        string? stableId,
        NeuronId? neuron,
        EntityId? entity,
        DurableInspectionReference? durable)
    {
        Kind = kind;
        StableId = CatalogContractValidation.Optional(stableId);
        Neuron = neuron;
        Entity = entity;
        Durable = durable;
        Validate();
    }

    [Id(0)] public CatalogTargetKind Kind { get; }
    [Id(1)] public string? StableId { get; }
    [Id(2)] public NeuronId? Neuron { get; }
    [Id(3)] public EntityId? Entity { get; }
    [Id(4)] public DurableInspectionReference? Durable { get; }

    public static CatalogTypedReference ForStable(CatalogTargetKind kind, string stableId)
        => new(kind, stableId, null, null, null);

    public static CatalogTypedReference ForNeuron(NeuronId neuron)
        => new(CatalogTargetKind.NeuronInstance, null, neuron, null, null);

    public static CatalogTypedReference ForEntity(EntityId entity)
        => new(CatalogTargetKind.Entity, null, null, entity, null);

    public static CatalogTypedReference ForDurable(
        CatalogTargetKind kind,
        DurableInspectionReference durable)
        => new(kind, null, null, null, durable);

    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind));
        }

        var payloads = (StableId is null ? 0 : 1) + (Neuron is null ? 0 : 1) +
            (Entity is null ? 0 : 1) + (Durable is null ? 0 : 1);
        if (payloads != 1)
        {
            throw new ArgumentException("A catalog target must carry exactly one payload.");
        }

        switch (Kind)
        {
            case CatalogTargetKind.Module:
            case CatalogTargetKind.Capability:
            case CatalogTargetKind.NeuronType:
            case CatalogTargetKind.SignalContract:
            case CatalogTargetKind.Operation:
                CatalogContractValidation.Required(StableId, nameof(StableId));
                break;
            case CatalogTargetKind.NeuronInstance when Neuron is { } neuron:
                CatalogContractValidation.ValidNeuron(neuron, nameof(Neuron));
                break;
            case CatalogTargetKind.Entity when Entity is { } entity:
                CatalogContractValidation.ValidEntity(entity, nameof(Entity));
                break;
            case CatalogTargetKind.Script:
            case CatalogTargetKind.Automation:
            case CatalogTargetKind.AgentDefinition:
            case CatalogTargetKind.Activity:
                if (Durable is null)
                {
                    throw new ArgumentException("A durable catalog target requires a durable reference.");
                }

                Durable.Validate();
                break;
            default:
                throw new ArgumentException("The catalog target payload does not match its kind.");
        }
    }

    public void ValidateFor(CatalogEntryKind descriptorKind, CatalogScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Validate();
        scope.Validate();

        if (!Enum.IsDefined(descriptorKind))
        {
            throw new ArgumentOutOfRangeException(nameof(descriptorKind));
        }

        if ((int)descriptorKind != (int)Kind)
        {
            throw new ArgumentException("The catalog target kind must match the descriptor kind.");
        }

        switch (Kind)
        {
            case CatalogTargetKind.Module:
            case CatalogTargetKind.Capability:
            case CatalogTargetKind.NeuronType:
            case CatalogTargetKind.SignalContract:
            case CatalogTargetKind.Operation:
                if (scope.Kind != CatalogScopeKind.Platform)
                {
                    throw new ArgumentException("A stable catalog definition requires platform scope.", nameof(scope));
                }

                break;
            case CatalogTargetKind.NeuronInstance when Neuron is { } neuron:
                RequireOwner(scope, neuron.Owner);
                break;
            case CatalogTargetKind.Entity when Entity is { } entity:
                RequireOwner(scope, entity.Owner);
                break;
            case CatalogTargetKind.Script:
            case CatalogTargetKind.Automation:
            case CatalogTargetKind.AgentDefinition:
            case CatalogTargetKind.Activity:
                if (scope.Kind != CatalogScopeKind.Owner)
                {
                    throw new ArgumentException("A durable catalog resource requires owner scope.", nameof(scope));
                }

                var expectedResourceKind = Kind switch
                {
                    CatalogTargetKind.Script => "script",
                    CatalogTargetKind.Automation => "automation",
                    CatalogTargetKind.AgentDefinition => "agent",
                    CatalogTargetKind.Activity => "activity",
                    _ => throw new InvalidOperationException("The durable target kind is unsupported."),
                };
                if (!string.Equals(Durable!.ResourceKind, expectedResourceKind, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"A {Kind} target requires durable resource kind '{expectedResourceKind}'.",
                        nameof(Durable));
                }

                break;
        }
    }

    private static void RequireOwner(CatalogScope scope, OwnerId owner)
    {
        if (scope.Kind != CatalogScopeKind.Owner || scope.Owner != owner)
        {
            throw new ArgumentException("The referenced resource owner must match the catalog scope.");
        }
    }
}

[GenerateSerializer]
[Alias("db.catalog.descriptor")]
public sealed record CatalogDescriptor
{
    [JsonConstructor]
    public CatalogDescriptor(
        CatalogReference reference,
        CatalogEntryKind kind,
        CatalogLifecycle lifecycle,
        CatalogVisibility visibility,
        CatalogConfigurationState configurationState,
        string name,
        string summary,
        CatalogDiscoveryText discovery,
        CatalogTypedReference target,
        CatalogNeuronDescriptor? neuron,
        CatalogSignalDescriptor? signal,
        CatalogCapabilityDescriptor? capability,
        CatalogOperationDescriptor? operation)
    {
        Reference = reference;
        Kind = kind;
        Lifecycle = lifecycle;
        Visibility = visibility;
        ConfigurationState = configurationState;
        Name = CatalogContractValidation.Required(name, nameof(name));
        Summary = CatalogContractValidation.Required(summary, nameof(summary));
        Discovery = discovery;
        Target = target;
        Neuron = neuron;
        Signal = signal;
        Capability = capability;
        Operation = operation;
        Validate();
    }

    [Id(0)] public CatalogReference Reference { get; init; }
    [Id(1)] public CatalogEntryKind Kind { get; init; }
    [Id(2)] public CatalogLifecycle Lifecycle { get; init; }
    [Id(3)] public CatalogVisibility Visibility { get; init; }
    [Id(4)] public CatalogConfigurationState ConfigurationState { get; init; }
    [Id(5)] public string Name { get; init; }
    [Id(6)] public string Summary { get; init; }
    [Id(7)] public CatalogDiscoveryText Discovery { get; init; }
    [Id(8)] public CatalogTypedReference Target { get; init; }
    [Id(9)] public CatalogNeuronDescriptor? Neuron { get; init; }
    [Id(10)] public CatalogSignalDescriptor? Signal { get; init; }
    [Id(11)] public CatalogCapabilityDescriptor? Capability { get; init; }
    [Id(12)] public CatalogOperationDescriptor? Operation { get; init; }

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Reference);
        ArgumentNullException.ThrowIfNull(Discovery);
        ArgumentNullException.ThrowIfNull(Target);
        Reference.Validate();
        CatalogContractValidation.Required(Name, nameof(Name));
        CatalogContractValidation.Required(Summary, nameof(Summary));

        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(Lifecycle) ||
            !Enum.IsDefined(Visibility) || !Enum.IsDefined(ConfigurationState))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind), "A catalog descriptor enum value is invalid.");
        }

        Target.ValidateFor(Kind, Reference.Scope);

        switch (Kind)
        {
            case CatalogEntryKind.Module:
            case CatalogEntryKind.Script:
            case CatalogEntryKind.Automation:
            case CatalogEntryKind.AgentDefinition:
            case CatalogEntryKind.Entity:
            case CatalogEntryKind.Activity:
                RejectMetadata(Neuron, Signal, Capability, Operation);
                break;
            case CatalogEntryKind.NeuronType:
            case CatalogEntryKind.NeuronInstance:
                ArgumentNullException.ThrowIfNull(Neuron);
                RejectMetadata(signal: Signal, capability: Capability, operation: Operation);
                Neuron.Validate();
                break;
            case CatalogEntryKind.SignalContract:
                ArgumentNullException.ThrowIfNull(Signal);
                RejectMetadata(neuron: Neuron, capability: Capability, operation: Operation);
                Signal.Validate();
                break;
            case CatalogEntryKind.Capability:
                ArgumentNullException.ThrowIfNull(Capability);
                RejectMetadata(neuron: Neuron, signal: Signal, operation: Operation);
                Capability.Validate();
                break;
            case CatalogEntryKind.Operation:
                ArgumentNullException.ThrowIfNull(Capability);
                ArgumentNullException.ThrowIfNull(Operation);
                RejectMetadata(neuron: Neuron, signal: Signal);
                Capability.Validate();
                Operation.Validate();
                if (!string.Equals(Capability.CapabilityId, Operation.CapabilityId, StringComparison.Ordinal) ||
                    !string.Equals(Capability.Version, Operation.CapabilityVersion, StringComparison.Ordinal))
                {
                    throw new ArgumentException("Operation capability metadata must identify the same capability version.");
                }

                break;
        }
    }

    private static void RejectMetadata(
        CatalogNeuronDescriptor? neuron = null,
        CatalogSignalDescriptor? signal = null,
        CatalogCapabilityDescriptor? capability = null,
        CatalogOperationDescriptor? operation = null)
    {
        if (neuron is not null || signal is not null || capability is not null || operation is not null)
        {
            throw new ArgumentException("Catalog metadata must match the descriptor kind exactly.");
        }
    }
}
