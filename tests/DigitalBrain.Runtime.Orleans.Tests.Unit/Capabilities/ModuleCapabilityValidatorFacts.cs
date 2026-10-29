using DigitalBrain.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities;
using DigitalBrain.Testing.Conformance.Secrets;
using DigitalBrain.Testing.Conformance.Storage;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Capabilities;

public sealed class ModuleCapabilityValidatorFacts
{
    private static readonly CapabilityRegistry Registry = new();

    [Fact]
    public void Unknown_major_is_refused_naming_module_and_capability()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModuleCapabilityValidator.Validate(Registry, [typeof(StorageTwoModule)]));

        Assert.Contains("Module 'test.storage2' provides unknown capability storage@2.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Provides_without_an_assignable_neuron_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => ModuleCapabilityValidator.Validate(Registry, [new ModuleCandidate("x", [Capability.Storage.V1], [typeof(InMemorySecrets)])]));

        Assert.Contains("Module 'x' provides storage@1 but contains no neuron implementing IStorage.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_modules_providing_a_single_connection_capability_are_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => ModuleCapabilityValidator.Validate(Registry,
        [
            new ModuleCandidate("a", [Capability.Secrets.V1], [typeof(InMemorySecrets)]),
            new ModuleCandidate("b", [Capability.Secrets.V1], [typeof(InMemorySecrets)]),
        ]));

        Assert.Contains("'a'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'b'", error.Message, StringComparison.Ordinal);
        Assert.Contains("secrets@1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Storage_modules_with_an_in_memory_storage_neuron_pass_and_many_are_fine()
    {
        ModuleCapabilityValidator.Validate(Registry,
        [
            new ModuleCandidate("a", [Capability.Storage.V1], [typeof(InMemoryStorage)]),
            new ModuleCandidate("b", [Capability.Storage.V1], [typeof(InMemoryStorage)]),
        ]);
    }

    [Fact]
    public void Accepted_binding_satisfies_the_neuron_check()
    {
        var registry = new CapabilityRegistry();
        registry.AcceptBinding(Capability.Storage.V1, typeof(ILegacy));

        ModuleCapabilityValidator.Validate(registry, [new ModuleCandidate("legacy", [Capability.Storage.V1], [typeof(LegacyNeuron)])]);
    }

    [Fact]
    public void Module_types_without_provides_pass()
    {
        ModuleCapabilityValidator.Validate(Registry, [typeof(PlainModule)]);
    }

    public interface ILegacy;

    private sealed class LegacyNeuron : ILegacy;
}

[Module("test.storage2")]
public sealed class StorageTwoModule : Module
{
    public static ModuleManifest Manifest { get; } = new([new CapabilityId("storage", 2)], [], [], [], []);
}

[Module("test.plain")]
public sealed class PlainModule : Module
{
    public static ModuleManifest Manifest { get; } = new([], [], [], [], []);
}
