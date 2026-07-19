using System.Reflection;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class DispatchManifestContracts
{
    private static readonly Assembly Probed = typeof(DispatchManifestContracts).Assembly;

    [Fact]
    public void ManifestIsGeneratedForThisAssembly()
        => Assert.True(SynapseWiring.TryGetManifest(Probed, out _));

    [Fact]
    public void ManifestListsEveryHandlerReflectionCanFind()
    {
        Assert.True(SynapseWiring.TryGetManifest(Probed, out var manifest));

        var declared = manifest.Handlers.Select(entry => (entry.Neuron, entry.Synapse)).ToHashSet();

        Assert.NotEmpty(declared);
        Assert.Equal(Reflected(typeof(IHandle<>)), declared);
    }

    [Fact]
    public void ManifestListsEveryEmissionReflectionCanFind()
    {
        Assert.True(SynapseWiring.TryGetManifest(Probed, out var manifest));

        var declared = manifest.Emissions.Select(entry => (entry.Neuron, entry.Synapse)).ToHashSet();

        Assert.NotEmpty(declared);
        Assert.Equal(Reflected(typeof(IEmit<>)), declared);
    }

    [Fact]
    public void HandlerLookupWorksForAssembliesThatCarryNoManifest()
    {
        Assert.False(SynapseWiring.TryGetManifest(typeof(object).Assembly, out _));

        var handled = SynapseWiring.HandledSynapseTypes(typeof(ProbeNeuron));

        Assert.Equal([typeof(ProbeSynapse)], handled);
    }

    [Fact]
    public void TheKernelCarriesAGeneratedManifestSoConsumersInheritTheGenerator()
        => Assert.True(SynapseWiring.TryGetManifest(typeof(Neuron).Assembly, out _));

    [Fact]
    public void HandlerLookupIsEmptyForATypeThatHandlesNothing()
        => Assert.Empty(SynapseWiring.HandledSynapseTypes(typeof(DispatchManifestContracts)));

    private static HashSet<(string Neuron, string Synapse)> Reflected(Type contractDefinition)
        => Probed.GetTypes()
            .SelectMany(neuron => neuron.GetInterfaces()
                .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == contractDefinition)
                .Select(contract => (Neuron: DisplayName(neuron), Synapse: DisplayName(contract.GetGenericArguments()[0]))))
            .ToHashSet();

    private static string DisplayName(Type type) => type.FullName!.Replace('+', '.');

    private sealed record ProbeSynapse : Synapse;

    private sealed class ProbeNeuron : IHandle<ProbeSynapse>, IEmit<ProbeSynapse>
    {
        public Task HandleAsync(ProbeSynapse synapse, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
