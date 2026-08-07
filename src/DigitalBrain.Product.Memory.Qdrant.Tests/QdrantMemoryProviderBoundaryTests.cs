namespace DigitalBrain.Product.Memory.Qdrant.Tests;

public sealed class QdrantMemoryProviderBoundaryTests
{
    [Fact]
    public void ProviderKeepsStorageImplementationAndSdkOutOfTheMemoryContract()
    {
        var providerSurface = typeof(QdrantMemoryStoreFactory).Assembly
            .GetExportedTypes()
            .Select(static type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [nameof(QdrantMemoryOptions), nameof(QdrantMemoryStoreFactory)],
            providerSurface);

        var memoryReferences = typeof(IMemoryStore).Assembly
            .GetReferencedAssemblies()
            .Select(static assembly => assembly.Name)
            .ToArray();
        Assert.DoesNotContain("Qdrant.Client", memoryReferences, StringComparer.Ordinal);
    }
}
