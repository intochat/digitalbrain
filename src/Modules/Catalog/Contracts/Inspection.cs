using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Catalog;

[GenerateSerializer]
[Alias("db.catalog.inspection-reference-kind")]
public enum InspectionReferenceKind
{
    CatalogDescriptor = 0,
    Neuron = 1,
    Synapse = 2,
    Entity = 3,
    DurableResource = 4,
}

[GenerateSerializer]
[Alias("db.catalog.synapse-reference")]
public sealed record SynapseReference
{
    [JsonConstructor]
    public SynapseReference(NeuronId source, NeuronId target, string signalType)
    {
        CatalogContractValidation.ValidNeuron(source, nameof(source));
        CatalogContractValidation.ValidNeuron(target, nameof(target));
        Source = source;
        Target = target;
        SignalType = CatalogContractValidation.Required(signalType, nameof(signalType));
    }

    [Id(0)] public NeuronId Source { get; }
    [Id(1)] public NeuronId Target { get; }
    [Id(2)] public string SignalType { get; }
}

[GenerateSerializer]
[Alias("db.catalog.durable-inspection-reference")]
public sealed record DurableInspectionReference
{
    [JsonConstructor]
    public DurableInspectionReference(string resourceKind, string resourceId, string? revision)
    {
        ResourceKind = CatalogContractValidation.Required(resourceKind, nameof(resourceKind))
            .ToLowerInvariant();
        ResourceId = CatalogContractValidation.Required(resourceId, nameof(resourceId));
        Revision = CatalogContractValidation.Optional(revision);
    }

    [Id(0)] public string ResourceKind { get; }
    [Id(1)] public string ResourceId { get; }
    [Id(2)] public string? Revision { get; }

    public void Validate() => _ = new DurableInspectionReference(ResourceKind, ResourceId, Revision);
}

[GenerateSerializer]
[Alias("db.catalog.inspection-reference")]
public sealed record InspectionReference
{
    [JsonConstructor]
    public InspectionReference(
        InspectionReferenceKind kind,
        CatalogReference? catalog,
        NeuronId? neuron,
        SynapseReference? synapse,
        EntityId? entity,
        DurableInspectionReference? durable)
    {
        Kind = kind;
        Catalog = catalog;
        Neuron = neuron;
        Synapse = synapse;
        Entity = entity;
        Durable = durable;
        Validate();
    }

    [Id(0)] public InspectionReferenceKind Kind { get; }
    [Id(1)] public CatalogReference? Catalog { get; }
    [Id(2)] public NeuronId? Neuron { get; }
    [Id(3)] public SynapseReference? Synapse { get; }
    [Id(4)] public EntityId? Entity { get; }
    [Id(5)] public DurableInspectionReference? Durable { get; }

    public static InspectionReference ForCatalog(CatalogReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new(InspectionReferenceKind.CatalogDescriptor, reference, null, null, null, null);
    }

    public static InspectionReference ForNeuron(NeuronId neuron)
        => new(InspectionReferenceKind.Neuron, null, neuron, null, null, null);

    public static InspectionReference ForSynapse(NeuronId source, NeuronId target, string signalType)
        => new(
            InspectionReferenceKind.Synapse,
            null,
            null,
            new SynapseReference(source, target, signalType),
            null,
            null);

    public static InspectionReference ForEntity(EntityId entity)
        => new(InspectionReferenceKind.Entity, null, null, null, entity, null);

    public static InspectionReference ForDurableResource(DurableInspectionReference durable)
    {
        ArgumentNullException.ThrowIfNull(durable);
        return new(InspectionReferenceKind.DurableResource, null, null, null, null, durable);
    }

    public void Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(Kind));
        }

        var payloads = (Catalog is null ? 0 : 1) + (Neuron is null ? 0 : 1) +
            (Synapse is null ? 0 : 1) + (Entity is null ? 0 : 1) + (Durable is null ? 0 : 1);
        if (payloads != 1)
        {
            throw new ArgumentException("An inspection reference must carry exactly one payload.");
        }

        switch (Kind)
        {
            case InspectionReferenceKind.CatalogDescriptor when Catalog is not null:
                Catalog.Validate();
                break;
            case InspectionReferenceKind.Neuron when Neuron is { } neuron:
                CatalogContractValidation.ValidNeuron(neuron, nameof(Neuron));
                break;
            case InspectionReferenceKind.Synapse when Synapse is not null:
                _ = new SynapseReference(Synapse.Source, Synapse.Target, Synapse.SignalType);
                break;
            case InspectionReferenceKind.Entity when Entity is { } entity:
                CatalogContractValidation.ValidEntity(entity, nameof(Entity));
                break;
            case InspectionReferenceKind.DurableResource when Durable is not null:
                Durable.Validate();
                break;
            default:
                throw new ArgumentException("The inspection payload does not match its kind.");
        }
    }
}

[GenerateSerializer]
[Alias("db.catalog.inspection-status")]
public enum InspectionStatus
{
    Found = 0,
    StaleReference = 1,
    Retired = 2,
    NotFound = 3,
    UnsupportedReference = 4,
}

[GenerateSerializer]
[Alias("db.catalog.inspection-result")]
public sealed record InspectionResult
{
    [JsonConstructor]
    public InspectionResult(
        InspectionReference reference,
        InspectionStatus status,
        CatalogInspection? catalog,
        string? reason)
    {
        ArgumentNullException.ThrowIfNull(reference);
        reference.Validate();
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        if (catalog is not null)
        {
            if (reference.Kind != InspectionReferenceKind.CatalogDescriptor ||
                reference.Catalog != catalog.Reference)
            {
                throw new ArgumentException(
                    "A catalog inspection payload must match the requested catalog reference.",
                    nameof(catalog));
            }

            var expectedStatus = catalog.Status switch
            {
                CatalogInspectionStatus.Found => InspectionStatus.Found,
                CatalogInspectionStatus.StaleDescriptor => InspectionStatus.StaleReference,
                CatalogInspectionStatus.Retired => InspectionStatus.Retired,
                CatalogInspectionStatus.NotFound => InspectionStatus.NotFound,
                _ => throw new ArgumentOutOfRangeException(nameof(catalog)),
            };
            if (status != expectedStatus)
            {
                throw new ArgumentException(
                    "The general inspection status must match its catalog payload.",
                    nameof(status));
            }
        }
        else if (reference.Kind == InspectionReferenceKind.CatalogDescriptor &&
            status != InspectionStatus.UnsupportedReference)
        {
            throw new ArgumentNullException(
                nameof(catalog),
                "A supported catalog inspection result requires its catalog payload.");
        }

        if (status == InspectionStatus.UnsupportedReference && catalog is not null)
        {
            throw new ArgumentException("An unsupported inspection cannot carry a payload.", nameof(catalog));
        }

        Reference = reference;
        Status = status;
        Catalog = catalog;
        Reason = CatalogContractValidation.OptionalBounded(
            reason,
            nameof(reason),
            CatalogContractLimits.ReasonLength);
    }

    [Id(0)] public InspectionReference Reference { get; }
    [Id(1)] public InspectionStatus Status { get; }
    [Id(2)] public CatalogInspection? Catalog { get; }
    [Id(3)] public string? Reason { get; }
}
