using System.Reflection;
using DigitalBrain;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests.Architecture;

public sealed class PublicPackageIdentityTests
{
    private static readonly Assembly[] PublicAssemblies =
    [
        typeof(INeuron).Assembly,
        typeof(DigitalBrainClient).Assembly,
        typeof(Neuron).Assembly
    ];

    [Fact]
    public void Public_assemblies_use_digital_brain_package_names()
    {
        Assert.Equal(
            ["DigitalBrain.Abstractions", "DigitalBrain.Client", "DigitalBrain.Kernel"],
            PublicAssemblies.Select(assembly => assembly.GetName().Name).ToArray());
    }

    [Fact]
    public void Public_types_live_in_digital_brain_namespaces()
    {
        var handwrittenTypes = PublicAssemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.Namespace?.StartsWith("OrleansCodeGen", StringComparison.Ordinal) is not true);

        foreach (var type in handwrittenTypes)
            Assert.True(
                type.Namespace is "DigitalBrain" ||
                type.Namespace?.StartsWith("DigitalBrain.", StringComparison.Ordinal) is true,
                $"{type.FullName} is outside the DigitalBrain namespace family.");
    }

    [Fact]
    public void Public_assemblies_expose_the_durable_foundation_contracts()
    {
        Assert.Equal("DigitalBrain.Abstractions", typeof(BrainOwnerId).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Abstractions", typeof(ExternalOperation).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Abstractions", typeof(NeuronNotification).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Kernel", typeof(DigitalBrain.Kernel.Quadrant).Assembly.GetName().Name);
        Assert.Equal("DigitalBrain.Kernel", typeof(NeuronDurableState).Assembly.GetName().Name);
    }
}
