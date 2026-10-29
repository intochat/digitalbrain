using System.Reflection;
using DigitalBrain.Capabilities;
using DigitalBrain.Runtime.Orleans.Capabilities;

namespace DigitalBrain.Runtime.Orleans.Tests.Unit.Capabilities;

public sealed class KitCoverageFacts
{
    private static readonly Dictionary<CapabilityId, Type> Kits = new() { [Capability.Storage.V1] = typeof(FakeKit<>) };

    [Fact]
    public void Module_whose_test_assembly_has_no_kit_subclass_is_reported()
    {
        var gap = Assert.Single(KitCoverage.Missing(
            [typeof(KitStorageModule)], new Dictionary<Type, Assembly> { [typeof(KitStorageModule)] = typeof(object).Assembly }, Kits));

        Assert.Equal("test.kit-storage", gap.ModuleId);
        Assert.Equal(Capability.Storage.V1, gap.Capability);
    }

    [Fact]
    public void Module_without_a_test_assembly_is_reported()
    {
        Assert.Single(KitCoverage.Missing([typeof(KitStorageModule)], new Dictionary<Type, Assembly>(), Kits));
    }

    [Fact]
    public void Module_whose_test_assembly_has_a_concrete_kit_subclass_passes()
    {
        Assert.Empty(KitCoverage.Missing(
            [typeof(KitStorageModule)], new Dictionary<Type, Assembly> { [typeof(KitStorageModule)] = typeof(KitCoverageFacts).Assembly }, Kits));
    }

    [Fact]
    public void Abstract_kit_subclass_alone_does_not_count()
    {
        Assert.False(KitCoverage.HasKitSubclass([typeof(AbstractKitSubject)], typeof(FakeKit<>)));
        Assert.True(KitCoverage.HasKitSubclass([typeof(AbstractKitSubject), typeof(ConcreteKitSubject)], typeof(FakeKit<>)));
    }

    [Fact]
    public void Module_providing_nothing_needs_no_kit()
    {
        Assert.Empty(KitCoverage.Missing([typeof(PlainModule)], new Dictionary<Type, Assembly>(), Kits));
    }
}

#pragma warning disable CA1812, S2094
public abstract class FakeKit<T>;

public abstract class AbstractKitSubject : FakeKit<string>;

public sealed class ConcreteKitSubject : FakeKit<string>;
#pragma warning restore CA1812, S2094

[Module("test.kit-storage")]
public sealed class KitStorageModule : Module
{
    public static ModuleManifest Manifest { get; } = new([Capability.Storage.V1], [], [], [], []);
}
