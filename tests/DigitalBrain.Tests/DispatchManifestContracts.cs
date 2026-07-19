using System.Reflection;
using DigitalBrain;
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

        var reflected = Probed.GetTypes()
            .SelectMany(neuron => neuron.GetInterfaces()
                .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IHandle<>))
                .Select(contract => (Neuron: DisplayName(neuron), Synapse: DisplayName(contract.GetGenericArguments()[0]))))
            .ToHashSet();

        var declared = manifest.Handlers.Select(entry => (entry.Neuron, entry.Synapse)).ToHashSet();

        Assert.Equal(reflected, declared);
    }

    [Fact]
    public void ManifestListsEveryEmissionReflectionCanFind()
    {
        Assert.True(SynapseWiring.TryGetManifest(Probed, out var manifest));

        var reflected = Probed.GetTypes()
            .SelectMany(neuron => neuron.GetInterfaces()
                .Where(contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEmit<>))
                .Select(contract => (Neuron: DisplayName(neuron), Synapse: DisplayName(contract.GetGenericArguments()[0]))))
            .ToHashSet();

        var declared = manifest.Emissions.Select(entry => (entry.Neuron, entry.Synapse)).ToHashSet();

        Assert.Equal(reflected, declared);
    }

    [Fact]
    public void HandlerLookupFallsBackToReflectionForTypesNoManifestDeclares()
    {
        var handled = SynapseWiring.HandledSynapseTypes(typeof(LateRegisteredNeuron));

        Assert.Contains(typeof(LateSynapse), handled);
    }

    private static string DisplayName(Type type) => type.FullName!.Replace('+', '.');

    private sealed record LateSynapse : Synapse;

    private sealed class LateRegisteredNeuron : IHandle<LateSynapse>
    {
        public Task HandleAsync(LateSynapse synapse, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
