namespace DigitalBrain;

public sealed class CatalogBootTests
{
    [Fact]
    public void UsesExplicitOrleansGrainTypesAsNeuronKinds()
    {
        var catalog = Catalog.Build([typeof(CatalogLeft), typeof(CatalogRight)]);

        Assert.True(catalog.HasNeuronKind("mechanics.catalog.left"));
        Assert.True(catalog.HasNeuronKind("mechanics.catalog.right"));
    }

    [Fact]
    public void RejectsAnImplicitNeuronKind()
    {
        var failure = Assert.Throws<InvalidOperationException>(
            () => Catalog.Build([typeof(CatalogImplicit)]));

        Assert.Contains("[GrainType", failure.Message, StringComparison.Ordinal);
    }
}

public sealed record CatalogPulse : Synapse;

[GrainType("mechanics.catalog.left")]
public sealed class CatalogLeft : Neuron, INeuron<CatalogPulse>
{
    public Task HandleAsync(CatalogPulse synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}

[GrainType("mechanics.catalog.right")]
public sealed class CatalogRight : Neuron, INeuron<CatalogPulse>
{
    public Task HandleAsync(CatalogPulse synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class CatalogImplicit : Neuron, INeuron<CatalogPulse>
{
    public Task HandleAsync(CatalogPulse synapse, CancellationToken cancellationToken) => Task.CompletedTask;
}
