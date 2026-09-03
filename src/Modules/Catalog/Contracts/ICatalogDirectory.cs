namespace DigitalBrain.Catalog;

[Alias("db.catalog.directory")]
public interface ICatalogDirectory : IGrainWithStringKey
{
    [Alias(nameof(Discover))]
    Task<DiscoveryResult> Discover(
        DiscoveryQuery query,
        CancellationToken cancellationToken = default);

    [Alias(nameof(Inspect))]
    Task<CatalogInspection> Inspect(
        CatalogReference reference,
        CancellationToken cancellationToken = default);
}
