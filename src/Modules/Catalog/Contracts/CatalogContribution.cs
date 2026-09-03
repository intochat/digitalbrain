using System.Text.Json.Serialization;

namespace DigitalBrain.Catalog;

[GenerateSerializer]
[Alias("db.catalog.contribution")]
public sealed record CatalogContribution
{
    [JsonConstructor]
    public CatalogContribution(string moduleTypeName, IReadOnlyList<CatalogDescriptor>? descriptors)
    {
        ModuleTypeName = CatalogContractValidation.Required(moduleTypeName, nameof(moduleTypeName));
        var copiedDescriptors = CatalogContractValidation.ReadOnlyCopy(descriptors);
        foreach (var descriptor in copiedDescriptors)
        {
            ArgumentNullException.ThrowIfNull(descriptor);
            descriptor.Validate();
        }

        Descriptors = copiedDescriptors;
    }

    [Id(0)] public string ModuleTypeName { get; }
    [Id(1)] public IReadOnlyList<CatalogDescriptor> Descriptors { get; }
}
