using DigitalBrain.Core;
using Microsoft.Extensions.Configuration;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class ModuleOptionsFacts
{
    private sealed class FakeOptions : IModuleOptions
    {
        public Uri Endpoint { get; set; } = new("https://default.example/");
        public Dictionary<string, string> Bindings { get; set; } = [];
        public bool Flag { get; set; }
        public void Validate() { if (!Endpoint.IsAbsoluteUri) { throw new ArgumentException("Endpoint must be absolute."); } }
    }

    private sealed class FakeModule : IModule<FakeOptions>
    {
        public void Configure(ISiloBuilder silo) { }
    }

    [Fact]
    public void OptionsSurviveTheCompileAndBindRoundTripByValue()
    {
        var options = new FakeOptions { Endpoint = new("https://x.example/api"), Flag = true, Bindings = { ["a"] = "1" } };
        var definition = ModuleOptionsSerialization.Compile<FakeModule, FakeOptions>(options);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(definition.Configuration).Build();
        var bound = configuration.GetModuleOptions<FakeOptions>(nameof(FakeModule));
        Assert.Equal(options.Endpoint, bound.Endpoint);
        Assert.True(bound.Flag);
        Assert.Equal("1", bound.Bindings["a"]);
    }

    [Fact]
    public void ValidationRunsAtCompileAndAtBind()
    {
        Assert.Throws<ArgumentException>(() =>
            ModuleOptionsSerialization.Compile<FakeModule, FakeOptions>(new() { Endpoint = new Uri("/relative", UriKind.Relative) }));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["DigitalBrain:Modules:FakeModule:Options"] = """{"Endpoint":"/relative"}""" }).Build();
        Assert.ThrowsAny<Exception>(() => configuration.GetModuleOptions<FakeOptions>(nameof(FakeModule)));
    }

    [Fact]
    public void AbsentOptionsBindToValidatedDefaults()
    {
        var configuration = new ConfigurationBuilder().Build();
        Assert.Equal(new Uri("https://default.example/"), configuration.GetModuleOptions<FakeOptions>(nameof(FakeModule)).Endpoint);
    }

    [Fact]
    public void CompiledOptionsPassPublicSettingsValidation()
    {
        var definition = ModuleOptionsSerialization.Compile<FakeModule, FakeOptions>(new());
        ModuleSettingsValidation.ValidatePublicSettings([definition]);
    }
}
