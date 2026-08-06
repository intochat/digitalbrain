using System.Reflection;

namespace DigitalBrain;

public sealed class ModuleBoundaryTests
{
    [Fact]
    public void RejectsAnOrleansAssemblyReference()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => ModuleAssemblyBoundary.Validate(
            "sample.module",
            [new AssemblyName("Orleans.Core")]));

        Assert.Contains("Only DigitalBrain.Hosting", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnAccessAssemblyReference()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => ModuleAssemblyBoundary.Validate(
            "sample.module",
            [new AssemblyName("DigitalBrain.Access")]));

        Assert.Contains("journal-read capabilities", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAHostingAssemblyReference()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => ModuleAssemblyBoundary.Validate(
            "sample.module",
            [new AssemblyName("DigitalBrain.Hosting")]));

        Assert.Contains("durable adapter", failure.Message, StringComparison.Ordinal);
    }
}
