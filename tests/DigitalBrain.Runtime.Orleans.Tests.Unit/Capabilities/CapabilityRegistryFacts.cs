using DigitalBrain.Capabilities;
using DigitalBrain.Capabilities.Identity;
using DigitalBrain.Capabilities.Secrets;
using DigitalBrain.Capabilities.Storage;
using DigitalBrain.Runtime.Orleans.Capabilities;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Capabilities;

public sealed class CapabilityRegistryFacts
{
    [Fact]
    public void Seeded_with_storage_secrets_and_identity_majors_one()
    {
        var registry = new CapabilityRegistry();

        Assert.True(registry.Known(Capability.Storage.V1));
        Assert.False(registry.Known(new CapabilityId("storage", 2)));
        Assert.Equal(typeof(IStorage), registry.ContractOf(Capability.Storage.V1));
        Assert.Equal(typeof(ISecrets), registry.ContractOf(Capability.Secrets.V1));
        Assert.Equal(typeof(IIdentity), registry.ContractOf(Capability.Identity.V1));
        Assert.True(registry.IsSingle(Capability.Secrets.V1));
        Assert.True(registry.IsSingle(Capability.Identity.V1));
        Assert.False(registry.IsSingle(Capability.Storage.V1));
    }

    [Fact]
    public void Accepted_binding_is_listed_and_maps_back_to_its_capability()
    {
        var registry = new CapabilityRegistry();
        registry.AcceptBinding(Capability.Storage.V1, typeof(ILegacyThing));

        Assert.Contains(typeof(ILegacyThing), registry.BindingsOf(Capability.Storage.V1));
        Assert.Equal(Capability.Storage.V1, registry.CapabilityOf(typeof(ILegacyThing)));
        Assert.Equal(Capability.Storage.V1, registry.CapabilityOf(typeof(IStorage)));
        Assert.Equal(Capability.Identity.V1, registry.CapabilityOf(typeof(IGrants)));
    }

    [Fact]
    public void Accepting_a_binding_for_an_unknown_capability_throws()
    {
        Assert.Throws<InvalidOperationException>(() => new CapabilityRegistry().AcceptBinding(new CapabilityId("storage", 9), typeof(ILegacyThing)));
    }

    public interface ILegacyThing;
}
