using DigitalBrain.Capabilities.Compute;
using Microsoft.Extensions.DependencyInjection;

namespace DigitalBrain.Capabilities.Compute.Tests.Unit;

public sealed class PriceListRegistryFacts
{
    [Fact]
    public void RegistryResolvesOperationToItsModule()
    {
        var services = new ServiceCollection();
        services.AddModulePriceList<SamplePriceList>("gmail",
        [
            new ExternalOperationDescriptor("gmail.messages.list", "google", "request"),
        ]);
        using var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<ModulePriceListRegistry>();

        var owner = registry.Resolve("gmail.messages.list");
        Assert.Equal("gmail", owner.ModuleId);
        Assert.Equal(typeof(SamplePriceList), owner.PriceListType);

        var descriptor = registry.GetOperation("gmail.messages.list");
        Assert.Equal("gmail.messages.list", descriptor.Name);
        Assert.Equal("google", descriptor.Integration);
        Assert.Equal("request", descriptor.Unit);
    }

    [Fact]
    public void UnknownOperationThrows()
    {
        var registry = new ModulePriceListRegistry([]);
        Assert.Throws<InvalidOperationException>(() => registry.Resolve("unknown.op"));
        Assert.Throws<InvalidOperationException>(() => registry.GetOperation("unknown.op"));
    }

    private sealed class SamplePriceList : IPriceList
    {
        public long? MicrosPerUnit(CostKind kind, string item) => 10;
    }
}
