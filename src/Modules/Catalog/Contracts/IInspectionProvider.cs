using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Catalog;

public readonly record struct InspectionProviderKey
{
    private readonly bool _initialized;

    private InspectionProviderKey(InspectionReferenceKind kind, string? durableResourceKind)
    {
        Kind = kind;
        DurableResourceKind = durableResourceKind;
        _initialized = true;
    }

    public InspectionReferenceKind Kind { get; }
    public string? DurableResourceKind { get; }

    public static InspectionProviderKey For(InspectionReferenceKind kind)
    {
        if (!Enum.IsDefined(kind) || kind == InspectionReferenceKind.DurableResource)
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        return new(kind, null);
    }

    public static InspectionProviderKey ForDurable(string resourceKind)
        => new(
            InspectionReferenceKind.DurableResource,
            CatalogContractValidation.Required(resourceKind, nameof(resourceKind)).ToLowerInvariant());

    public void Validate()
    {
        if (!_initialized)
        {
            throw new ArgumentException("An inspection provider key must be created by a validating factory.");
        }

        if (Kind == InspectionReferenceKind.DurableResource)
        {
            if (!string.Equals(
                    DurableResourceKind,
                    CatalogContractValidation.Required(DurableResourceKind, nameof(DurableResourceKind))
                        .ToLowerInvariant(),
                    StringComparison.Ordinal))
            {
                throw new ArgumentException("A durable provider key must use a normalized resource kind.");
            }

            return;
        }

        if (!Enum.IsDefined(Kind) || DurableResourceKind is not null)
        {
            throw new ArgumentException("An ordinary inspection provider key cannot carry a resource kind.");
        }
    }
}

public interface IInspectionProvider
{
    InspectionProviderKey Key { get; }

    Task<InspectionResult> InspectAsync(
        OwnerId owner,
        InspectionReference reference,
        CancellationToken cancellationToken);
}
