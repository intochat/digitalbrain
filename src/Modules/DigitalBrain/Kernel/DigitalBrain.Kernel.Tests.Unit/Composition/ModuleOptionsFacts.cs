using DigitalBrain.Contracts;
using DigitalBrain.Kernel;
using Microsoft.Extensions.Configuration;
using Orleans.Hosting;
using Xunit;

namespace DigitalBrain.Kernel.Tests.Unit;

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
        { ["DigitalBrain:Modules:FakeModule:Options:Endpoint"] = "/relative" }).Build();
        Assert.Throws<ArgumentException>(() => configuration.GetModuleOptions<FakeOptions>(nameof(FakeModule)));
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

    [Fact]
    public void TheModuleListBindsBesideAnOptionsKeyUnderTheSameSection()
    {
        var options = ModuleOptionsSerialization.Compile<FakeModule, FakeOptions>(new() { Flag = true });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(options.Configuration)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DigitalBrain:Modules:0"] = "First.Module, First",
                ["DigitalBrain:Modules:1"] = "Second.Module, Second",
            }).Build();
        Assert.Equal(["First.Module, First", "Second.Module, Second"], configuration.SelectedModuleNames());
        Assert.True(configuration.GetModuleOptions<FakeOptions>(nameof(FakeModule)).Flag);
    }

    [Fact]
    public void PopulateCopiesBoundOptionsOntoAnExistingInstance()
    {
        var definition = ModuleOptionsSerialization.Compile<FakeModule, FakeOptions>(new() { Flag = true, Endpoint = new("https://x.example/") });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(definition.Configuration).Build();
        var target = new FakeOptions();
        configuration.PopulateModuleOptions(nameof(FakeModule), target);
        Assert.True(target.Flag);
        Assert.Equal(new Uri("https://x.example/"), target.Endpoint);
    }

    private sealed class LeakyOptions : IModuleOptions
    {
        public string ApiKey { get; set; } = "";
        public void Validate() { }
    }

    private sealed class LeakyModule : IModule<LeakyOptions>
    {
        public void Configure(ISiloBuilder silo) { }
    }

    [Fact]
    public void CredentialShapedOptionPropertiesAreRefusedAtCompile()
    {
        var refusal = Assert.Throws<ArgumentException>(() => ModuleOptionsSerialization.Compile<LeakyModule, LeakyOptions>(new()));
        Assert.Contains("integrations registration", refusal.Message);
    }

    [Fact]
    public void BindRefusesAValueTheOptionTypeCannotConvert()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DigitalBrain:Modules:FakeModule:Options:Flag"] = "not-a-bool" }).Build();
        var failure = Assert.Throws<InvalidOperationException>(() => configuration.GetModuleOptions<FakeOptions>(nameof(FakeModule)));
        Assert.Contains(nameof(FakeModule), failure.Message);
    }

    private sealed class IgnoredOptions : IModuleOptions
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public string Endpoint { get; set; } = "";
        public void Validate() { }
    }

    private sealed class IgnoredModule : IModule<IgnoredOptions>
    {
        public void Configure(ISiloBuilder silo) { }
    }

    [Fact]
    public void WritableOptionsCannotDisappearWhenCompositionCrossesTheHostBoundary()
    {
        var refusal = Assert.Throws<ArgumentException>(() =>
            ModuleOptionsSerialization.Compile<IgnoredModule, IgnoredOptions>(new() { Endpoint = "https://example.org" }));
        Assert.Contains("IgnoredOptions.Endpoint", refusal.Message);
    }

    [Theory]
    [InlineData("DigitalBrain:Modules:0:Options")]
    [InlineData("DigitalBrain:Modules:Options")]
    [InlineData("DigitalBrain:Modules:A:B:Options")]
    [InlineData("DigitalBrain:Modules:A__B:Options")]
    public void OnlyTheOptionsKeyShapeIsExemptFromHostOwnedSettings(string key)
    {
        var definition = new ModuleDefinition(typeof(FakeModule), new Dictionary<string, string?> { [key] = "x" });
        Assert.Throws<ArgumentException>(() => ModuleSettingsValidation.ValidatePublicSettings([definition]));
    }

    [Theory]
    [InlineData("DigitalBrain:Modules:X:Options")]
    [InlineData("DigitalBrain:Modules:X:Options:Endpoint")]
    [InlineData("DigitalBrain:Modules:X:Options:Bindings:a:0")]
    public void FlattenedOptionKeysArePublicSettings(string key)
        => ModuleSettingsValidation.ValidatePublicSettings(
            [new(typeof(FakeModule), new Dictionary<string, string?> { [key] = "x" })]);
}
