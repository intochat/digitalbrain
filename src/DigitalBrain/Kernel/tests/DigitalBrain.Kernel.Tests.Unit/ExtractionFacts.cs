using System.Reflection;
using DigitalBrain;
using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Kernel.Tests.Unit;

public sealed class ExtractionFacts
{
    [Fact]
    public void TheDigitalBrainAssemblyExportsOnlyTheWords()
    {
        var exported = typeof(INeuron).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == "DigitalBrain" && !type.Name.Contains('_', StringComparison.Ordinal))
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal("DigitalBrain", typeof(INeuron).Assembly.GetName().Name);
        Assert.Equal(
            ["BrainSnapshot", "EstablishBrain", "IBrain", "IDigitalBrain", "INeuron", "INeuronObserver", "ISignalSubscription`1", "Signal"],
            exported);
    }

    [Fact]
    public void AnEmptyCompositionStillExposesTheWordsAndTheKernelContracts()
    {
        var assemblies = new ModuleInventory([]).ContractAssemblies().Select(assembly => assembly.GetName().Name).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(["DigitalBrain", "DigitalBrain.Contracts"], assemblies);
        Assert.DoesNotContain(new ModuleInventory([]).ContractAssemblies(), PlatformAssemblyAttribute.IsPlatform);
    }
}
