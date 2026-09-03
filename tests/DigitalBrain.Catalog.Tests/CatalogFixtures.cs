using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Catalog.Tests;

internal static class CatalogFixtures
{
    private static readonly OwnerId Owner = new("owner-a");

    public static NeuronId Neuron(string name) => new("fixture-neuron", Owner, name);

    public static EntityId Entity(string name) => new("fixture-entity", Owner, name);

    public static CatalogDescriptor ModuleDescriptor(string id = "module.fixture")
    {
        var reference = new CatalogReference(
            CatalogScope.Platform,
            new CatalogSourceReference("platform-module", id),
            new CatalogEntryId(id),
            "static-v1:fixture",
            new CatalogFingerprint(new string('a', 64)));

        return new CatalogDescriptor(
            reference,
            CatalogEntryKind.Module,
            CatalogLifecycle.Active,
            CatalogVisibility.Discoverable,
            CatalogConfigurationState.Configured,
            "Fixture module",
            "A module used to exercise catalog contracts.",
            CatalogDiscoveryText.Empty,
            CatalogTypedReference.ForStable(CatalogTargetKind.Module, id),
            neuron: null,
            signal: null,
            capability: null,
            operation: null);
    }
}
