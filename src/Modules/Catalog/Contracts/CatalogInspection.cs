using System.Text.Json.Serialization;

namespace DigitalBrain.Catalog;

[GenerateSerializer]
[Alias("db.catalog.catalog-inspection-status")]
public enum CatalogInspectionStatus
{
    Found = 0,
    StaleDescriptor = 1,
    Retired = 2,
    NotFound = 3,
}

[GenerateSerializer]
[Alias("db.catalog.catalog-inspection")]
public sealed record CatalogInspection
{
    [JsonConstructor]
    public CatalogInspection(
        CatalogReference Reference,
        CatalogInspectionStatus Status,
        CatalogDescriptor? Descriptor,
        CatalogAvailabilitySnapshot? Availability,
        string? Reason)
    {
        ArgumentNullException.ThrowIfNull(Reference);
        Reference.Validate();
        if (!Enum.IsDefined(Status))
        {
            throw new ArgumentOutOfRangeException(nameof(Status));
        }

        switch (Status)
        {
            case CatalogInspectionStatus.Found:
                var foundDescriptor = RequireExactDescriptor(Reference, Descriptor);
                ArgumentNullException.ThrowIfNull(Availability);
                if (foundDescriptor.Lifecycle == CatalogLifecycle.Retired)
                {
                    throw new ArgumentException("A retired descriptor must use retired inspection status.");
                }

                break;
            case CatalogInspectionStatus.Retired:
                var retiredDescriptor = RequireExactDescriptor(Reference, Descriptor);
                if (retiredDescriptor.Lifecycle != CatalogLifecycle.Retired)
                {
                    throw new ArgumentException("Retired inspection requires an exact retired descriptor.");
                }

                break;
            case CatalogInspectionStatus.StaleDescriptor:
            case CatalogInspectionStatus.NotFound:
                if (Descriptor is not null || Availability is not null)
                {
                    throw new ArgumentException(
                        "A stale or missing inspection cannot disclose descriptor or availability data.");
                }

                break;
        }

        this.Reference = Reference;
        this.Status = Status;
        this.Descriptor = Descriptor;
        this.Availability = Availability;
        this.Reason = CatalogContractValidation.OptionalBounded(
            Reason,
            nameof(Reason),
            CatalogContractLimits.ReasonLength);
    }

    [Id(0)] public CatalogReference Reference { get; }
    [Id(1)] public CatalogInspectionStatus Status { get; }
    [Id(2)] public CatalogDescriptor? Descriptor { get; }
    [Id(3)] public CatalogAvailabilitySnapshot? Availability { get; }
    [Id(4)] public string? Reason { get; }

    private static CatalogDescriptor RequireExactDescriptor(
        CatalogReference reference,
        CatalogDescriptor? descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptor.Validate();
        if (descriptor.Reference != reference)
        {
            throw new ArgumentException("Inspection details must match the requested exact catalog handle.");
        }

        return descriptor;
    }
}
