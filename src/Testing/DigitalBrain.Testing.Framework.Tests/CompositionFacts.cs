using DigitalBrain.Core;

namespace DigitalBrain.Tests;

public sealed class CompositionFacts
{
    [Fact]
    public void DefinitionCopiesConfigurationAndDeduplicatesIdenticalModules()
    {
        var values = new Dictionary<string, string?> { ["Feature:Enabled"] = "false" };
        var first = new ModuleDefinition(typeof(ExampleModule), values);
        values["Feature:Enabled"] = "true";
        Assert.Equal("false", first.Configuration["Feature:Enabled"]);
        Assert.Single(ModuleComposition.Resolve([first, new(typeof(ExampleModule), new Dictionary<string, string?> { ["Feature:Enabled"] = "false" })]));
    }

    [Fact]
    public void ConflictingDefinitionsFail()
    {
        var first = new ModuleDefinition(typeof(ExampleModule), new Dictionary<string, string?> { ["Value"] = "1" });
        var second = new ModuleDefinition(typeof(ExampleModule), new Dictionary<string, string?> { ["Value"] = "2" });
        Assert.Throws<InvalidOperationException>(() => ModuleComposition.Resolve([first, second]));
    }

    [Fact]
    public void NonModuleTypeFailsImmediately()
        => Assert.Throws<ArgumentException>(() => new ModuleDefinition(typeof(string)));

    public sealed class ExampleModule : IModule
    {
        public void Configure(Orleans.Hosting.ISiloBuilder silo) { }
    }
}
